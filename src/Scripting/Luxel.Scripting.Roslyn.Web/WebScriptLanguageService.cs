using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
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
    private readonly ProjectId _projectId;

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
        _projectId = ProjectId.CreateNewId();
        ProjectInfo projectInfo = ProjectInfo.Create(
            _projectId,
            VersionStamp.Create(),
            "Luxel Playground",
            "Luxel.Playground.LanguageServices",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp13, DocumentationMode.Parse),
            metadataReferences: metadataReferences);
        _workspace.AddProject(projectInfo);
    }

    public Task<WebCompletionResult> CompleteAsync(string source, int position, int revision = 0, CancellationToken cancellationToken = default)
        => CompleteAsync(WebScriptProject.FromSource(source), WebScriptCompiler.ScriptFileName, position, revision, cancellationToken);

    public async Task<WebCompletionResult> CompleteAsync(
        WebScriptProject project,
        string fileName,
        int position,
        int revision = 0,
        CancellationToken cancellationToken = default)
    {
        (RoslynDocument document, int bodyStart, int sourceLength) = WithProject(project, fileName);
        position = Math.Clamp(position, 0, sourceLength);
        CompletionService completion = CompletionService.GetService(document)
            ?? throw new InvalidOperationException("Roslyn C# completion services are unavailable.");
        CompletionList? list = await completion.GetCompletionsAsync(document, bodyStart + position, cancellationToken: cancellationToken);
        if (list is null)
            return new WebCompletionResult(revision, position, 0, []);

        int replacementStart = Math.Clamp(list.Span.Start - bodyStart, 0, sourceLength);
        int replacementLength = Math.Clamp(list.Span.Length, 0, sourceLength - replacementStart);
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

    public Task<WebHoverResult?> HoverAsync(string source, int position, int revision = 0, CancellationToken cancellationToken = default)
        => HoverAsync(WebScriptProject.FromSource(source), WebScriptCompiler.ScriptFileName, position, revision, cancellationToken);

    public async Task<WebHoverResult?> HoverAsync(
        WebScriptProject project,
        string fileName,
        int position,
        int revision = 0,
        CancellationToken cancellationToken = default)
    {
        (RoslynDocument document, int bodyStart, int sourceLength) = WithProject(project, fileName);
        position = Math.Clamp(position, 0, sourceLength);
        QuickInfoService quickInfo = QuickInfoService.GetService(document)
            ?? throw new InvalidOperationException("Roslyn C# QuickInfo services are unavailable.");
        QuickInfoItem? item = await quickInfo.GetQuickInfoAsync(document, bodyStart + position, cancellationToken);
        if (item is null)
            return null;

        string markdown = string.Join("\n\n", item.Sections
            .Select(section => string.Concat(section.TaggedParts.Select(part => part.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (markdown.Length == 0)
            return null;

        int start = Math.Clamp(item.Span.Start - bodyStart, 0, sourceLength);
        int length = Math.Clamp(item.Span.Length, 0, sourceLength - start);
        return new WebHoverResult(revision, markdown, start, length);
    }

    public Task<WebFormatResult> FormatAsync(string source, int revision = 0, CancellationToken cancellationToken = default)
        => FormatAsync(WebScriptProject.FromSource(source), WebScriptCompiler.ScriptFileName, revision, cancellationToken);

    public async Task<WebFormatResult> FormatAsync(
        WebScriptProject project,
        string fileName,
        int revision = 0,
        CancellationToken cancellationToken = default)
    {
        (RoslynDocument document, _, _) = WithProject(project, fileName);
        RoslynDocument formatted = await Formatter.FormatAsync(document, cancellationToken: cancellationToken);
        string text = (await formatted.GetTextAsync(cancellationToken)).ToString();
        bool isEntry = string.Equals(fileName, project.EntryDocument.FileName, StringComparison.OrdinalIgnoreCase);
        return new WebFormatResult(revision, isEntry ? ExtractEntryBody(text, fileName) : text);
    }

    public Task<WebAnalysisResult> AnalyzeAsync(string source, int revision = 0, CancellationToken cancellationToken = default)
        => AnalyzeAsync(WebScriptProject.FromSource(source), revision, cancellationToken);

    public async Task<WebAnalysisResult> AnalyzeAsync(
        WebScriptProject project,
        int revision = 0,
        CancellationToken cancellationToken = default)
    {
        (RoslynDocument document, _, _) = WithProject(project, project.EntryDocument.FileName);
        Compilation? compilation = await document.Project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
            return new WebAnalysisResult(revision, []);

        WebScriptDiagnostic[] diagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .Select(WebScriptCompiler.MapDiagnostic)
            .Take(200)
            .ToArray();
        return new WebAnalysisResult(revision, diagnostics);
    }

    private (RoslynDocument Document, int BodyStart, int SourceLength) WithProject(WebScriptProject project, string fileName)
    {
        IReadOnlyList<WebScriptDocument> documents = WebScriptCompiler.CSharpDocuments(project);
        WebScriptDocument target = documents.FirstOrDefault(document =>
            string.Equals(document.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"C# document '{fileName}' is not part of the project.", nameof(fileName));

        Solution solution = _workspace.CurrentSolution;
        foreach (DocumentId documentId in solution.GetProject(_projectId)!.DocumentIds)
            solution = solution.RemoveDocument(documentId);

        DocumentId? targetId = null;
        int bodyStart = 0;
        foreach (WebScriptDocument sourceDocument in documents)
        {
            bool isEntry = ReferenceEquals(sourceDocument, project.EntryDocument);
            string text = isEntry
                ? WebScriptCompiler.Wrap(sourceDocument.Source, sourceDocument.FileName)
                : sourceDocument.Source;
            var documentId = DocumentId.CreateNewId(_projectId, sourceDocument.FileName);
            solution = solution.AddDocument(documentId, sourceDocument.FileName, SourceText.From(text), filePath: sourceDocument.FileName);
            if (string.Equals(sourceDocument.FileName, target.FileName, StringComparison.OrdinalIgnoreCase))
            {
                targetId = documentId;
                bodyStart = isEntry ? FindBodyStart(text, sourceDocument.FileName) : 0;
            }
        }

        if (!_workspace.TryApplyChanges(solution))
            throw new InvalidOperationException("Roslyn rejected the Playground project update.");
        return (_workspace.CurrentSolution.GetDocument(targetId!)!, bodyStart, target.Source.Length);
    }

    private static string ExtractEntryBody(string generated, string fileName)
    {
        int bodyStart = FindBodyStart(generated, fileName);
        int bodyEnd = generated.IndexOf("#line default", bodyStart, StringComparison.Ordinal);
        if (bodyEnd < 0)
            throw new InvalidOperationException("The formatted Playground source is missing its source mapping terminator.");

        string body = generated[bodyStart..bodyEnd].Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n');
        string[] lines = body.Split('\n');
        int indentation = lines.Where(line => line.Length > 0)
            .Select(line => line.TakeWhile(char.IsWhiteSpace).Count())
            .DefaultIfEmpty(0)
            .Min();
        return string.Join("\n", lines.Select(line => line.Length >= indentation ? line[indentation..] : string.Empty));
    }

    private static int FindBodyStart(string generated, string fileName)
    {
        string escaped = fileName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        string marker = "#line 1 \"" + escaped + "\"";
        int markerStart = generated.IndexOf(marker, StringComparison.Ordinal);
        if (markerStart < 0)
            throw new InvalidOperationException("The generated Playground source is missing its source mapping marker.");

        int bodyStart = markerStart + marker.Length;
        if (generated.AsSpan(bodyStart).StartsWith("\r\n", StringComparison.Ordinal)) return bodyStart + 2;
        if (generated.AsSpan(bodyStart).StartsWith("\n", StringComparison.Ordinal)) return bodyStart + 1;
        throw new InvalidOperationException("The generated Playground source mapping marker is not followed by a line break.");
    }

    public void Dispose() => _workspace.Dispose();
}
