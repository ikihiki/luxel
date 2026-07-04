using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>ストーリー共通の下回り (背景フレーム、フォントフォールバック、エディタ書体)。</summary>
internal static class StoryKit
{
    /// <summary>テーマ背景 + 余白 + 中央寄せの定番フレーム。</summary>
    internal static Border Frame(Widget child) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[child]];

    /// <summary>UI + 日本語 + カラー絵文字 (COLR — 無い環境では省略) のフォールバック連鎖。</summary>
    internal static readonly Lazy<FontCollection> JpFallback = new(() =>
    {
        VectorFont? emoji = null;
        try { emoji = VectorFont.LoadSystem("seguiemj.ttf"); } catch { /* 絵文字フォント無し */ }
        return emoji is null
            ? new FontCollection(VectorFont.LoadSystem(), VectorFont.LoadSystemJapanese())
            : new FontCollection(VectorFont.LoadSystem(), VectorFont.LoadSystemJapanese(), emoji);
    });

    /// <summary>リッチエディタ用の書体 (太字/斜体/等幅。無ければ通常フォント代用)。</summary>
    internal static readonly Lazy<(VectorFont? Bold, VectorFont? Italic, VectorFont? BoldItalic, VectorFont? Mono)> EditorFaces = new(() =>
    {
        VectorFont? Try(params string[] names)
        {
            try { return VectorFont.LoadSystem(names); } catch { return null; }
        }
        return (Try("segoeuib.ttf", "arialbd.ttf"), Try("segoeuii.ttf", "ariali.ttf"),
                Try("segoeuiz.ttf", "arialbi.ttf"), Try("consola.ttf", "cour.ttf"));
    });
}
