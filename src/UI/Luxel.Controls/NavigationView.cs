using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>An item displayed in a <see cref="NavigationView"/> pane.</summary>
public sealed record NavigationViewItem(string Path, string Label, bool IsEnabled = true);

/// <summary>
/// A fixed navigation pane and a single content child. Use
/// <c>NavigationView(navigation, items)[content]</c> like other layout controls.
/// </summary>
[UiComponent]
public sealed partial class NavigationView : CompositeControl
{
    /// <summary>Navigation state shared with the content.</summary>
    [UiParam] private readonly Bindable<Navigation> _navigation = new();
    /// <summary>Destinations displayed in the pane.</summary>
    [UiParam] private readonly Bindable<IReadOnlyList<NavigationViewItem>> _items = new([]);
    /// <summary>Whether the stable back-button row is displayed.</summary>
    [UiParam] private readonly Bindable<bool> _showBackButton = true;
    /// <summary>Width of the expanded navigation pane.</summary>
    [UiParam] private readonly Bindable<float> _paneWidth = 240f;
    /// <summary>Height of each navigation row.</summary>
    [UiParam] private readonly Bindable<float> _itemHeight = 40f;
    /// <summary>Pane background. Unset uses the current theme surface.</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _paneBackground = new();
    /// <summary>Normal item foreground. Unset uses muted theme text.</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _itemForeground = new();
    /// <summary>Selected item background. Unset uses the alternate theme surface.</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _selectedBackground = new();
    /// <summary>Selected item foreground. Unset uses the theme primary color.</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _selectedForeground = new();

    private Widget? _content;

    partial void OnConstruct()
    {
        HAlign.SetBase(Align.Stretch);
        VAlign.SetBase(Align.Stretch);
    }

    /// <summary>Assigns or replaces the single content child.</summary>
    public NavigationView this[Widget content]
    {
        get
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Rebuild();
            return this;
        }
    }

    /// <summary>The currently selected item, or null when the current path is not represented.</summary>
    public NavigationViewItem? SelectedItem
    {
        get
        {
            Navigation navigation = Navigation.Get();
            string path = navigation.CurrentPath;
            return Items.Get().FirstOrDefault(item =>
                StringComparer.Ordinal.Equals(NavigationPath.Normalize(item.Path), path));
        }
    }

    protected override Widget Build()
    {
        Navigation navigation = Navigation.Get();
        string currentPath = navigation.CurrentPath;
        bool canGoBack = navigation.CanGoBack;
        float rowHeight = MathF.Max(1, ItemHeight.Get());
        float paneWidth = MathF.Max(0, PaneWidth.Get());
        var rows = new List<Widget>();

        if (ShowBackButton.Get())
        {
            Button back = Button(_ => navigation.Back(), "‹  Back", height: rowHeight,
                hAlign: Align.Stretch, variant: Variant.Ghost,
                foreground: ItemForeground.Or(UiTheme.T.TextMuted));
            back.Enabled = canGoBack;
            rows.Add(back);
        }

        foreach (NavigationViewItem item in Items.Get())
        {
            string itemPath = NavigationPath.Normalize(item.Path);
            bool selected = StringComparer.Ordinal.Equals(itemPath, currentPath);
            Button row = Button(_ => navigation.Navigate(itemPath), item.Label,
                height: rowHeight, hAlign: Align.Stretch, variant: Variant.Ghost,
                background: selected ? SelectedBackground.Or(UiTheme.T.SurfaceAlt) : 0u,
                foreground: selected
                    ? SelectedForeground.Or(UiTheme.T.Primary)
                    : ItemForeground.Or(UiTheme.T.TextMuted));
            row.Enabled = item.IsEnabled;
            rows.Add(row);
        }

        Widget pane = Border(
            background: PaneBackground.Or(UiTheme.T.Surface),
            padding: new Thickness(8),
            width: paneWidth,
            hAlign: Align.Stretch,
            vAlign: Align.Stretch)[VStack(4)[rows.ToArray()]];
        pane.GridColumn(0);

        Widget content = _content ?? Spacer();
        content.GridColumn(1);
        content.HAlign.SetBase(Align.Stretch);
        content.VAlign.SetBase(Align.Stretch);

        return Grid(
            columns: [GridLength.Px(paneWidth), GridLength.Star()],
            rows: [GridLength.Star()],
            hAlign: Align.Stretch,
            vAlign: Align.Stretch)[pane, content];
    }
}
