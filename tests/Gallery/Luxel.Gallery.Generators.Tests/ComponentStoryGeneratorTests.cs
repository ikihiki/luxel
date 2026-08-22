using System.Collections.Immutable;
using Luxel.Gallery.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Luxel.Gallery.Generators.Tests;

public sealed class ComponentStoryGeneratorTests
{
    [Fact]
    public void GeneratesStronglyTypedFactoryArgsApplyAndTemplate()
    {
        GeneratorDriverRunResult result = Run("""
            [ComponentStory(typeof(Button), "Controls/Button/Playground", Factory = typeof(Kit), Template = nameof(Wrap),
                ShortDescription = "操作を確認します。", LongDescription = "引数を変更して比較します。")]
            [ComponentArg(nameof(Button.Text), "Click me")]
            [ComponentArg(nameof(Button.Variant), Variant.Filled)]
            [ComponentArg("Disabled", false, Apply = nameof(ApplyDisabled))]
            internal static class Playground
            {
                internal static void ApplyDisabled(Button button, bool disabled) => button.Enabled = !disabled;
                internal static Widget Wrap(Button button) => button;
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string generated = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("ctx.Arg<string>(\"text\", \"Click me\")", generated);
        Assert.Contains("ctx.Arg<global::Luxel.Controls.Variant>(\"variant\", global::Luxel.Controls.Variant.Filled)", generated);
        Assert.Contains("global::Luxel.Controls.Kit.Button(text: arg0.Value, variant: arg1.Value)", generated);
        Assert.Contains("global::Demo.Playground.ApplyDisabled(component, arg2.Value)", generated);
        Assert.Contains("global::Demo.Playground.Wrap(component)", generated);
        Assert.DoesNotContain("GalleryXmlDocText.Resolve", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Luxel.Gallery.ComponentStoryPreview", generated);
        Assert.Contains("ShortDescription: \"操作を確認します。\"", generated);
        Assert.Contains("LongDescription: \"引数を変更して比較します。\"", generated);
        Assert.DoesNotContain("Activator", generated);
        Assert.DoesNotContain("DynamicInvoke", generated);
        Assert.DoesNotContain("Reflection", generated);
    }

    [Theory]
    [InlineData("[ComponentArg(\"Missing\", false)]", "", "NGUI020")]
    [InlineData("[ComponentArg(nameof(Button.Variant), \"bad\")]", "", "NGUI021")]
    [InlineData("[ComponentArg(\"Disabled\", false, Apply = \"Missing\")]", "", "NGUI022")]
    [InlineData("", ", Template = \"Missing\"", "NGUI023")]
    public void ReportsFocusedDeclarationDiagnostics(string arg, string storyOptions, string diagnosticId)
    {
        GeneratorDriverRunResult result = Run($$"""
            [ComponentStory(typeof(Button), "Invalid", Factory = typeof(Kit){{storyOptions}})]
            {{arg}}
            internal static class InvalidStory { }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult Run(string declaration)
    {
        string source = $$"""
            using System;
            using Luxel.Gallery;
            using Luxel.Controls;
            using Luxel.UI;

            namespace Luxel.UI
            {
                [AttributeUsage(AttributeTargets.Class)] public sealed class UiComponentAttribute : Attribute { public string? Factory { get; set; } public string? Name { get; set; } }
                [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class UiParamAttribute : Attribute { }
                public class Bindable<T> { }
                public sealed class BindableString { }
                public class Widget { public bool Enabled { get; set; } }
                public sealed class Signal<T> { public T Value => default!; }
                public sealed class StoryContext { public Signal<T> Arg<T>(string name, T value, StoryArgOptions<T>? options = null) => new(); }
                public sealed class StoryArgOptions<T> { public string? Description { get; init; } public int Order { get; init; } public double? Min { get; init; } public double? Max { get; init; } public double? Step { get; init; } }
                public sealed class StoryResult { public static implicit operator StoryResult(Widget widget) => new(); }
                public sealed record StoryInfo(string Path, Func<StoryContext, StoryResult> Build, string? Source = null, bool RealWindowOnly = false, System.Collections.Generic.IReadOnlyList<object>? ArgDefinitions = null, string? ShortDescription = null, string? LongDescription = null);
                public sealed class StoryCatalog { public System.Collections.Generic.IReadOnlyList<StoryInfo> All => Array.Empty<StoryInfo>(); }
                public sealed class StoryCatalogBuilder { public StoryCatalogBuilder Add(StoryInfo story) => this; public StoryCatalog Build() => new(); }
                public static class StoryRegistry { public static void Register(StoryInfo story) { } }
            }

            namespace Luxel.Gallery
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ComponentStoryAttribute(Type componentType, string path) : Attribute
                {
                    public Type? Factory { get; set; } public string? FactoryMethod { get; set; } public string? Template { get; set; }
                    public bool RealWindowOnly { get; set; }
                    public string? ShortDescription { get; set; } public string? LongDescription { get; set; }
                }
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public sealed class ComponentArgAttribute(string member, object? defaultValue) : Attribute
                {
                    public string? Name { get; set; } public string? Apply { get; set; } public string? Description { get; set; }
                    public int Order { get; set; } = 1000; public double Min { get; set; } = double.NaN;
                    public double Max { get; set; } = double.NaN; public double Step { get; set; } = double.NaN;
                }
                public sealed class ComponentStoryPreview(Func<Widget> build) : Widget { }
            }

            namespace Luxel.Controls
            {
                public enum Variant { Filled, Outline }
                [UiComponent]
                public sealed class Button : Widget
                {
                    [UiParam] public BindableString Text { get; } = new();
                    [UiParam] public Bindable<Variant> Variant { get; } = new();
                }
                public static class Kit
                {
                    public static Button Button(BindableString? text = null, Bindable<Variant>? variant = null) => new();
                }
            }

            namespace Demo
            {
            {{Indent(declaration, 4)}}
            }
            """;

        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create("ComponentStoryTests", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ComponentStoryGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string Indent(string value, int spaces)
    {
        string prefix = new(' ', spaces);
        return string.Join("\n", value.Replace("\r", "").Split('\n').Select(line => prefix + line));
    }
}
