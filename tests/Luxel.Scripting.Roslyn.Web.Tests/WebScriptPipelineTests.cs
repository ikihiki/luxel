using Luxel.Controls;
using Luxel.Scripting;
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
    public void GeneratedLog_ForwardsMessagesToHostSink()
    {
        var messages = new List<string>();
        WebScriptOutput.SetSink(messages.Add);
        try
        {
            WebScriptCompilation compilation = CreateCompiler().Compile("Log(\"Button clicked.\"); return (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;");
            Assert.True(compilation.Success, Format(compilation.Diagnostics));

            WebScriptExecution execution = new WebScriptExecutor().Execute(compilation.PeImage!, compilation.PdbImage!);

            Assert.True(execution.Success, execution.Failure?.Message);
            Assert.Equal(["Button clicked."], messages);
        }
        finally
        {
            WebScriptOutput.SetSink(null);
        }
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
    public void Compile_ProjectCanReferenceSupportCSharpAndIgnoresSlang()
    {
        var project = new WebScriptProject(
            new WebScriptDocument("entry.csx", "return Support.Create();"),
            [
                new WebScriptDocument(
                    "support.cs",
                    "using System; using Luxel.Controls; using Luxel.UI; public static class Support { public static Widget Create() => (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!; }"),
                new WebScriptDocument("shader.slang", "this is not C#"),
            ]);

        WebScriptCompilation compilation = CreateCompiler().Compile(project);

        Assert.True(compilation.Success, Format(compilation.Diagnostics));
        WebScriptExecution execution = new WebScriptExecutor().Execute(compilation.PeImage!, compilation.PdbImage!);
        Assert.True(execution.Success, execution.Failure?.Message);
        Assert.IsType<Text>(execution.Widget);
    }

    [Fact]
    public void Compile_SupportDiagnosticPreservesFileNameAndLine()
    {
        var project = new WebScriptProject(
            new WebScriptDocument("entry.csx", "return Support.Create();"),
            [new WebScriptDocument("support.cs", "public static class Support\n{\n    public static Luxel.UI.Widget Create() => missingName;\n}")]);

        WebScriptCompilation compilation = CreateCompiler().Compile(project);

        WebScriptDiagnostic diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "CS0103");
        Assert.Equal("support.cs", diagnostic.FileName);
        Assert.Equal(3, diagnostic.Line);
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

    [Theory]
    [InlineData("while (true) { }")]
    [InlineData("for (;;) { }")]
    public void Policy_RejectsStaticallyUnboundedLoops(string source)
    {
        WebScriptCompilation compilation = CreateCompiler().Compile(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "LUXWEB003");
    }

    [Fact]
    public void Policy_RejectsUtf8SizeLimit()
    {
        var compiler = new WebScriptCompiler(References(), new WebScriptPolicy(MaxSourceBytes: 4));

        WebScriptCompilation compilation = compiler.Compile("ééé");

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, d => d.Id == "LUXWEB001");
    }

    [Fact]
    public async Task CommonExecutor_MapsCompilationAndRuntimeResults()
    {
        WebScriptCompiler compiler = CreateCompiler();
        var executor = new RoslynWebScriptExecutor(
            new InProcessWebScriptWorkerController(compiler, new WebScriptExecutor()));

        ScriptExecutionResult success = await executor.ExecuteAsync(new Luxel.Scripting.ScriptExecutionRequest
        {
            Source = "return (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!;",
        });
        Assert.Equal(Luxel.Scripting.ScriptExecutionOutcome.Succeeded, success.Outcome);
        Assert.Equal(typeof(Text).FullName, success.ReturnValue);

        ScriptExecutionResult compilation = await executor.ExecuteAsync(new Luxel.Scripting.ScriptExecutionRequest
        {
            Source = "return missingName;",
        });
        Assert.Equal(Luxel.Scripting.ScriptExecutionOutcome.CompilationFailed, compilation.Outcome);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "CS0103");

        ScriptExecutionResult runtime = await executor.ExecuteAsync(new Luxel.Scripting.ScriptExecutionRequest
        {
            Source = "throw new InvalidOperationException(\"boom\");",
        });
        Assert.Equal(Luxel.Scripting.ScriptExecutionOutcome.RuntimeFailed, runtime.Outcome);
        Assert.Equal("boom", runtime.Failure?.Message);
    }

    [Fact]
    public async Task CommonExecutor_CompilesCSharpFilesExcludesSlangAndPreservesFileDiagnostics()
    {
        WebScriptCompiler compiler = CreateCompiler();
        var executor = new RoslynWebScriptExecutor(
            new InProcessWebScriptWorkerController(compiler, new WebScriptExecutor()));

        ScriptExecutionResult success = await executor.ExecuteAsync(new ScriptExecutionRequest
        {
            FileName = "entry.csx",
            Source = "return Support.Create();",
            Files =
            [
                new ScriptDocument
                {
                    FileName = "support.cs",
                    Source = "using System; using Luxel.Controls; using Luxel.UI; public static class Support { public static Widget Create() => (Widget)Activator.CreateInstance(typeof(Text), nonPublic: true)!; }",
                },
                new ScriptDocument { FileName = "shader.slang", Source = "this is not C#" },
            ],
        });
        Assert.Equal(ScriptExecutionOutcome.Succeeded, success.Outcome);

        ScriptExecutionResult failure = await executor.ExecuteAsync(new ScriptExecutionRequest
        {
            FileName = "entry.csx",
            Source = "return Support.Create();",
            Files =
            [
                new ScriptDocument
                {
                    FileName = "support.cs",
                    Source = "public static class Support { public static Luxel.UI.Widget Create() => missingName; }",
                },
            ],
        });

        Assert.Equal(ScriptExecutionOutcome.CompilationFailed, failure.Outcome);
        ScriptExecutionDiagnostic diagnostic = Assert.Single(failure.Diagnostics, item => item.Code == "CS0103");
        Assert.Equal("support.cs", diagnostic.Span?.FileName);
    }

    [Fact]
    public async Task LanguageService_ProvidesSemanticLuxelCompletion()
    {
        using var service = new WebScriptLanguageService(References());
        const string source = "return Kit.";

        WebCompletionResult completion = await service.CompleteAsync(source, source.Length, revision: 7);

        Assert.Equal(7, completion.Revision);
        Assert.Contains(completion.Items, item => item.Label == "Button");
        Assert.DoesNotContain(completion.Items, item => item.Label == "Kit.Button");
    }

    [Fact]
    public async Task LanguageService_ProvidesRoslynQuickInfo()
    {
        using var service = new WebScriptLanguageService(References());
        const string source = "return Kit.Button(_ => Log(\"clicked\"), \"Click\");";
        int position = source.IndexOf("Button", StringComparison.Ordinal) + 2;

        WebHoverResult? hover = await service.HoverAsync(source, position, revision: 8);

        Assert.NotNull(hover);
        Assert.Equal(8, hover.Revision);
        Assert.Contains("Button", hover.Markdown, StringComparison.Ordinal);
        Assert.True(hover.Length > 0);
    }

    [Fact]
    public async Task LanguageService_UsesSupportDocumentsForCompletionAndHover()
    {
        using var service = new WebScriptLanguageService(References());
        const string entry = "return Support.";
        const string support = "public static class Support { public static Luxel.UI.Widget Create() => null!; }";
        var project = new WebScriptProject(
            new WebScriptDocument("entry.csx", entry),
            [new WebScriptDocument("support.cs", support)]);

        WebCompletionResult completion = await service.CompleteAsync(project, "entry.csx", entry.Length, revision: 10);
        int hoverPosition = support.IndexOf("Create", StringComparison.Ordinal) + 2;
        WebHoverResult? hover = await service.HoverAsync(project, "support.cs", hoverPosition, revision: 11);

        Assert.Equal(10, completion.Revision);
        Assert.Contains(completion.Items, item => item.Label == "Create");
        Assert.NotNull(hover);
        Assert.Equal(11, hover.Revision);
        Assert.Contains("Create", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageService_MapsSupportDiagnosticsToTheirDocument()
    {
        using var service = new WebScriptLanguageService(References());
        var project = new WebScriptProject(
            new WebScriptDocument("entry.csx", "return Support.Create();"),
            [new WebScriptDocument("support.cs", "public static class Support\n{\n    public static Luxel.UI.Widget Create() => missingName;\n}")]);

        WebAnalysisResult analysis = await service.AnalyzeAsync(project, revision: 12);

        WebScriptDiagnostic diagnostic = Assert.Single(analysis.Diagnostics, item => item.Id == "CS0103");
        Assert.Equal(12, analysis.Revision);
        Assert.Equal("support.cs", diagnostic.FileName);
        Assert.Equal(3, diagnostic.Line);
    }

    [Fact]
    public async Task LanguageService_MapsLiveDiagnosticsToUserSource()
    {
        using var service = new WebScriptLanguageService(References());

        WebAnalysisResult analysis = await service.AnalyzeAsync("var value = 1;\nreturn missingName;", revision: 9);

        WebScriptDiagnostic diagnostic = Assert.Single(analysis.Diagnostics, item => item.Id == "CS0103");
        Assert.Equal(9, analysis.Revision);
        Assert.Equal(2, diagnostic.Line);
        Assert.NotNull(diagnostic.Column);
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
