namespace Luxel.UI;

/// <summary>
/// 1 行テキスト編集モデル (純粋ロジック・テスト可能)。caret/選択/挿入削除/単語移動/IME 合成を扱う。
/// </summary>
public sealed class TextEditor
{
    public string Text { get; private set; } = "";
    public int Caret { get; private set; }
    public int Anchor { get; private set; }       // 選択の固定端
    public string Composition { get; private set; } = "";   // IME 編集中 (preedit)

    public bool HasSelection => Caret != Anchor;
    public int SelMin => Math.Min(Caret, Anchor);
    public int SelMax => Math.Max(Caret, Anchor);

    public void SetText(string t)
    {
        Text = t ?? "";
        Caret = Math.Clamp(Caret, 0, Text.Length);
        Anchor = Caret;
    }

    public void Insert(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        DeleteSelection();
        Text = Text[..Caret] + s + Text[Caret..];
        Caret += s.Length; Anchor = Caret;
    }

    public void Backspace()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (Caret > 0)
        {
            int prev = PrevGrapheme(Text, Caret);
            Text = Text[..prev] + Text[Caret..];
            Caret = prev; Anchor = Caret;
        }
    }

    public void DeleteForward()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (Caret < Text.Length) Text = Text[..Caret] + Text[NextGrapheme(Text, Caret)..];
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        int a = SelMin, b = SelMax;
        Text = Text[..a] + Text[b..];
        Caret = a; Anchor = a;
    }

    /// <summary>ACP 範囲で選択する (TSF 用)。</summary>
    public void Select(int start, int end)
    {
        Anchor = Math.Clamp(start, 0, Text.Length);
        Caret = Math.Clamp(end, 0, Text.Length);
    }

    /// <summary>[start,end) を s で置換する (TSF SetText 用)。</summary>
    public void Replace(int start, int end, string s)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, start, Text.Length);
        Text = Text[..start] + (s ?? "") + Text[end..];
        Caret = start + (s?.Length ?? 0);
        Anchor = Caret;
    }

    public void MoveLeft(bool select) { if (Caret > 0) Caret = PrevGrapheme(Text, Caret); if (!select) Anchor = Caret; }
    public void MoveRight(bool select) { if (Caret < Text.Length) Caret = NextGrapheme(Text, Caret); if (!select) Anchor = Caret; }

    /// <summary>index の直前のグラフェム境界 (UAX#29 — サロゲート/結合文字を割らない移動・削除単位)。</summary>
    internal static int PrevGrapheme(string text, int index)
    {
        int prev = 0;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            int end = e.ElementIndex + ((string)e.Current).Length;
            if (end >= index) return e.ElementIndex;
            prev = end;
        }
        return prev;
    }

    /// <summary>index の直後のグラフェム境界。</summary>
    internal static int NextGrapheme(string text, int index)
    {
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            int end = e.ElementIndex + ((string)e.Current).Length;
            if (end > index) return end;
        }
        return text.Length;
    }
    public void Home(bool select) { Caret = 0; if (!select) Anchor = Caret; }
    public void End(bool select) { Caret = Text.Length; if (!select) Anchor = Caret; }
    public void SelectAll() { Anchor = 0; Caret = Text.Length; }
    public void PlaceCaret(int index) { Caret = Math.Clamp(index, 0, Text.Length); Anchor = Caret; }

    // ---- IME ----
    /// <summary>変換対象節 (Composition 内のオフセット/長さ)。強調表示用。</summary>
    public int CompTargetStart { get; private set; }
    public int CompTargetLen { get; private set; }

    public void SetComposition(string preedit, int targetStart = 0, int targetLen = 0)
    {
        Composition = preedit ?? "";
        CompTargetStart = Math.Clamp(targetStart, 0, Composition.Length);
        CompTargetLen = Math.Clamp(targetLen, 0, Composition.Length - CompTargetStart);
    }

    public void CommitComposition(string final)
    {
        Composition = ""; CompTargetStart = 0; CompTargetLen = 0;
        Insert(final);
    }

    /// <summary>表示用テキスト (合成中は caret 位置に preedit を差し込む)。</summary>
    public string Display => Composition.Length == 0 ? Text : Text[..Caret] + Composition + Text[Caret..];
    /// <summary>表示上の caret 位置 (合成中は preedit 末尾)。</summary>
    public int DisplayCaret => Caret + Composition.Length;

    /// <summary>Display 内での preedit 範囲 (start,len)。合成中でなければ len=0。</summary>
    public (int start, int len) CompositionDisplayRange => (Caret, Composition.Length);
    /// <summary>Display 内での変換対象節範囲 (start,len)。</summary>
    public (int start, int len) TargetDisplayRange => (Caret + CompTargetStart, CompTargetLen);
}
