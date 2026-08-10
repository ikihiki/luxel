using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>ストーリー共通の下回り (背景フレーム、フォントフォールバック、エディタ書体)。</summary>
public static class StoryKit
{
    /// <summary>テーマ背景 + 余白 + 中央寄せの定番フレーム。</summary>
    public static Border Frame(Widget child) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[child]];

    /// <summary>本文 (日本語+ラテン, 同梱 BIZ UDGothic) + カラー絵文字 (COLR — 無い環境では省略) の
    /// フォールバック連鎖。BIZ UDGothic がラテンもカバーするので基本 1 本 + 絵文字。
    /// 絵文字のみシステム依存 (seguiemj) を許容 — その絵文字 golden はこのマシン専用。</summary>
    public static readonly Lazy<FontCollection> JpFallback = new(() =>
    {
        VectorFont host = GalleryFonts.Load(GalleryFonts.Regular);
        VectorFont? emoji = null;
        try { emoji = VectorFont.LoadSystem("seguiemj.ttf"); } catch { /* 絵文字フォント無し */ }
        return emoji is null ? new FontCollection(host) : new FontCollection(host, emoji);
    });

    /// <summary>リッチエディタ用の書体。太字/等幅は同梱フォント (マシン非依存)。
    /// 斜体は BIZ UDGothic に無い → null (RichTextEditor が通常フォントで代用、golden は決定的)。</summary>
    public static readonly Lazy<(VectorFont? Bold, VectorFont? Italic, VectorFont? BoldItalic, VectorFont? Mono)> EditorFaces = new(() =>
    {
        VectorFont bold = GalleryFonts.Load(GalleryFonts.Bold);
        VectorFont mono = GalleryFonts.Load(GalleryFonts.Mono);
        return (bold, null, null, mono);
    });
}
