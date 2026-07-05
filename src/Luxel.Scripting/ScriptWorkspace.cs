using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.Scripting;

/// <summary>補完候補 1 件 (エディタのポップアップ用)。</summary>
public sealed record CompletionItem(string Label, string InsertText, string Kind);

/// <summary>ホバー情報 (シンボルの下に出る型/説明)。</summary>
public sealed record HoverInfo(string Text);

/// <summary>
/// スクリプトの言語サービス — 補完/ホバーを **in-proc の Roslyn ワークスペース**で提供する
/// (LSP プロトコルは挟まない — 内蔵エディタと同一プロセスなので直接ホストする方が速く単純)。
/// <see cref="ScriptHost"/> と同じ references/usings を与えると、**エンジン API が型付きで補完される**
/// (C# スクリプトを選んだ最大の配当)。
/// <para>ワークスペースは 1 つの script Document を使い回し、<see cref="Complete"/>/<see cref="Hover"/>
/// のたびにソースを差し替える (AdhocWorkspace は軽量)。スレッド非安全 — UI スレッド専有。</para>
/// </summary>
public sealed class ScriptWorkspace : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly DocumentId _docId;
    private readonly CompletionService _completion;
    private readonly QuickInfoService _quickInfo;

    public ScriptWorkspace(IEnumerable<Assembly> references, IEnumerable<string> usings)
    {
        var hostServices = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        _workspace = new AdhocWorkspace(hostServices);

        var refs = references
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compOptions = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            usings: usings.ToArray());
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            kind: SourceCodeKind.Script);

        var projInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "Script", "Script", LanguageNames.CSharp,
            compilationOptions: compOptions, parseOptions: parseOptions, metadataReferences: refs,
            isSubmission: true, hostObjectType: typeof(object));
        Project project = _workspace.AddProject(projInfo);

        var docInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id), "script.csx",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())));
        Document doc = _workspace.AddDocument(docInfo);
        _docId = doc.Id;
        _completion = CompletionService.GetService(doc)
            ?? throw new InvalidOperationException("CompletionService を取得できません");
        _quickInfo = QuickInfoService.GetService(doc)
            ?? throw new InvalidOperationException("QuickInfoService を取得できません");
    }

    private Document WithCode(string code)
    {
        Solution updated = _workspace.CurrentSolution.WithDocumentText(_docId, SourceText.From(code));
        _workspace.TryApplyChanges(updated);   // AdhocWorkspace は単一書込者 (UI スレッド専有) — 競合しない
        return _workspace.CurrentSolution.GetDocument(_docId)!;
    }

    /// <summary>position (0 始まりの文字オフセット) での補完候補。空なら該当なし。</summary>
    public IReadOnlyList<CompletionItem> Complete(string code, int position)
    {
        position = Math.Clamp(position, 0, code.Length);
        Document doc = WithCode(code);
        CompletionList list = _completion.GetCompletionsAsync(doc, position).GetAwaiter().GetResult();
        if (list is null) return [];
        var items = new List<CompletionItem>(list.ItemsList.Count);
        foreach (Microsoft.CodeAnalysis.Completion.CompletionItem it in list.ItemsList)
        {
            string kind = it.Tags.Length > 0 ? it.Tags[0] : "";
            items.Add(new CompletionItem(it.DisplayText, it.FilterText, kind));
        }
        return items;
    }

    /// <summary>コードのコンパイル診断 (エラー/警告、1 始まりの行/桁 + 長さ) — 波線表示用。</summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnose(string code)
    {
        Document doc = WithCode(code);
        Compilation? comp = doc.Project.GetCompilationAsync().GetAwaiter().GetResult();
        if (comp is null) return [];
        var list = new List<ScriptDiagnostic>();
        foreach (Diagnostic d in comp.GetDiagnostics())
        {
            if (d.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) continue;
            FileLinePositionSpan span = d.Location.GetLineSpan();
            int len = Math.Max(1, span.EndLinePosition.Character - span.StartLinePosition.Character);
            list.Add(new ScriptDiagnostic(
                span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                d.GetMessage(), d.Severity == DiagnosticSeverity.Error)
            { Length = len });
        }
        return list;
    }

    /// <summary>position のシンボルのホバー情報 (型/シグネチャ/doc)。無ければ null。</summary>
    public HoverInfo? Hover(string code, int position)
    {
        position = Math.Clamp(position, 0, code.Length);
        Document doc = WithCode(code);
        QuickInfoItem? info = _quickInfo.GetQuickInfoAsync(doc, position).GetAwaiter().GetResult();
        if (info is null) return null;
        string text = string.Concat(info.Sections.SelectMany(s => s.TaggedParts).Select(p => p.Text));
        return text.Length == 0 ? null : new HoverInfo(text);
    }

    public void Dispose() => _workspace.Dispose();
}
