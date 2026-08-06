using System.Collections.Immutable;
using Luxel.Gallery.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Luxel.Gallery;

namespace Luxel.Gallery.Generators.Tests;

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
    public void SourceMembers_AppendsNamedHelpersFromDeclaringType()
    {
        const string story = """
            [Story("Examples/Delegated", SourceMembers = "BuildScene, Scene")]
            public static Widget Delegated() => BuildScene();

            private static Widget BuildScene() => new Widget("helper");

            private sealed class Scene
            {
                public Widget Build() => new Widget("scene");
            }
            """;

        string source = GeneratedStorySource(story);

        Assert.Contains("public static Widget Delegated()", source);
        Assert.Contains("private static Widget BuildScene()", source);
        Assert.Contains("private sealed class Scene", source);
        Assert.DoesNotContain("SourceMembers_AppendsNamedHelpers", source);
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
    public void Runtime_descriptor_metadata_is_emitted_without_building_the_story()
    {
        GeneratorDriverRunResult result = Run("""
            [Story("Controls/Demo/Basic", RuntimeBundleId = "webgpu-browser-v1", Args = nameof(Args), CapabilityNote = "fixture")]
            public static Widget Demo() => new Widget();
            public static System.Collections.Generic.IReadOnlyList<StoryArgDefinition> Args() => System.Array.Empty<StoryArgDefinition>();
            """);

        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("RuntimeBundleId: \"webgpu-browser-v1\"", generated, StringComparison.Ordinal);
        Assert.Contains("ArgDefinitions: global::Demo.Stories.Args()", generated, StringComparison.Ordinal);
        Assert.Contains("CapabilityNote: \"fixture\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_markdown_story_and_toc_metadata_are_emitted_from_the_return_type()
    {
        GeneratorDriverRunResult result = Run("""
            [Story("Learn/Graphics/2D/Overview", Toc = true)]
            public static StoryResult Overview() => new StoryResult();
            """);

        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("ResultBuild: static ctx => global::Demo.Stories.Overview()", generated, StringComparison.Ordinal);
        Assert.Contains("Toc: true", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DocNew", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_semantic_result_provider_is_emitted_without_invoking_story_dependencies()
    {
        GeneratorDriverRunResult result = Run("""
            [Story("Examples/Document", Result = nameof(DocumentResult))]
            public static Widget Document(StoryContext ctx, DemoService service) => new Widget(service.Name);
            internal static StoryResult DocumentResult() => new StoryResult();
            """);

        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("ResultBuild: static _ => global::Demo.Stories.DocumentResult()", generated, StringComparison.Ordinal);
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
            using Luxel.Gallery;
            using Luxel.UI;

            namespace Luxel.UI
            {
                public class Widget
                {
                    public Widget(string value = "") { }
                }
            }

            namespace Luxel.Gallery
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class StoryAttribute(string path) : Attribute
                {
                    public int Width { get; set; }
                    public int Height { get; set; }
                    public int Order { get; set; }
                    public string? Theme { get; set; }
                    public bool RealWindowOnly { get; set; }
                    public bool Toc { get; set; }
                    public string? SampleBundle { get; set; }
                    public string? RuntimeBundleId { get; set; }
                    public string? Args { get; set; }
                    public string? Result { get; set; }
                    public string? CapabilityNote { get; set; }
                    public string? SourceMembers { get; set; }
                }
                public sealed class StoryContext
                {
                    public T Require<T>() => default!;
                }
                public sealed record StoryArgDefinition(string Name);
                public sealed class StoryResult
                {
                    public static implicit operator StoryResult(Widget widget) => new();
                }
                public sealed record StoryInfo(string Path, int Width, int Height, string? Theme,
                    Func<StoryContext, Widget> Build, int Order = 1000, string? Source = null, bool RealWindowOnly = false, string? SampleBundle = null,
                    Func<StoryContext, StoryResult>? ResultBuild = null, string? RuntimeBundleId = null,
                    System.Collections.Generic.IReadOnlyList<StoryArgDefinition>? ArgDefinitions = null,
                    string? CapabilityNote = null, bool Toc = false);
                public static class StoryRegistry { public static void Register(StoryInfo story) { } }
            }

            namespace Demo
            {
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
