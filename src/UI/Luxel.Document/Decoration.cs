namespace Luxel.Document;

/// <summary>下線スタイル — 色と波線か否か (C# 診断の実線/波線に使う)。</summary>
public readonly record struct UnderlineStyle(uint Color, bool Wavy = false);

/// <summary>囲みスタイル — 枠色・任意の塗り・角丸半径 (Strudel の再生シーケンス囲みに使う)。</summary>
public readonly record struct BoxStyle(uint Stroke, uint? Fill = null, float Radius = 0f);

/// <summary>フォント変種 — 合成できないため view が別 VectorFont を供給する
/// (<see cref="EditorConfig"/> の各スロット)。Markdown/リッチ文書の太字・斜体・見出し・インラインコードに使う。</summary>
public enum FontVariant { Bold, Italic, BoldItalic, Mono }

/// <summary>アンカー widget を文字の前/後どちらに置くか。</summary>
public enum WidgetSide { Before, After }

/// <summary>
/// テキストへの**装飾** — 位置 (フラットオフセット) を持ち、編集 (<see cref="ChangeSet"/>) を通すと
/// 位置が写る (<see cref="Map"/>)。種別は Mark/Widget/Line/LinePrefix/Block。全種が同じ写像機構に乗ることで、
/// シンタックス色・診断波線・検索背景・再生囲み・行内 widget・行頭番号を一様に扱える。
/// <see cref="AffectsLayout"/> は「行の再レイアウトを要するか」の分類 (色/widget/prefix=要 / 背景/下線/囲み=不要)。
/// </summary>
public abstract record Decoration
{
    /// <summary>並び替え用の開始オフセット。</summary>
    public abstract int SortFrom { get; }
    /// <summary>並び替え用の終了オフセット。</summary>
    public abstract int SortTo { get; }
    /// <summary>行の再レイアウト (テキストノード再構築/空間占有) を要するか。false = 矩形オーバーレイのみ。</summary>
    public abstract bool AffectsLayout { get; }
    /// <summary>変更セットで位置を写した装飾を返す。範囲が丸ごと消えた等で無効なら null。</summary>
    public abstract Decoration? Map(ChangeSet cs);
}

/// <summary>範囲 <c>[From, To)</c> への装飾 — 前景色 (色分け)・背景 (検索帯)・下線/波線 (診断)・囲み (再生位置) を
/// 同時指定できる。<see cref="InclusiveStart"/>/<see cref="InclusiveEnd"/> は端での挿入を含めるか
/// (既定 false = 端の外側の挿入は含めない・内側のタイプは範囲を伸ばす)。</summary>
public sealed record MarkDecoration(
    int From, int To,
    uint? Foreground = null,
    uint? Background = null,
    UnderlineStyle? Underline = null,
    BoxStyle? Box = null,
    bool InclusiveStart = false,
    bool InclusiveEnd = false,
    FontVariant? Variant = null,
    float? FontScale = null,
    bool Hidden = false) : Decoration
{
    /// <inheritdoc/>
    public override int SortFrom => From;
    /// <inheritdoc/>
    public override int SortTo => To;
    /// <summary>前景色・フォント変種・サイズ倍率・非表示のいずれかを持つ場合にレイアウト (ラン分割/幅0 畳み) に効く。
    /// 背景/下線/囲みはオーバーレイのみ。</summary>
    public override bool AffectsLayout => Foreground.HasValue || Variant.HasValue || FontScale.HasValue || Hidden;

    /// <inheritdoc/>
    public override Decoration? Map(ChangeSet cs)
    {
        int nf = cs.MapPos(From, InclusiveStart ? -1 : 1);
        int nt = cs.MapPos(To, InclusiveEnd ? 1 : -1);
        if (nt < nf) nt = nf;
        if (From < To && nf == nt) return null;   // 範囲が丸ごと削除された
        return this with { From = nf, To = nt };
    }
}

/// <summary>行内 widget — <c>From==To</c> ならアンカー挿入 (ソース 0 文字、<see cref="Side"/> で前後)、
/// <c>From&lt;To</c> なら範囲置換。表示上 <see cref="Width"/>×<see cref="Height"/> を占有し、view が
/// <see cref="Key"/> から実 Widget を解決する (コアは Widget 型を知らない = Luxel.UI 非依存)。</summary>
public sealed record WidgetDecoration(
    int From, int To, float Width, float Height, object Key, WidgetSide Side = WidgetSide.After) : Decoration
{
    /// <summary>アンカー (点) 挿入か。</summary>
    public bool IsAnchor => From == To;
    /// <inheritdoc/>
    public override int SortFrom => From;
    /// <inheritdoc/>
    public override int SortTo => To;
    /// <summary>空間を占有するので常にレイアウトに効く。</summary>
    public override bool AffectsLayout => true;

    /// <inheritdoc/>
    public override Decoration? Map(ChangeSet cs)
    {
        if (IsAnchor)
        {
            int p = cs.MapPos(From, Side == WidgetSide.Before ? -1 : 1);
            return this with { From = p, To = p };
        }
        int nf = cs.MapPos(From, 1), nt = cs.MapPos(To, -1);
        if (nt <= nf) return null;   // 置換対象が消えた
        return this with { From = nf, To = nt };
    }
}

/// <summary>複数ソース行 <c>[From, To)</c> を占有する**ブロック widget** — 表/図 (mermaid)/数式/埋め込みライブ UI など。
/// geometry は範囲の先頭行を宣言高さ <see cref="Height"/> の**全幅スロット**にし、残りの被覆行を高さ 0 に畳む
/// (行 ↔ ソースの 1:1 対応は保つのでキャレット/ヒットは壊れない)。view が <see cref="Key"/> から実 Widget を解決する。</summary>
public sealed record BlockWidgetDecoration(int From, int To, object Key, float Height) : Decoration
{
    /// <inheritdoc/>
    public override int SortFrom => From;
    /// <inheritdoc/>
    public override int SortTo => To;
    /// <summary>行の占有 (高さ/畳み) を変えるのでレイアウトに効く。</summary>
    public override bool AffectsLayout => true;
    /// <inheritdoc/>
    public override Decoration? Map(ChangeSet cs)
    {
        int nf = cs.MapPos(From, 1), nt = cs.MapPos(To, -1);
        if (nt < nf) nt = nf;
        if (From < To && nf == nt) return null;   // 範囲が丸ごと削除された
        return this with { From = nf, To = nt };
    }
}

/// <summary>行全体の背景 (現在行・実行中の行)。<see cref="At"/> はその行内の任意オフセット (行は編集で移っても追従)。</summary>
public sealed record LineDecoration(int At, uint Background) : Decoration
{
    /// <inheritdoc/>
    public override int SortFrom => At;
    /// <inheritdoc/>
    public override int SortTo => At;
    /// <summary>オーバーレイのみ。</summary>
    public override bool AffectsLayout => false;
    /// <inheritdoc/>
    public override Decoration? Map(ChangeSet cs) => this with { At = cs.MapPos(At) };
}

/// <summary>行頭装飾 — 番号 "1." や箇条書き記号などの表示専用テキスト (ソース 0 文字 ↔ 表示 k 文字)。
/// <see cref="At"/> は対象行内のオフセット。番号採番は供給側 (format 層) が行う。</summary>
public sealed record LinePrefixDecoration(int At, string Text, uint Color) : Decoration
{
    /// <inheritdoc/>
    public override int SortFrom => At;
    /// <inheritdoc/>
    public override int SortTo => At;
    /// <summary>行頭に幅を占有するのでレイアウトに効く。</summary>
    public override bool AffectsLayout => true;
    /// <inheritdoc/>
    public override Decoration? Map(ChangeSet cs) => this with { At = cs.MapPos(At) };
}

/// <summary>行グループ <c>[From, To)</c> への装飾 — 背景や左縦バー (引用/コードブロック)。
/// <see cref="Indent"/> はブロック内の行を右へずらす左余白 px (縦バー/マーカーの場所を確保し、
/// 行頭 prefix やテキストと重ならないようにする)。背景/バーはインデント前の左端に描かれる。</summary>
public sealed record BlockDecoration(int From, int To, uint? Background = null, uint? BarColor = null,
    float BarWidth = 3f, float Indent = 0f, float Radius = 0f) : Decoration
{
    /// <inheritdoc/>
    public override int SortFrom => From;
    /// <inheritdoc/>
    public override int SortTo => To;
    /// <summary>オーバーレイのみ。</summary>
    public override bool AffectsLayout => false;
    /// <inheritdoc/>
    public override Decoration? Map(ChangeSet cs)
    {
        int nf = cs.MapPos(From, 1), nt = cs.MapPos(To, -1);
        if (nt < nf) nt = nf;
        if (From < To && nf == nt) return null;
        return this with { From = nf, To = nt };
    }
}
