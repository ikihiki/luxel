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
            [Story]
            public static StoryResult Basic(StoryContext ctx)
                => new Widget("quoted <tag> & value");
            """;

        string source = GeneratedStorySource(story);

        Assert.Equal(CapturedMethodSyntax(story), source);
        Assert.Contains("[Story]", source);
        Assert.Contains("public static StoryResult Basic(StoryContext ctx)", source);
        Assert.Contains("=> new Widget(\"quoted <tag> & value\");", source);
    }

    [Fact]
    public void Source_PreservesCapturedBlockBodyWhitespaceParametersAndComments()
    {
        const string story = """
            [Story]
            internal static StoryResult Block(StoryContext ctx, DemoService service)
            {
                // source contract
                string text = "line 1\\nline 2";
                return new Widget(text + service.Name);
            }
            """;

        string source = GeneratedStorySource(story);

        Assert.Equal(CapturedMethodSyntax(story), source);
        Assert.Contains("StoryContext ctx, DemoService service", source);
        Assert.Contains("// source contract", source);
        Assert.Contains("line 1", source);
        Assert.Contains("line 2", source);
    }

    [Fact]
    public void Story_path_uses_meta_title_and_method_name()
    {
        GeneratorDriverRunResult result = Run("""
            [Story]
            public static StoryResult Triangle() => new Widget();
            """, "Build");
        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("\"Build/Triangle\"", generated);
    }

    [Fact]
    public void Stories_in_the_same_meta_follow_method_declaration_order()
    {
        GeneratorDriverRunResult result = Run("""
            [Story]
            public static StoryResult Overview() => new Widget();

            [Story]
            public static StoryResult CreateProject() => new Widget();

            [Story]
            public static StoryResult Finish() => new Widget();
            """, "Tutorials/3DApp");

        string generated = Assert.Single(result.GeneratedTrees).ToString();
        int overview = generated.IndexOf("\"Tutorials/3DApp/Overview\"", StringComparison.Ordinal);
        int createProject = generated.IndexOf("\"Tutorials/3DApp/CreateProject\"", StringComparison.Ordinal);
        int finish = generated.IndexOf("\"Tutorials/3DApp/Finish\"", StringComparison.Ordinal);

        Assert.True(overview >= 0 && overview < createProject);
        Assert.True(createProject < finish);
    }

    [Fact]
    public void Samples_files_are_excluded_from_page_navigation()
    {
        GeneratorDriverRunResult page = Run("""
            [Story]
            public static StoryResult Overview() => new Widget();
            """, sourceFile: "Tutorial3DApp.cs");
        GeneratorDriverRunResult sample = Run("""
            [Story]
            public static StoryResult Triangle() => new Widget();
            """, sourceFile: "Tutorial3DApp.Samples.cs");

        Assert.DoesNotContain("IncludeInPageNavigation", Assert.Single(page.GeneratedTrees).ToString(), StringComparison.Ordinal);
        Assert.Contains("IncludeInPageNavigation: false", Assert.Single(sample.GeneratedTrees).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_descriptor_schema_is_emitted_without_building_the_story()
    {
        GeneratorDriverRunResult result = Run("""
            [Story(Args = nameof(Args), CapabilityNote = "fixture")]
            public static StoryResult Demo() => new Widget();
            public static System.Collections.Generic.IReadOnlyList<StoryArgDefinition> Args() => System.Array.Empty<StoryArgDefinition>();
            """);

        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("ArgDefinitions: global::Demo.Stories.Args()", generated, StringComparison.Ordinal);
        Assert.Contains("CapabilityNote: \"fixture\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_markdown_story_is_emitted_from_the_return_type()
    {
        GeneratorDriverRunResult result = Run("""
            [Story]
            public static StoryResult Overview() => new StoryResult();
            """);

        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("\"Demo/Overview\", static ctx => global::Demo.Stories.Overview()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Toc:", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DocNew", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Widget_return_type_is_rejected_as_a_removed_compatibility_signature()
    {
        GeneratorDriverRunResult result = Run("""
            [Story]
            public static Widget Document(StoryContext ctx, DemoService service) => new Widget(service.Name);
            """);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, value => value.Id == "NGUI010");
        Assert.Contains("Document", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidStorySignature_ReportsNgui010()
    {
        GeneratorDriverRunResult result = Run("""
            [Story]
            public Widget Invalid() => new Widget("bad");
            """);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, value => value.Id == "NGUI010");
        Assert.Contains("Invalid", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string CapturedMethodSyntax(string storyMethod)
    {
        string[] lines = storyMethod.Replace("\r", "").Split('\n');
        return string.Join("\n", lines.Select((line, index) => index == 0 ? line : "        " + line));
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
        ArgumentSyntax sourceArgument = Assert.Single(registration.ArgumentList!.Arguments,
            argument => argument.NameColon?.Name.Identifier.ValueText == "Source");
        LiteralExpressionSyntax literal = Assert.IsType<LiteralExpressionSyntax>(sourceArgument.Expression);
        return literal.Token.ValueText;
    }

    private static GeneratorDriverRunResult Run(string storyMethod, string title = "Demo", string sourceFile = "Stories.cs")
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
                public sealed class StoryAttribute : Attribute
                {
                    public bool RealWindowOnly { get; set; }
                    public string? Args { get; set; }
                    public string? CapabilityNote { get; set; }
                }
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class StoryMeta(string title) : Attribute { public string Title { get; } = title; }
                public sealed class StoryContext
                {
                    public T Require<T>() => default!;
                }
                public sealed record StoryArgDefinition(string Name);
                public sealed class StoryResult
                {
                    public static implicit operator StoryResult(Widget widget) => new();
                }
                public sealed record StoryInfo(string Path, Func<StoryContext, StoryResult> Build,
                    string? Source = null, bool RealWindowOnly = false,
                    System.Collections.Generic.IReadOnlyList<StoryArgDefinition>? ArgDefinitions = null,
                    string? CapabilityNote = null, bool IncludeInPageNavigation = true);
                public static class StoryRegistry { public static void Register(StoryInfo story) { } }
            }

            namespace Demo
            {
                public sealed class DemoService { public string Name => "demo"; }
                [StoryMeta("{{title}}")] public static class Stories
                {
            {{Indent(storyMethod, 8)}}
                }
            }
            """;

        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, sourceFile);
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
