using Luxel.Controls;
using Luxel.Scripting.Roslyn.Web;

namespace Luxel.Scripting.Roslyn.Web.Tests;

public sealed class WebScriptPipelineTests
{
    [Fact]
    public void CompileAndExecute_ReturnsWidget()
    {
        WebScriptCompilation compilation = CreateCompiler().Compile("return (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;");

        Assert.True(compilation.Success, Format(compilation.Diagnostics));
        Assert.NotEmpty(compilation.PeImage!);
        Assert.NotEmpty(compilation.PdbImage!);

        WebScriptExecution execution = new WebScriptExecutor().Execute(compilation.PeImage!, compilation.PdbImage!);
        Assert.True(execution.Success, execution.Failure?.Message);
        Assert.IsType<Text>(execution.Widget);
    }

    [Fact]
    public void Compile_IsDeterministicForSameInput()
    {
        WebScriptCompiler compiler = CreateCompiler();
        WebScriptCompilation first = compiler.Compile("return (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;");
        WebScriptCompilation second = compiler.Compile("return (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;");

        Assert.True(first.Success, Format(first.Diagnostics));
        Assert.True(second.Success, Format(second.Diagnostics));
        Assert.Equal(first.PeImage, second.PeImage);
        Assert.Equal(first.PdbImage, second.PdbImage);
    }

    [Fact]
    public void CompileDiagnostic_MapsToBodyLine()
    {
        WebScriptCompilation compilation = CreateCompiler().Compile("var value = 1;\nreturn missingName;");

        Assert.False(compilation.Success);
        WebScriptDiagnostic diagnostic = Assert.Single(compilation.Diagnostics, d => d.Severity == WebScriptDiagnosticSeverity.Error);
        Assert.Equal("CS0103", diagnostic.Id);
        Assert.Equal(2, diagnostic.Line);
        Assert.NotNull(diagnostic.Column);
    }

    [Fact]
    public void RuntimeFailure_IsStructuredAndMapsLine()
    {
        WebScriptCompilation compilation = CreateCompiler().Compile("var value = 1;\nthrow new InvalidOperationException(\"boom\");");
        Assert.True(compilation.Success, Format(compilation.Diagnostics));

        WebScriptExecution execution = new WebScriptExecutor().Execute(compilation.PeImage!, compilation.PdbImage!);

        Assert.False(execution.Success);
        Assert.Equal("runtime", execution.Failure?.Kind);
        Assert.Equal(typeof(InvalidOperationException).FullName, execution.Failure?.ExceptionType);
        Assert.Equal("boom", execution.Failure?.Message);
        Assert.Equal(2, execution.Failure?.Line);
    }

    [Fact]
    public void Executor_RejectsEmptyAssemblyAsStructuredFailure()
    {
        WebScriptExecution execution = new WebScriptExecutor().Execute(ReadOnlyMemory<byte>.Empty);

        Assert.False(execution.Success);
        Assert.Equal("load", execution.Failure?.Kind);
    }

    [Fact]
    public void EntryValidator_RejectsAssemblyWithoutFixedProgramType()
    {
        WebScriptExecution execution = new WebScriptExecutor().ExecuteAssembly(typeof(WebScriptPipelineTests).Assembly);

        Assert.False(execution.Success);
        Assert.Equal("entry-point", execution.Failure?.Kind);
        Assert.Contains(WebScriptCompiler.EntryTypeName, execution.Failure?.Message);
    }

    [Theory]
    [InlineData("#r \"nuget:Example,1.0.0\"\nreturn (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;")]
    [InlineData("#load \"other.csx\"\nreturn (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;")]
    public void Policy_RejectsHostControlledDirectives(string source)
    {
        WebScriptCompilation compilation = CreateCompiler().Compile(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, d => d.Id == "LUXWEB002" && d.Line == 1);
    }

    [Fact]
    public void Policy_RejectsUtf8SizeLimit()
    {
        var compiler = new WebScriptCompiler(References(), new WebScriptPolicy(MaxSourceBytes: 4));

        WebScriptCompilation compilation = compiler.Compile("ééé");

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, d => d.Id == "LUXWEB001");
    }

    private static WebScriptCompiler CreateCompiler() => new(References());

    private static IReadOnlyList<MetadataReferenceImage> References()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trusted is not null)
            foreach (string path in trusted.Split(Path.PathSeparator)) paths.Add(path);

        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")) paths.Add(path);

        var references = new List<MetadataReferenceImage>();
        foreach (string path in paths.OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                references.Add(new(Path.GetFileName(path), File.ReadAllBytes(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A transient/unreadable optional runtime image is not required by these tests.
            }
        }
        return references;
    }

    private static string Format(IReadOnlyList<WebScriptDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Id} ({d.Line},{d.Column}): {d.Message}"));
}
