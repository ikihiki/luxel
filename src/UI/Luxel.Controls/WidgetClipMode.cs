namespace Luxel.Controls;

/// <summary>
/// ホストしたインライン widget (ノード内 / テキスト行内) をどうクリップするか。
/// エディタ系ビュー (<see cref="NodeGraphView"/> / <see cref="TextEditorView"/>) の <c>WidgetClip</c> で指定する。
/// </summary>
public enum WidgetClipMode
{
    /// <summary>宣言された枠 (スロット矩形) でクリップする — はみ出しを隠す (既定)。</summary>
    Box,
    /// <summary>クリップしない — widget が意図的に枠外へ描く場合 (ポップアップ等) の逃げ道。</summary>
    None,
}
