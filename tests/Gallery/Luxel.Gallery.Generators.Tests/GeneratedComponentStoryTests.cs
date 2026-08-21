using System.Text.RegularExpressions;
using System.Text.Json;
using Luxel.Gallery.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Luxel.Gallery;

namespace Luxel.Gallery.Generators.Tests;

public sealed class GeneratedComponentStoryTests
{
    [Fact]
    public void Automatic_component_story_generation_covers_every_param_with_typed_args_collections_and_presets()
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
                public readonly record struct Thickness(float Value) : IParsable<Thickness>
                {
                    public static Thickness Parse(string value, IFormatProvider? provider) => new(float.Parse(value, provider));
                    public static bool TryParse(string? value, IFormatProvider? provider, out Thickness result)
                    {
                        bool parsed = float.TryParse(value, provider, out float scalar);
                        result = new Thickness(scalar);
                        return parsed;
                    }
                    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                public enum GridUnit { Pixel, Auto, Star }
                public readonly record struct GridLength(float Value, GridUnit Unit)
                {
                    public static GridLength Star(float value = 1) => new(value, GridUnit.Star);
                    public static GridLength Px(float value) => new(value, GridUnit.Pixel);
                    public static GridLength Auto => new(0, GridUnit.Auto);
                }
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
                    [UiParam] public Bindable<Thickness> Padding { get; } = new();
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
                    [UiParam] public Bindable<Signal<bool>> Open { get; } = new();
                    [UiParam] public Bindable<string[]> Labels { get; } = new();
                    [UiParam] public Bindable<GridLength[]> Columns { get; } = new();
                    [UiParam] public Bindable<Widget[]> Content { get; } = new();
                }

                public sealed class Capability { }

                [UiComponent]
                public sealed partial class AssetBrowser : Widget
                {
                    [UiParam] public Bindable<Signal<Capability>> Services { get; } = new();
                    [UiParam] public Bindable<Action<Capability>> Changed { get; } = new();
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
        MatchCollection argBlocks = Regex.Matches(generated,
            @"private static readonly global::Luxel\.Gallery\.StoryArgDefinition\[\] Args_\d+ =\s*\[(.*?)\];",
            RegexOptions.Singleline);
        Assert.Equal(3, argBlocks.Count);
        string[][] expectedArgNames =
        [
            ["services", "changed", "width"],
            ["text", "enabled", "width"],
            ["child", "padding", "min", "max", "iconSize", "stroke", "surfaceWidth", "surfaceHeight", "spinnerSize",
             "viewportHeight", "editorWidth", "editorHeight", "open", "labels", "columns", "content", "width"],
        ];
        for (int block = 0; block < argBlocks.Count; block++)
        {
            string[] actualNames = Regex.Matches(argBlocks[block].Groups[1].Value, "Create<[^>]+>\\(\\\"([^\\\"]+)\\\"")
                .Select(match => match.Groups[1].Value).ToArray();
            Assert.Equal(expectedArgNames[block], actualNames);
        }
        Assert.Contains("public const int ComponentCount = 3;", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedComponentStoryDescriptor", generated, StringComparison.Ordinal);
        Assert.Contains("\"Controls/Input/Button/Docs\"", generated, StringComparison.Ordinal);
        Assert.Contains("StoryKind.Docs", generated, StringComparison.Ordinal);
        Assert.Contains("StoryKind.Basic", generated, StringComparison.Ordinal);
        Assert.Contains("StoryKind.Playground", generated, StringComparison.Ordinal);
        Assert.Contains("\"Controls/Input/Button/Playground\"", generated, StringComparison.Ordinal);
        Assert.Contains("ShortDescription: \"Button の概要、使い方、APIを確認します。\"", generated, StringComparison.Ordinal);
        Assert.Contains("ShortDescription: \"Button の基本的な表示例です。\"", generated, StringComparison.Ordinal);
        Assert.Contains("ShortDescription: \"Button の引数を変更して動作を確認できます。\"", generated, StringComparison.Ordinal);
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
        Assert.Contains("StoryArgDefinition.Create<string>(\"padding\", \"string\", default(global::Luxel.UI.Thickness).ToString() ?? string.Empty", generated, StringComparison.Ordinal);
        Assert.Contains("ctx.Arg<string>(\"padding\", default(global::Luxel.UI.Thickness).ToString() ?? string.Empty", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Parser = static value => global::Luxel.UI.Thickness.Parse", generated, StringComparison.Ordinal);
        Assert.Contains("padding: global::Luxel.UI.Thickness.Parse(arg", generated, StringComparison.Ordinal);
        Assert.Contains(".Value, global::System.Globalization.CultureInfo.InvariantCulture)", generated, StringComparison.Ordinal);
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
        Assert.Contains("StoryArgDefinition.Create<bool>(\"open\", \"bool\", false", generated, StringComparison.Ordinal);
        Assert.Contains("ctx.Arg<bool>(\"open\", false", generated, StringComparison.Ordinal);
        Assert.Contains("global::Luxel.UI.Signal<bool> value", generated, StringComparison.Ordinal);
        Assert.Contains("value", generated, StringComparison.Ordinal);
        Assert.Contains(".Value = arg", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"labels\", \"string\", \"One, Two, Three\"", generated, StringComparison.Ordinal);
        Assert.Contains("labels: arg", generated, StringComparison.Ordinal);
        Assert.Contains("StringSplitOptions.TrimEntries | global::System.StringSplitOptions.RemoveEmptyEntries", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"columns\", \"string\", \"1*, 1*\"", generated, StringComparison.Ordinal);
        Assert.Contains("columns: ParseGridLengths(arg", generated, StringComparison.Ordinal);
        Assert.Contains("private static global::Luxel.UI.GridLength[] ParseGridLengths", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"content\", \"preset\", \"Generated fixture\"", generated, StringComparison.Ordinal);
        Assert.Contains("content: new global::Luxel.UI.Widget[]", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"services\", \"preset\", \"Component default\"", generated, StringComparison.Ordinal);
        Assert.Contains("StoryArgDefinition.Create<string>(\"changed\", \"preset\", \"Component default\"", generated, StringComparison.Ordinal);
        Assert.Contains("per-parameter component-default presets where adapters are required: Services, Changed.", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported capability/constructor inputs use a deterministic fallback", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresFallback", generated, StringComparison.Ordinal);
        Assert.Contains("(\"width\", \"length\"", generated, StringComparison.Ordinal);
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
