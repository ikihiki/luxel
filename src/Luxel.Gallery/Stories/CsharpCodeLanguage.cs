using Luxel.Controls;
using Luxel.Scripting;

namespace Luxel.Gallery.Stories;

/// <summary>
/// <see cref="ICodeLanguage"/> の C# 実装 — <see cref="ScriptWorkspace"/> (in-proc Roslyn) を
/// CodeEditor 向けの DTO へ橋渡しする。**この橋は Controls と Scripting の両方を参照する層
/// (Gallery) に置く** — Controls は Roslyn を知らない (LSP のエディタ⇄サーバー分離と同じ)。
/// </summary>
public sealed class CsharpCodeLanguage(ScriptWorkspace ws) : ICodeLanguage
{
    public IReadOnlyList<CodeCompletion> Complete(string code, int position)
    {
        var outp = new List<CodeCompletion>();
        foreach (Luxel.Scripting.CompletionItem it in ws.Complete(code, position))
            outp.Add(new CodeCompletion(it.Label, it.InsertText, it.Kind));
        return outp;
    }

    public IReadOnlyList<CodeDiagnostic> Diagnose(string code)
    {
        var outp = new List<CodeDiagnostic>();
        foreach (ScriptDiagnostic d in ws.Diagnose(code))
            outp.Add(new CodeDiagnostic(d.Line, d.Column, d.Length, d.Message, d.IsError));
        return outp;
    }

    public string? Hover(string code, int position) => ws.Hover(code, position)?.Text;
}
