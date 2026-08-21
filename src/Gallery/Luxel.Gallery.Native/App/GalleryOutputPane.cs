using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery;

/// <summary>Blazor Gallery のOutputパネルに合わせたNativeイベント一覧。</summary>
internal sealed class GalleryOutputPane(
    Signal<IReadOnlyList<StoryLogEntry>> entries,
    float width,
    GalleryChromeTokens chrome) : CompositeControl
{
    protected override Widget Build()
    {
        IReadOnlyList<StoryLogEntry> snapshot = entries.Value;
        var children = new List<Widget>
        {
            HStack(8)[
                Border(background: chrome.Success, rounded: 4,
                    width: 8, height: 8, margin: new Thickness(0, 3, 0, 0)),
                Text(NativeGalleryLabels.OutputReady, 12, color: chrome.TreeHoverText),
                Text(NativeGalleryLabels.OutputReadySummary, 12,
                    color: Bind.From(() => UiTheme.T.TextMuted))],
        };

        if (snapshot.Count == 0)
        {
            children.Add(Text(NativeGalleryLabels.OutputEmptySummary, 13,
                color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(2, 10, 0, 0)));
            return VStack(8)[children.ToArray()];
        }

        var rows = new List<Widget>(snapshot.Count);
        foreach (StoryLogEntry entry in snapshot)
        {
            string time = entry.Time.Length >= 8 ? entry.Time[..8] : entry.Time;
            float messageWidth = MathF.Max(80, width - 2 - 18 - 58 - 58 - 16);
            Widget content = HStack(8)[
                Text(time, 12, color: chrome.OutputTime, width: 58),
                Text("イベント", 12, color: chrome.OutputKind, width: 58),
                Text(entry.Message, 12, color: chrome.OutputText,
                    width: messageWidth, wrap: TextWrap.Word)];
            rows.Add(Border(background: chrome.Border, padding: new Thickness(1),
                rounded: 5, clip: true, width: width)[
                    Border(background: chrome.OutputRow, padding: new Thickness(9, 7),
                        rounded: 4, width: width - 2)[content]]);
        }
        children.Add(VStack(5)[rows.ToArray()]);
        return VStack(8)[children.ToArray()];
    }
}
