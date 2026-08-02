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

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp13, DocumentationMode.None);
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
        => Compile(WebScriptProject.FromSource(body), assemblyName);

    public WebScriptCompilation Compile(WebScriptProject project, string assemblyName = "Luxel.Playground.Script")
    {
        ValidateProject(project);
        WebScriptDocument entry = project.EntryDocument;
        IReadOnlyList<WebScriptDocument> documents = CSharpDocuments(project);
        IReadOnlyList<WebScriptDiagnostic> policyDiagnostics = documents
            .SelectMany(document => _policy.Validate(document.Source)
                .Select(diagnostic => diagnostic with { FileName = document.FileName }))
            .ToArray();
        string generated = Wrap(entry.Source, entry.FileName);
        if (policyDiagnostics.Any(d => d.Severity == WebScriptDiagnosticSeverity.Error))
            return new(false, null, null, policyDiagnostics, generated);

        var trees = new List<SyntaxTree>(documents.Count)
        {
            CSharpSyntaxTree.ParseText(SourceText.From(generated, Encoding.UTF8), ParseOptions, entry.FileName),
        };
        trees.AddRange(documents.Skip(1).Select(document => CSharpSyntaxTree.ParseText(
            SourceText.From(document.Source, Encoding.UTF8), ParseOptions, document.FileName)));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
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

    public static string Wrap(string body) => Wrap(body, ScriptFileName);

    public static string Wrap(string body, string fileName) => $$"""
        using System;
        using Luxel.Graphics;
        using Luxel.UI;
        using Luxel.Controls;
        using Luxel.Scripting.Roslyn.Web;

        namespace Luxel.Generated;

        public sealed class LuxelWebScriptProgram : global::Luxel.Scripting.Roslyn.Web.ILuxelWebScriptProgram
        {
            private static void Log(string message) => global::Luxel.Scripting.Roslyn.Web.WebScriptOutput.Write(message);

            public Widget Build()
            {
        #line 1 "{{EscapeLineDirectivePath(fileName)}}"
        {{body}}
        #line default
            }
        }
        """;

    internal static IReadOnlyList<WebScriptDocument> CSharpDocuments(WebScriptProject project)
    {
        ValidateProject(project);
        return [project.EntryDocument, .. project.Documents.Where(IsCSharpDocument)];
    }

    internal static bool IsCSharpDocument(WebScriptDocument document)
        => string.Equals(Path.GetExtension(document.FileName), ".cs", StringComparison.OrdinalIgnoreCase);

    private static void ValidateProject(WebScriptProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(project.EntryDocument);
        ArgumentNullException.ThrowIfNull(project.Documents);
        if (string.IsNullOrWhiteSpace(project.EntryDocument.FileName))
            throw new ArgumentException("The entry document must have a file name.", nameof(project));
        ArgumentNullException.ThrowIfNull(project.EntryDocument.Source);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.EntryDocument.FileName };
        foreach (WebScriptDocument document in project.Documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (string.IsNullOrWhiteSpace(document.FileName))
                throw new ArgumentException("Every support document must have a file name.", nameof(project));
            ArgumentNullException.ThrowIfNull(document.Source);
            if (!names.Add(document.FileName))
                throw new ArgumentException($"Duplicate document file name '{document.FileName}'.", nameof(project));
        }
    }

    private static string EscapeLineDirectivePath(string fileName)
        => fileName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    internal static WebScriptDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
        LinePositionSpan sourceSpan = diagnostic.Location.GetLineSpan().Span;
        bool sourceLocation = diagnostic.Location.IsInSource;
        return new(
            diagnostic.Id,
            diagnostic.GetMessage(),
            diagnostic.Severity == DiagnosticSeverity.Error ? WebScriptDiagnosticSeverity.Error : WebScriptDiagnosticSeverity.Warning,
            sourceLocation ? span.StartLinePosition.Line + 1 : null,
            sourceLocation ? span.StartLinePosition.Character + 1 : null,
            sourceLocation ? Math.Max(1, sourceSpan.End.Character - sourceSpan.Start.Character) : 1,
            sourceLocation ? span.Path : null);
    }
}
