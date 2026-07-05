namespace Luxel.Controls;

/// <summary>補完候補 1 件 (CodeEditor のポップアップ用 — 言語サービス非依存の DTO)。</summary>
public readonly record struct CodeCompletion(string Label, string InsertText, string Kind);

/// <summary>診断 1 件 (波線 + ガターマーカー用)。座標は 1 始まりの行/桁。</summary>
public readonly record struct CodeDiagnostic(int Line, int Column, int Length, string Message, bool IsError);

/// <summary>
/// CodeEditor が使う言語サービスの契約。実装 (C# = Roslyn、将来は他言語) を外から注入する —
/// **Controls はここに Roslyn を持ち込まない** (LSP のエディタ⇄サーバー分離と同じ疎結合)。
/// すべて同期 (コードは短い前提)。UI スレッドから呼ばれる。
/// </summary>
public interface ICodeLanguage
{
    /// <summary>キャレット位置 (全文フラットオフセット) の補完候補。空なら該当なし。</summary>
    IReadOnlyList<CodeCompletion> Complete(string code, int position);

    /// <summary>コード全体のコンパイル診断 (波線)。</summary>
    IReadOnlyList<CodeDiagnostic> Diagnose(string code);

    /// <summary>position のシンボルのホバー情報 (型/シグネチャ)。無ければ null。</summary>
    string? Hover(string code, int position);
}
