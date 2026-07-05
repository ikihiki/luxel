namespace Luxel.Typography;

/// <summary>リッチテキストの 1 スパン (連続した同一スタイルのテキスト)。\n を含んでよい。</summary>
public readonly record struct TextSpan(string Text, SpanStyle Style)
{
    public TextSpan(string text) : this(text, SpanStyle.Default) { }
}

/// <summary>スパン単位のスタイル。null のプロパティはレイアウト既定 (フォント/サイズ) を使う。</summary>
public sealed record SpanStyle
{
    public static readonly SpanStyle Default = new();

    /// <summary>フォント (null = レイアウト既定。フォールバックは <see cref="FontCollection"/> が担う)。</summary>
    public VectorFont? Font { get; init; }
    /// <summary>文字サイズ px (null = レイアウト既定)。行内ベースラインは最大 ascent に揃う。</summary>
    public float? Size { get; init; }
    /// <summary>文字色。RichTextView が色ごとにノード分割して描く (保持型の 1 ノード 1 色制約)。</summary>
    public uint Color { get; init; } = 0xFF000000;

    /// <summary>インラインボックス幅 (0 = 通常テキスト)。&gt; 0 のスパンは**グリフを描かず**
    /// 幅 <see cref="BoxW"/> × 高さ <see cref="BoxH"/> の占位になる (文中 widget 等 —
    /// 呼び出し側が矩形位置に実体を重ねる)。ボックス下端がベースラインに揃う。
    /// テキストは占位 1 文字 (U+FFFC 推奨) にすること — キャレット/選択は 1 文字として扱われる。</summary>
    public float BoxW { get; init; }
    /// <summary>インラインボックス高さ (<see cref="BoxW"/> &gt; 0 のとき有効)。行高へ寄与する。</summary>
    public float BoxH { get; init; }
}

/// <summary>
/// 優先順フォント列。グリフ未収載の文字を次候補へフォールバックする (例: [UI フォント, 日本語フォント])。
/// フォントの寿命は所有しない (呼び出し側が Dispose を管理)。
/// </summary>
public sealed class FontCollection
{
    private readonly VectorFont[] _fonts;

    public FontCollection(params VectorFont[] fonts)
    {
        if (fonts.Length == 0) throw new ArgumentException("フォントを 1 つ以上指定してください");
        _fonts = fonts;
    }

    public IReadOnlyList<VectorFont> Fonts => _fonts;
    public VectorFont Primary => _fonts[0];

    /// <summary>コードポイントを収載する最初のフォント (どれも無ければ先頭 = .notdef 豆腐)。
    /// <paramref name="preferred"/> があればそれを最優先候補にする。</summary>
    public VectorFont FontFor(int codepoint, VectorFont? preferred = null)
    {
        if (preferred is not null && preferred.HasGlyph(codepoint)) return preferred;
        foreach (VectorFont f in _fonts)
            if (f.HasGlyph(codepoint)) return f;
        return preferred ?? _fonts[0];
    }
}
