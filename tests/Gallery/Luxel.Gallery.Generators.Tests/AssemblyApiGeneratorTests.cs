using System.Text;
using Luxel.Gallery.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.Gallery.Generators.Tests;

public sealed class AssemblyApiGeneratorTests
{
    [Fact]
    public void Emits_documentation_ids_for_types_properties_and_method_overloads()
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, DocumentationMode.Diagnose);
        MetadataReference[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation library = CSharpCompilation.Create("Demo.Api",
            [CSharpSyntaxTree.ParseText("""
                namespace Demo.Api;
                /// <summary>Service summary.</summary>
                public sealed class Service
                {
                    /// <summary>Name summary.</summary>
                    public string Name { get; } = "";
                    /// <summary>Parse text.</summary>
                    public void Parse(string value) { }
                    /// <summary>Parse number.</summary>
                    public void Parse(int value) { }
                }
                """, parseOptions)], platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        EmitResult emitted = library.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        MetadataReference libraryReference = MetadataReference.CreateFromImage(image.ToArray());
        CSharpCompilation consumer = CSharpCompilation.Create("Docs",
            [CSharpSyntaxTree.ParseText("""
                using System;
                [assembly: Luxel.UI.GenerateAssemblyApi("Demo.Api")]
                namespace Luxel.UI
                {
                    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                    public sealed class GenerateAssemblyApiAttribute(string value) : Attribute { }
                }
                """, parseOptions)], [.. platform, libraryReference], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        const string xml = """
            <doc><members>
              <member name="T:Demo.Api.Service"><summary>Service summary.</summary></member>
              <member name="P:Demo.Api.Service.Name"><summary>Name summary.</summary></member>
              <member name="M:Demo.Api.Service.Parse(System.String)"><summary>Parse text.</summary></member>
              <member name="M:Demo.Api.Service.Parse(System.Int32)"><summary>Parse number.</summary></member>
            </members></doc>
            """;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AssemblyApiGenerator().AsSourceGenerator()],
            additionalTexts: [new TextFile("Demo.Api.xml", xml)], parseOptions: parseOptions);
        driver = driver.RunGenerators(consumer);
        string generated = Assert.Single(driver.GetRunResult().GeneratedTrees).ToString();

        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:T:Demo.Api.Service\", \"Service summary.\")", generated, StringComparison.Ordinal);
        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:P:Demo.Api.Service.Name\", \"Name summary.\")", generated, StringComparison.Ordinal);
        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:M:Demo.Api.Service.Parse(System.String)\", \"Parse text.\")", generated, StringComparison.Ordinal);
        Assert.Contains("GalleryXmlDocText.Resolve(\"xml:M:Demo.Api.Service.Parse(System.Int32)\", \"Parse number.\")", generated, StringComparison.Ordinal);
    }

    private sealed class TextFile(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(text, Encoding.UTF8);
    }
}
