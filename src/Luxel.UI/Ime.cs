namespace Luxel.UI;

/// <summary>
/// IME の変換中(preedit)状態。<see cref="Text"/>=未確定文字列、<see cref="TargetStart"/>/<see cref="TargetLen"/>
/// =変換対象節 (強調表示する範囲)、<see cref="Caret"/>=変換中の caret 位置 (Text 内オフセット)。
/// </summary>
public readonly record struct ImeComposition(string Text, int Caret = 0, int TargetStart = 0, int TargetLen = 0)
{
    public bool IsEmpty => string.IsNullOrEmpty(Text);
}

/// <summary>
/// テキスト入力コントロールが公開する編集面 (TSF / IME ブリッジが操作する)。座標は canvas 座標。
/// </summary>
public interface ITextInput
{
    string Text { get; }
    (int start, int length) Selection { get; }
    void Select(int start, int end);
    void Replace(int start, int end, string s);   // ACP 範囲を置換 (TSF SetText)
    void SetComposition(ImeComposition comp);
    void CommitComposition(string final);
    /// <summary>caret の矩形 (canvas 座標)。IME 候補ウィンドウ配置に使う。</summary>
    Rect CaretRect { get; }

    /// <summary>実 IME (TSF) の変換装飾。preedit は既に文書テキストとして挿入済み (SetText 経由) で、
    /// これは**装飾だけ**の通知 — [start, start+length) に下線、[targetStart, targetStart+targetLength) に
    /// 変換対象節の強調。length=0 で解除。範囲は文書 (=現在ブロック) 内オフセット。既定は無視。</summary>
    void SetCompositionHighlight(int start, int length, int targetStart, int targetLength) { }
}
