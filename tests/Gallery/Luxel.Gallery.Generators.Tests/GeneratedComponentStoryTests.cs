using System.Text.Json;
using Luxel.Gallery.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Luxel.Gallery;

namespace Luxel.Gallery.Generators.Tests;

public sealed class GeneratedComponentStoryTests
{
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

            namespace Luxel.Controls
            {
                public sealed class Text : Luxel.UI.Widget { }
                public static class Kit
                {
                    public static Text Text(string text, float size) => new();
                }
            }

            namespace Demo
            {
                using Luxel.UI;

                /// <summary>Clickable action.</summary>
                [UiComponent]
                public sealed partial class Button : Widget
                {
                    /// <summary>Displayed label.</summary>
                    [UiParam] public BindableString Text { get; } = new();
                    [UiParam] public Bindable<bool> Enabled { get; } = new();
                    /// <summary>Raised after activation.</summary>
                    [UiEvent] public UiEvent Clicked = new();
                }

                [UiComponent]
                public sealed partial class PreviewPanel : Widget
                {
                    [UiParam] public Bindable<Widget> Child { get; } = new();
                    [UiParam] public Bindable<float> Min { get; } = new();
                    [UiParam] public Bindable<float> Max { get; } = new();
                    [UiParam] public Bindable<float> IconSize { get; } = new();
                    [UiParam] public Bindable<float> Stroke { get; } = new();
                    [UiParam] public Bindable<float> SurfaceWidth { get; } = new();
                    [UiParam] public Bindable<float> SurfaceHeight { get; } = new();
                    [UiParam] public Bindable<float> SpinnerSize { get; } = new();
                    [UiParam] public Bindable<float> ViewportHeight { get; } = new();
                    [UiParam] public Bindable<float> EditorWidth { get; } = new();
                    [UiParam] public Bindable<float> EditorHeight { get; } = new();
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
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new GeneratedComponentStoryGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("public const int ComponentCount = 3;", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedComponentStoryDescriptor", generated, StringComparison.Ordinal);
        Assert.Contains("\"Controls/Input/Button/Docs\"", generated, StringComparison.Ordinal);
        Assert.Contains("StoryKind.Docs", generated, StringComparison.Ordinal);
        Assert.Contains("StoryKind.Basic", generated, StringComparison.Ordinal);
        Assert.Contains("StoryKind.Playground", generated, StringComparison.Ordinal);
        Assert.Contains("\"Controls/Input/Button/Playground\"", generated, StringComparison.Ordinal);
        Assert.Contains("ArgDefinitions: global::System.Array.Empty<global::Luxel.Gallery.StoryArgDefinition>()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Controls/Button/Overview", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"text\", \"string\", \"Example\"", generated, StringComparison.Ordinal);
        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:T:Demo.Button\", \"Clickable action.\")", generated, StringComparison.Ordinal);
        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:P:Demo.Button.Text\", \"Displayed label.\")", generated, StringComparison.Ordinal);
        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:E:Demo.Button.Clicked\", \"Raised after activation.\")", generated, StringComparison.Ordinal);
        Assert.Contains("global::Demo.Kit.Button(text: arg", generated, StringComparison.Ordinal);
        Assert.Contains("clicked: () => ctx.Log(\"Button.Clicked\")", generated, StringComparison.Ordinal);
        Assert.Contains("child: global::Luxel.Controls.Kit.Text(\"Generated child\", 16f)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("StoryCapabilityFallback(\"Child fixture\"", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"min\", \"float\", 0f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"max\", \"float\", 1f", generated, StringComparison.Ordinal);
        Assert.Contains("min: -100d, max: 100d, step: 0.1d", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"iconSize\", \"float\", 24f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"stroke\", \"float\", 2f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"surfaceWidth\", \"float\", 320f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"surfaceHeight\", \"float\", 180f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"spinnerSize\", \"float\", 32f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"viewportHeight\", \"float\", 180f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"editorWidth\", \"float\", 320f", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<float>(\"editorHeight\", \"float\", 240f", generated, StringComparison.Ordinal);
        Assert.Contains("Min = 1d, Max = 1024d, Step = 1d", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Luxel.Gallery.StoryCapabilityFallback(\"AssetBrowser\"", generated, StringComparison.Ordinal);
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
