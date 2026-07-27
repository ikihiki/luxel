using System.Collections.Immutable;
using Luxel.UI.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Luxel.UI.Generators.Tests;

public sealed class StoryGeneratorTests
{
    [Fact]
    public void Source_CapturesFullExpressionBodiedMethod()
    {
        const string story = """
            [Story("Controls/Button/Basic", Height = 160)]
            public static Widget Basic(StoryContext ctx)
                => new Widget("quoted <tag> & value");
            """;

        string source = GeneratedStorySource(story);

        Assert.Equal(story, source);
        Assert.Contains("[Story(\"Controls/Button/Basic\", Height = 160)]", source);
        Assert.Contains("public static Widget Basic(StoryContext ctx)", source);
        Assert.Contains("=> new Widget(\"quoted <tag> & value\");", source);
    }

    [Fact]
    public void Source_DedentsBlockBodyAndPreservesParametersAndComments()
    {
        const string story = """
            [Story("Examples/Block")]
            internal static Widget Block(StoryContext ctx, DemoService service)
            {
                // source contract
                string text = "line 1\\nline 2";
                return new Widget(text + service.Name);
            }
            """;

        string source = GeneratedStorySource(story);

        Assert.Equal(story, source);
        Assert.Contains("StoryContext ctx, DemoService service", source);
        Assert.Contains("// source contract", source);
        Assert.Contains("line 1", source);
        Assert.Contains("line 2", source);
    }

    [Fact]
    public void SampleBundle_IsEmittedIntoStoryInfo()
    {
        GeneratorDriverRunResult result = Run("""
            [Story("Build/Triangle", SampleBundle = "rendering.triangle")]
            public static Widget Triangle() => new Widget();
            """);
        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("\"rendering.triangle\"", generated);
    }

    [Fact]
    public void InvalidStorySignature_ReportsNgui010()
    {
        GeneratorDriverRunResult result = Run("""
            [Story("Invalid")]
            public Widget Invalid() => new Widget("bad");
            """);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, value => value.Id == "NGUI010");
        Assert.Contains("Invalid", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string GeneratedStorySource(string storyMethod)
    {
        GeneratorDriverRunResult result = Run(storyMethod);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.GeneratedTrees).ToString();
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(generated).GetCompilationUnitRoot();
        ObjectCreationExpressionSyntax registration = Assert.Single(
            root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>(),
            node => node.Type.ToString().Contains("StoryInfo", StringComparison.Ordinal));
        LiteralExpressionSyntax literal = Assert.IsType<LiteralExpressionSyntax>(registration.ArgumentList!.Arguments[6].Expression);
        return literal.Token.ValueText;
    }

    private static GeneratorDriverRunResult Run(string storyMethod)
    {
        string source = $$"""
            using System;
            namespace Luxel.UI
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class StoryAttribute(string path) : Attribute
                {
                    public int Width { get; set; }
                    public int Height { get; set; }
                    public int Order { get; set; }
                    public string? Theme { get; set; }
                    public bool RealWindowOnly { get; set; }
                    public string? SampleBundle { get; set; }
                }
                public class Widget
                {
                    public Widget(string value = "") { }
                }
                public sealed class StoryContext
                {
                    public T Require<T>() => default!;
                }
                public sealed record StoryInfo(string Path, int Width, int Height, string? Theme,
                    Func<StoryContext, Widget> Build, int Order = 1000, string? Source = null, bool RealWindowOnly = false, string? SampleBundle = null);
                public static class StoryRegistry { public static void Register(StoryInfo story) { } }
                public sealed class DemoService { public string Name => "demo"; }
                public static class Stories
                {
            {{Indent(storyMethod, 8)}}
                }
            }
            """;

        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create("GeneratorTests", [syntaxTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new StoryGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> _);
        return driver.GetRunResult();
    }

    private static string Indent(string value, int spaces)
    {
        string prefix = new(' ', spaces);
        return string.Join("\n", value.Replace("\r", "").Split('\n').Select(line => prefix + line));
    }
}
