using Luxel.Graphics.TwoD;
using Luxel.UI;
namespace Luxel.Controls;

/// <summary>Controls/ButtonCounter の Gallery/browser 共通 widget recipe。</summary>
public static class CanonicalCounterRecipe
{
    public const string Story = "Controls/ButtonCounter";
    public const string Recipe = "canonical-counter-v1";

    public sealed record Result(Widget Root, Button Minus, Widget Text, Button Plus, Signal<int> Count);

    public static Result Build(Signal<int>? count = null)
    {
        count ??= new Signal<int>(0);
        Button minus = Kit.Button(_ => count.Value--, "-");
        Widget text = Kit.Text($" {count} ", 22, color: Bind.From(() => UiTheme.T.Text), vAlign: Align.Center);
        Button plus = Kit.Button(_ => count.Value++, "+");
        Widget root = Kit.Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Kit.Center()[Kit.HStack(8)[minus, text, plus]]];
        return new Result(root, minus, text, plus, count);
    }
}

/// <summary>
/// 全コントロールのベアファクトリ。<c>using static Luxel.Controls.Kit;</c> で <c>Button(...)</c>/<c>Card(...)</c> 等を直接呼ぶ。
///
/// <para><b>per-widget ファクトリはソースジェネレーターが生成する</b> — 各コントロールの
/// <c>[UiComponent]</c> と <c>[UiParam]</c> フィールド由来 (生成先はアセンブリ既定 <c>[assembly: UiFactoryDefaults("Kit")]</c>)。
/// このファイルには sugar (VStack/HStack) と複合ヘルパ (テーマを焼き込む組み合わせ) だけを手書きする。</para>
///
/// <para><b>コントロールの構築は必ずファクトリ経由</b> — 全コントロールの ctor は <c>internal</c> で、
/// 外部アセンブリからの直接 <c>new</c> はコンパイルエラーになる (ファクトリは同一アセンブリ生成なので可)。</para>
///
/// <para><b>注意 (マルチ UI 島)</b>: 複合ヘルパ (Heading/Card/Badge/Alert 等) は構築時に
/// プロセス既定テーマ <see cref="UiTheme.Current"/> を閉包で購読する — **既定テーマ島専用**。
/// 別スレッドの UI 島 (自前テーマ signal を持つ UiHost) では使わず、プリミティブを直接組むこと。</para>
/// </summary>
public static partial class Kit
{
    // ---- スタック sugar ----
    /// <summary>縦スタック sugar (<c>Stack(vertical: true, ...)</c> の別名)。</summary>
    public static StackPanel VStack(float spacing = 0f,
        Length width = default, Length height = default)
        => Stack(true, spacing, width: width, height: height);

    /// <summary>横スタック sugar。</summary>
    public static StackPanel HStack(float spacing = 0f,
        Length width = default, Length height = default)
        => Stack(false, spacing, width: width, height: height);

    // ---- タイポグラフィ ----
    public static Text Heading(string text, int level = 1)
    {
        float size = level switch
        {
            1 => UiTheme.T.FontHeading,
            2 => UiTheme.T.FontLg,
            _ => UiTheme.T.Font * 1.17f,
        };
        return Text(text, size, color: Bind.From(() => UiTheme.T.Text));
    }

    public static Text Label(string text) => Text(text, UiTheme.T.Font, color: Bind.From(() => UiTheme.T.Text));
    public static Text Muted(string text) => Text(text, UiTheme.T.FontSm, color: Bind.From(() => UiTheme.T.TextMuted));

    // ---- サーフェス ----
    public static Border Card(Widget child)
        => Border(background: Bind.From(() => UiTheme.T.Surface), rounded: UiTheme.T.RadiusLg, padding: new Thickness(16))[child];

    public static Box Divider()
        => Box(background: Bind.From(() => UiTheme.T.BorderColor), height: 1f,
               hAlign: Align.Stretch, margin: new Thickness(0, 6));

    // ---- ラベル系 ----
    public static Border Badge(string text, Intent intent = Intent.Primary)
        => Border(background: Bind.From(() => Styles.Base(UiTheme.T, intent)), rounded: 999, padding: new Thickness(8, 2))
            [Text(text, UiTheme.T.FontSm, color: Bind.From(() => UiTheme.T.OnAccent))];

    public static Border Chip(string text)
        => Border(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 999, padding: new Thickness(10, 4))
            [Text(text, UiTheme.T.FontSm, color: Bind.From(() => UiTheme.T.Text))];

    public static Border Alert(string message, Intent intent = Intent.Info)
        => Border(
            background: Bind.From(() => Styles.Mix(UiTheme.T.Surface, Styles.Base(UiTheme.T, intent), 0.14f)),
            rounded: UiTheme.T.Radius, padding: new Thickness(12, 10))
            [HStack()[
                Icon(IconKind.Circle, 14, color: Bind.From(() => Styles.Base(UiTheme.T, intent)),
                     margin: new Thickness(0, 0, 8, 0), vAlign: Align.Center),
                Text(message, UiTheme.T.Font, color: Bind.From(() => UiTheme.T.Text))]];

    public static Box Skeleton(float width, float height)
        => Box(background: Bind.From(() => UiTheme.T.SurfaceAlt), rounded: 6f, width: width, height: height);

    // ---- ナビゲーション ----
    public static Border Breadcrumb(params string[] crumbs)
    {
        var children = new List<Widget>();
        for (int i = 0; i < crumbs.Length; i++)
        {
            children.Add(Text(crumbs[i], UiTheme.T.Font, color: Bind.From(() => UiTheme.T.Text)));
            if (i < crumbs.Length - 1)
                children.Add(Text(" / ", UiTheme.T.Font, color: Bind.From(() => UiTheme.T.TextMuted)));
        }
        return Border(padding: new Thickness(0))[HStack(spacing: 4)[children.ToArray()]];
    }
}
