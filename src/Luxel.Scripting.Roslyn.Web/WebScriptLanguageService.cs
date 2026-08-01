using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using RoslynDocument = Microsoft.CodeAnalysis.Document;

namespace Luxel.Scripting.Roslyn.Web;

/// <summary>
/// Browser-compatible Roslyn language services for the same generated program and metadata
/// reference set used by <see cref="WebScriptCompiler"/>.
/// </summary>
public sealed class WebScriptLanguageService : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly DocumentId _documentId;
    private readonly CompletionService _completion;
    private readonly QuickInfoService _quickInfo;

    public WebScriptLanguageService(IEnumerable<MetadataReferenceImage> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        ImmutableArray<MetadataReference> metadataReferences = references.Select(reference =>
        {
            if (reference.Image.IsEmpty)
                throw new ArgumentException($"Metadata image '{reference.FileName}' is empty.", nameof(references));
            return (MetadataReference)MetadataReference.CreateFromImage(
                ImmutableArray.Create(reference.Image.ToArray()),
                filePath: reference.FileName);
        }).ToImmutableArray();

        _workspace = new AdhocWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies));
        ProjectInfo projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "Luxel Playground",
            "Luxel.Playground.LanguageServices",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp13, DocumentationMode.Parse),
            metadataReferences: metadataReferences);
        Project project = _workspace.AddProject(projectInfo);
        RoslynDocument document = _workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            WebScriptCompiler.ScriptFileName,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(WebScriptCompiler.Wrap("")), VersionStamp.Create())),
            filePath: WebScriptCompiler.ScriptFileName));
        _documentId = document.Id;
        _completion = CompletionService.GetService(document)
            ?? throw new InvalidOperationException("Roslyn C# completion services are unavailable.");
        _quickInfo = QuickInfoService.GetService(document)
            ?? throw new InvalidOperationException("Roslyn C# QuickInfo services are unavailable.");
    }

    public async Task<WebCompletionResult> CompleteAsync(string source, int position, int revision = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        position = Math.Clamp(position, 0, source.Length);
        (RoslynDocument document, int bodyStart) = WithSource(source);
        CompletionList? list = await _completion.GetCompletionsAsync(document, bodyStart + position, cancellationToken: cancellationToken);
        if (list is null)
            return new WebCompletionResult(revision, position, 0, []);

        int replacementStart = Math.Clamp(list.Span.Start - bodyStart, 0, source.Length);
        int replacementLength = Math.Clamp(list.Span.Length, 0, source.Length - replacementStart);
        // Resolving a CompletionChange for every item is prohibitively expensive under the WASM
        // interpreter. Monaco requests the common identifier insertion here; richer edits can be
        // added later as an explicit completion-item resolve request.
        var items = new List<WebCompletionItem>(Math.Min(list.ItemsList.Count, 200));
        foreach (Microsoft.CodeAnalysis.Completion.CompletionItem item in list.ItemsList.Take(200))
        {
            string kind = item.Tags.FirstOrDefault() ?? "Text";
            items.Add(new WebCompletionItem(
                item.DisplayText,
                item.DisplayText,
                kind,
                item.InlineDescription));
        }
        return new WebCompletionResult(revision, replacementStart, replacementLength, items);
    }

    public async Task<WebHoverResult?> HoverAsync(string source, int position, int revision = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        position = Math.Clamp(position, 0, source.Length);
        (RoslynDocument document, int bodyStart) = WithSource(source);
        QuickInfoItem? item = await _quickInfo.GetQuickInfoAsync(document, bodyStart + position, cancellationToken);
        if (item is null)
            return null;

        string markdown = string.Join("\n\n", item.Sections
            .Select(section => string.Concat(section.TaggedParts.Select(part => part.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (markdown.Length == 0)
            return null;

        int start = Math.Clamp(item.Span.Start - bodyStart, 0, source.Length);
        int length = Math.Clamp(item.Span.Length, 0, source.Length - start);
        return new WebHoverResult(revision, markdown, start, length);
    }

    public async Task<WebAnalysisResult> AnalyzeAsync(string source, int revision = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        (RoslynDocument document, _) = WithSource(source);
        Compilation? compilation = await document.Project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
            return new WebAnalysisResult(revision, []);

        WebScriptDiagnostic[] diagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .Select(MapDiagnostic)
            .Take(200)
            .ToArray();
        return new WebAnalysisResult(revision, diagnostics);
    }

    private (RoslynDocument Document, int BodyStart) WithSource(string source)
    {
        string generated = WebScriptCompiler.Wrap(source);
        int bodyStart = FindBodyStart(generated);
        Solution solution = _workspace.CurrentSolution.WithDocumentText(
            _documentId,
            SourceText.From(generated),
            PreservationMode.PreserveIdentity);
        if (!_workspace.TryApplyChanges(solution))
            throw new InvalidOperationException("Roslyn rejected the Playground document update.");
        return (_workspace.CurrentSolution.GetDocument(_documentId)!, bodyStart);
    }

    private static int FindBodyStart(string generated)
    {
        const string marker = "#line 1 \"" + WebScriptCompiler.ScriptFileName + "\"\n";
        int markerStart = generated.IndexOf(marker, StringComparison.Ordinal);
        if (markerStart < 0)
            throw new InvalidOperationException("The generated Playground source is missing its source mapping marker.");
        return markerStart + marker.Length;
    }

    private static WebScriptDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
        bool sourceLocation = diagnostic.Location.IsInSource && span.Path == WebScriptCompiler.ScriptFileName;
        int length = diagnostic.Location.IsInSource ? Math.Max(1, diagnostic.Location.SourceSpan.Length) : 1;
        return new WebScriptDiagnostic(
            diagnostic.Id,
            diagnostic.GetMessage(),
            diagnostic.Severity == DiagnosticSeverity.Error ? WebScriptDiagnosticSeverity.Error : WebScriptDiagnosticSeverity.Warning,
            sourceLocation ? span.StartLinePosition.Line + 1 : null,
            sourceLocation ? span.StartLinePosition.Character + 1 : null,
            length);
    }

    public void Dispose() => _workspace.Dispose();
}
