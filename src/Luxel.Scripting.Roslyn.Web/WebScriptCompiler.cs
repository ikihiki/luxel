using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.Scripting.Roslyn.Web;

public sealed class WebScriptCompiler
{
    public const string ScriptFileName = "playground.cs";
    public const string EntryTypeName = "Luxel.Generated.LuxelWebScriptProgram";

    private readonly ImmutableArray<MetadataReference> _references;
    private readonly WebScriptPolicy _policy;

    public WebScriptCompiler(IEnumerable<MetadataReferenceImage> references, WebScriptPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references.Select(reference =>
        {
            if (reference.Image.IsEmpty) throw new ArgumentException($"Metadata image '{reference.FileName}' is empty.", nameof(references));
            return (MetadataReference)MetadataReference.CreateFromImage(ImmutableArray.Create(reference.Image.ToArray()), filePath: reference.FileName);
        }).ToImmutableArray();
        _policy = policy ?? new WebScriptPolicy();
    }

    public WebScriptCompilation Compile(string body, string assemblyName = "Luxel.Playground.Script")
    {
        ArgumentNullException.ThrowIfNull(body);
        IReadOnlyList<WebScriptDiagnostic> policyDiagnostics = _policy.Validate(body);
        string generated = Wrap(body);
        if (policyDiagnostics.Any(d => d.Severity == WebScriptDiagnosticSeverity.Error))
            return new(false, null, null, policyDiagnostics, generated);

        var tree = CSharpSyntaxTree.ParseText(
            SourceText.From(generated, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp13, DocumentationMode.None),
            ScriptFileName);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            _references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true,
                nullableContextOptions: NullableContextOptions.Enable));

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        EmitResult emit = compilation.Emit(
            pe,
            pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: assemblyName + ".pdb"));
        IReadOnlyList<WebScriptDiagnostic> diagnostics = emit.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .Select(MapDiagnostic)
            .ToArray();
        return emit.Success
            ? new(true, pe.ToArray(), pdb.ToArray(), diagnostics, generated)
            : new(false, null, null, diagnostics, generated);
    }

    public static string Wrap(string body) => $$"""
        using System;
        using Luxel.UI;
        using Luxel.Controls;

        namespace Luxel.Generated;

        public sealed class LuxelWebScriptProgram : global::Luxel.Scripting.Roslyn.Web.ILuxelWebScriptProgram
        {
            public Widget Build()
            {
        #line 1 "{{ScriptFileName}}"
        {{body}}
        #line default
            }
        }
        """;

    private static WebScriptDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
        LinePositionSpan sourceSpan = diagnostic.Location.GetLineSpan().Span;
        bool sourceLocation = diagnostic.Location.IsInSource && span.Path == ScriptFileName;
        return new(
            diagnostic.Id,
            diagnostic.GetMessage(),
            diagnostic.Severity == DiagnosticSeverity.Error ? WebScriptDiagnosticSeverity.Error : WebScriptDiagnosticSeverity.Warning,
            sourceLocation ? span.StartLinePosition.Line + 1 : null,
            sourceLocation ? span.StartLinePosition.Character + 1 : null,
            sourceLocation ? Math.Max(1, sourceSpan.End.Character - sourceSpan.Start.Character) : 1);
    }
}
