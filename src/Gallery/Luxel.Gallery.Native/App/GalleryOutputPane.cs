using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery;

/// <summary>Blazor Gallery のOutputパネルに合わせたNativeイベント一覧。</summary>
internal sealed class GalleryOutputPane(
    Signal<IReadOnlyList<StoryLogEntry>> entries,
    float width) : CompositeControl
{
    protected override Widget Build()
    {
        IReadOnlyList<StoryLogEntry> snapshot = entries.Value;
        var children = new List<Widget>
        {
            HStack(8)[
                Border(background: GalleryChromeTheme.Success, rounded: 4,
                    width: 8, height: 8, margin: new Thickness(0, 3, 0, 0)),
                Text("準備完了", 11, color: GalleryChromeTheme.TreeHoverText),
                Text("Storyランタイムの準備が完了しました。", 11,
                    color: Bind.From(() => UiTheme.T.TextMuted))],
        };

        if (snapshot.Count == 0)
        {
            children.Add(Text("ランタイムのイベントとエラーがここに表示されます。", 13,
                color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(2, 10, 0, 0)));
            return VStack(8)[children.ToArray()];
        }

        var rows = new List<Widget>(snapshot.Count);
        foreach (StoryLogEntry entry in snapshot)
        {
            string time = entry.Time.Length >= 8 ? entry.Time[..8] : entry.Time;
            float messageWidth = MathF.Max(80, width - 2 - 18 - 58 - 50 - 16);
            Widget content = HStack(8)[
                Text(time, 11, color: GalleryChromeTheme.OutputTime, width: 58),
                Text("イベント", 10, color: GalleryChromeTheme.OutputKind, width: 50),
                Text(entry.Message, 11, color: GalleryChromeTheme.OutputText,
                    width: messageWidth, wrap: TextWrap.Word)];
            rows.Add(Border(background: GalleryChromeTheme.Border, padding: new Thickness(1),
                rounded: 5, clip: true, width: width)[
                    Border(background: GalleryChromeTheme.OutputRow, padding: new Thickness(9, 7),
                        rounded: 4, width: width - 2)[content]]);
        }
        children.Add(VStack(5)[rows.ToArray()]);
        return VStack(8)[children.ToArray()];
    }
}
