using System.Text.Json;
using Luxel.Controls;
using Luxel.Gallery.Playground;
using Luxel.Scripting.Roslyn.Web;
using Luxel.UI;

namespace Luxel.Scripting.Gallery;

/// <summary>Browser-safe Roslyn compiler, execution, diagnostics, and Playground adapter used by Gallery WASM.</summary>
public sealed class BrowserRoslynGalleryRuntime
{
    private readonly WebScriptCompiler _compiler;
    private readonly WebScriptExecutor _executor = new();

    public BrowserRoslynGalleryRuntime(IReadOnlyList<MetadataReferenceImage> references)
    {
        _compiler = new WebScriptCompiler(references);
        Language = new BrowserRoslynCodeLanguage(_compiler);
        Playground = new BrowserRoslynPlaygroundRunner(_compiler, _executor);
    }

    public ICodeLanguage Language { get; }
    public INativePlaygroundRunner Playground { get; }

    public Task<BrowserRoslynRunResult> RunAsync(
        string source,
        Action<string>? log = null,
        string fileName = WebScriptCompiler.ScriptFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WebScriptCompilation compilation = _compiler.Compile(
            WebScriptProject.FromSource(source, fileName),
            $"Luxel.Gallery.Script.{Guid.NewGuid():N}");
        if (!compilation.Success || compilation.PeImage is null)
            return Task.FromResult(new BrowserRoslynRunResult(false, null, compilation.Diagnostics, null));
        try
        {
            WebScriptOutput.SetSink(log);
            WebScriptExecution execution = _executor.Execute(compilation.PeImage, compilation.PdbImage ?? []);
            return Task.FromResult(new BrowserRoslynRunResult(
                execution.Success, execution.Widget, compilation.Diagnostics, execution.Failure));
        }
        finally
        {
            WebScriptOutput.SetSink(null);
        }
    }

    public static async Task<MetadataReferenceImage[]> LoadReferencesAsync(
        HttpClient http,
        string basePath = "references",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        string json = await http.GetStringAsync($"{basePath}/manifest.json", cancellationToken);
        ReferenceManifest manifest = JsonSerializer.Deserialize<ReferenceManifest>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The browser Roslyn reference manifest is empty.");
        var references = new List<MetadataReferenceImage>(manifest.Assemblies.Length);
        foreach (string fileName in manifest.Assemblies)
            references.Add(new MetadataReferenceImage(
                fileName,
                await http.GetByteArrayAsync($"{basePath}/{fileName}", cancellationToken)));
        return references.ToArray();
    }

    private sealed record ReferenceManifest(int Version, string[] Assemblies);
}

public sealed record BrowserRoslynRunResult(
    bool Success,
    Widget? Widget,
    IReadOnlyList<WebScriptDiagnostic> Diagnostics,
    WebScriptFailure? Failure);

/// <summary>Sync TextEditorView bridge using the browser compiler for deterministic diagnostics.</summary>
public sealed class BrowserRoslynCodeLanguage(WebScriptCompiler compiler) : ICodeLanguage
{
    public IReadOnlyList<CodeCompletion> Complete(string code, int position) => [];

    public IReadOnlyList<CodeDiagnostic> Diagnose(string code)
        => compiler.Compile(code).Diagnostics
            .Where(diagnostic => diagnostic.Line is not null && diagnostic.Column is not null)
            .Select(diagnostic => new CodeDiagnostic(
                diagnostic.Line!.Value,
                diagnostic.Column!.Value,
                Math.Max(1, diagnostic.Length),
                diagnostic.Message,
                diagnostic.Severity == WebScriptDiagnosticSeverity.Error))
            .ToArray();

    public string? Hover(string code, int position) => null;
}

/// <summary>Browser implementation of the existing multi-file Playground runner seam.</summary>
public sealed class BrowserRoslynPlaygroundRunner(
    WebScriptCompiler compiler,
    WebScriptExecutor executor) : INativePlaygroundRunner
{
    public Task<NativePlaygroundRunResult> RunAsync(
        NativePlaygroundSession session,
        CancellationToken cancellationToken = default)
        => RunAsync(session.Draft, cancellationToken);

    public Task<NativePlaygroundRunResult> RunAsync(
        PlaygroundDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaygroundFile entry = draft.MainFile;
        var project = new WebScriptProject(
            new WebScriptDocument(entry.Path, entry.Source),
            draft.Files
                .Where(file => file.Id != entry.Id
                    && string.Equals(Path.GetExtension(file.Path), ".cs", StringComparison.OrdinalIgnoreCase))
                .Select(file => new WebScriptDocument(file.Path, file.Source))
                .ToArray());
        WebScriptCompilation compilation = compiler.Compile(project);
        if (!compilation.Success || compilation.PeImage is null)
            return Task.FromResult(new NativePlaygroundRunResult(false, null, compilation.Diagnostics, null));
        WebScriptExecution execution = executor.Execute(compilation.PeImage, compilation.PdbImage ?? []);
        return Task.FromResult(new NativePlaygroundRunResult(
            execution.Success, execution.Widget, compilation.Diagnostics, execution.Failure));
    }
}
