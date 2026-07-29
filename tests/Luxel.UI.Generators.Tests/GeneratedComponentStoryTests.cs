using System.Text.Json;
using Luxel.UI.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Luxel.UI.Generators.Tests;

public sealed class GeneratedComponentStoryTests
{
    [Fact]
    public void Checked_in_runtime_manifest_identifies_exact_60_generated_production_basics()
    {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "samples", "LuxelWebGpuBrowser", "wwwroot", "browser-runtime-manifest.json")));
        JsonElement[] production = document.RootElement.GetProperty("stories").EnumerateArray()
            .Where(story => story.TryGetProperty("componentType", out JsonElement component)
                && component.ValueKind == JsonValueKind.String)
            .ToArray();

        Assert.Equal(2, document.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(60, production.Length);
        Assert.Equal(60, production.Select(story => story.GetProperty("componentType").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(60, production.Select(story => story.GetProperty("path").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(production, story => Assert.EndsWith("/Basic", story.GetProperty("path").GetString(), StringComparison.Ordinal));
        Assert.All(production, story => Assert.Equal(JsonValueKind.Array, story.GetProperty("args").ValueKind));
    }

    [Fact]
    public void Automatic_component_story_generation_emits_static_schema_direct_factory_and_capability_fallback()
    {
        const string source = """
            using System;
            [assembly: Luxel.UI.UiFactoryDefaults("Kit")]

            namespace Luxel.UI
            {
                [AttributeUsage(AttributeTargets.Assembly)] public sealed class UiFactoryDefaultsAttribute(string name) : Attribute { }
                [AttributeUsage(AttributeTargets.Class)] public sealed class UiComponentAttribute : Attribute
                {
                    public string? Factory { get; set; }
                    public string? Name { get; set; }
                }
                [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class UiParamAttribute : Attribute
                {
                    public bool Stateable { get; set; }
                }
                [AttributeUsage(AttributeTargets.Field)] public sealed class UiEventAttribute : Attribute { }
                public sealed class Bindable<T> { }
                public sealed class BindableString { }
                public sealed class Signal<T> { public Signal(T value) { } }
                public sealed class UiEvent { }
                public readonly struct Length { }
                public abstract partial class Widget
                {
                    [UiParam] public Bindable<Length> Width { get; } = new();
                }
            }

            namespace Demo
            {
                using Luxel.UI;

                [UiComponent]
                public sealed partial class Button : Widget
                {
                    [UiParam] public BindableString Text { get; } = new();
                    [UiParam] public Bindable<bool> Enabled { get; } = new();
                    [UiEvent] public UiEvent Clicked = new();
                }

                public sealed class Capability { }

                [UiComponent]
                public sealed partial class AssetBrowser : Widget
                {
                    [UiParam] public Bindable<Signal<Capability>> Services { get; } = new();
                }

                public static partial class Kit { }
            }
            """;

        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create("GeneratedComponentStories", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new WidgetDebugGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("public const int ComponentCount = 2;", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedComponentStoryDescriptor", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"text\", \"string\", \"Example\"", generated, StringComparison.Ordinal);
        Assert.Contains("global::Demo.Kit.Button(text: arg", generated, StringComparison.Ordinal);
        Assert.Contains("clicked: () => ctx.Log(\"Button.Clicked\")", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Luxel.UI.StoryCapabilityFallback(\"AssetBrowser\"", generated, StringComparison.Ordinal);
        Assert.Contains("Unsupported capability/constructor inputs use a deterministic fallback: Services.", generated, StringComparison.Ordinal);
        Assert.Contains("StoryResult.FromMarkdown", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", generated, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Luxel.slnx.");
    }
}
