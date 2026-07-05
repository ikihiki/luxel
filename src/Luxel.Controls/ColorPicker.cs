using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>
/// カラーピッカー。スウォッチ + hex 表示のトリガーをクリックすると、
/// パレット + R/G/B スライダー + hex 入力 (正規表現規制) のオーバーレイが開く。
/// 値は RGBA の <see cref="Signal{T}"/> 束縛 — どの経路の編集も即時に部分更新で反映される。
/// </summary>
[UiComponent]
public sealed partial class ColorPicker : Widget
{
    private static readonly uint[] Palette =
    [
        Color2D.Rgba(255, 255, 255), Color2D.Rgba(200, 205, 214), Color2D.Rgba(112, 118, 130), Color2D.Rgba(28, 30, 36),
        Color2D.Rgba(220, 72, 72), Color2D.Rgba(230, 150, 50), Color2D.Rgba(214, 190, 46), Color2D.Rgba(46, 160, 90),
        Color2D.Rgba(60, 176, 220), Color2D.Rgba(56, 118, 224), Color2D.Rgba(128, 90, 220), Color2D.Rgba(220, 80, 160),
    ];

    /// <summary>RGBA 色の signal (パレット/スライダー/hex どの経路の編集も書き戻す)。</summary>
    [UiParam] private readonly Bindable<Signal<uint>> _color = new();

    private readonly Signal<bool> _open = new(false);
    private const float DefaultWidth = 104;
    private float _h = 38;   // レイアウト時にテーマから確定
    private float W = DefaultWidth;   // PerformLayout で解決 (% / em / vw 対応)

    public override string? DebugDetail => ToHex(Color.Get().Value);

    /// <summary>"#rrggbb" (α≠255 なら "#rrggbbaa")。</summary>
    public static string ToHex(uint c)
    {
        uint r = c & 0xFF, g = (c >> 8) & 0xFF, b = (c >> 16) & 0xFF, a = (c >> 24) & 0xFF;
        return a == 255 ? $"#{r:x2}{g:x2}{b:x2}" : $"#{r:x2}{g:x2}{b:x2}{a:x2}";
    }

    /// <summary>"#rgb" / "#rrggbb" / "#rrggbbaa" を解釈する。</summary>
    public static bool TryParseHex(string s, out uint color)
    {
        color = 0;
        s = s.TrimStart('#');
        static bool Hex(string t, out uint v) => uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out v);
        if (s.Length == 3 && Hex(s, out uint _))
        {
            uint r = Convert.ToUInt32($"{s[0]}{s[0]}", 16), g = Convert.ToUInt32($"{s[1]}{s[1]}", 16), b = Convert.ToUInt32($"{s[2]}{s[2]}", 16);
            color = r | (g << 8) | (b << 16) | 0xFF000000u;
            return true;
        }
        if (s.Length == 6 && Hex(s, out uint v6))
        {
            color = ((v6 >> 16) & 0xFF) | (((v6 >> 8) & 0xFF) << 8) | ((v6 & 0xFF) << 16) | 0xFF000000u;
            return true;
        }
        if (s.Length == 8 && Hex(s, out uint v8))
        {
            color = ((v8 >> 24) & 0xFF) | (((v8 >> 16) & 0xFF) << 8) | (((v8 >> 8) & 0xFF) << 16) | ((v8 & 0xFF) << 24);
            return true;
        }
        return false;
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _h = ctx.Theme.ControlH;
        W = ResolveW(c, ctx, DefaultWidth);
        Size = c.Constrain(new Size(W, _h));
    }
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, DefaultWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        Signal<uint> color = Color.Get();
        Signal<Theme> theme = ctx.Theme;
        float pad = 3, sw = _h - pad * 2;
        var bg = new Scene2D(); bg.FillRoundedRect(Color2D.White, 0, 0, W, _h, theme.Value.Radius + 1);
        node.Content = bg;
        ctx.Effect(() => node.Color = theme.Value.SurfaceAlt);

        // スウォッチ (現在色)
        UiNode swatch = ctx.Canvas.AddChild(node); swatch.Z = 1;
        var ss = new Scene2D(); ss.FillRoundedRect(Color2D.White, pad, pad, sw, sw, MathF.Max(2, theme.Value.Radius - 1));
        swatch.Content = ss;
        ctx.Effect(() => swatch.Color = color.Value | 0xFF000000u);   // 表示は不透明で

        // hex ラベル (色変化で更新)
        UiNode label = ctx.Canvas.AddChild(node); label.Z = 1;
        float fs = theme.Value.FontSm;
        float ascent = ctx.Font.Ascent(fs), fh = ctx.Font.Measure("Mg", fs).height;
        ctx.Effect(() =>
        {
            var s = new Scene2D();
            ctx.Font.AppendText(s, ToHex(color.Value), pad * 2 + sw, (_h - fh) / 2 + ascent, fs, Color2D.White);
            label.Content = s;
        });
        ctx.Effect(() => label.Color = theme.Value.Text);

        // ---- オーバーレイ: パレット + RGB スライダー + hex 入力 ----
        bool sync = false;
        var rS = new Signal<float>((color.Value) & 0xFF);
        var gS = new Signal<float>((color.Value >> 8) & 0xFF);
        var bS = new Signal<float>((color.Value >> 16) & 0xFF);
        var hexS = new Signal<string>(ToHex(color.Value));

        // color → sliders/hex (sync 中の再入は依存登録だけ残して抜ける)
        ctx.Effect(() =>
        {
            uint c = color.Value;
            if (sync) return;
            sync = true;
            try
            {
                rS.Value = c & 0xFF; gS.Value = (c >> 8) & 0xFF; bS.Value = (c >> 16) & 0xFF;
                hexS.Value = ToHex(c);
            }
            finally { sync = false; }
        });
        // sliders → color
        ctx.Effect(() =>
        {
            float r = rS.Value, g = gS.Value, b = bS.Value;
            if (sync) return;
            sync = true;
            try { color.Value = ((uint)Math.Clamp(r, 0, 255)) | ((uint)Math.Clamp(g, 0, 255) << 8) | ((uint)Math.Clamp(b, 0, 255) << 16) | (color.Peek() & 0xFF000000u); }
            finally { sync = false; }
        });
        // hex → color (完全な hex のときだけ)
        ctx.Effect(() =>
        {
            string h = hexS.Value;
            if (sync) return;
            if (!TryParseHex(h, out uint c)) return;
            sync = true;
            try { color.Value = c; }
            finally { sync = false; }
        });

        var paletteRow1 = Palette[..6].Select(SwatchButton).ToArray();
        var paletteRow2 = Palette[6..].Select(SwatchButton).ToArray();
        Widget SwatchButton(uint c) =>
            Button(_ => color.Value = c, "", background: c, width: 18f, height: 18f,
                   rounded: 3f, padding: new Thickness(0));

        var hexField = TextField(hexS, width: 108f);
        hexField.Pattern = "^#?[0-9a-fA-F]{0,8}$";

        Widget menu = Border(background: Bind.From(() => theme.Value.Surface), rounded: theme.Value.Radius, padding: new Thickness(8))
            [VStack(spacing: 5)[
                HStack(4)[paletteRow1],
                HStack(4)[paletteRow2],
                Row("R", rS), Row("G", gS), Row("B", bS),
                hexField]];

        Widget Row(string name, Signal<float> v) =>
            HStack(6)[
                Text(name, theme.Value.FontSm, color: Bind.From(() => theme.Value.TextMuted), width: 12f),
                Slider(v, min: 0, max: 255, width: 140f)];

        FocusTarget f = ctx.AddFocusable(onFocus: on => Focused.Value = on, onKey: ev =>
        {
            if (ev.Key is Key.Enter or Key.Space) { _open.Value = !_open.Value; return true; }
            return false;
        });
        ctx.AddHit(node, new Rect(0, 0, W, _h), onClick: () => _open.Value = !_open.Value, focus: f,
                   onHover: h => Hovered.Value = h);
        ctx.RegisterOverlay(new OverlayEntry
        {
            Open = _open,
            Content = menu,
            Placement = OverlayPlacement.Below,
            Anchor = () => new Rect(WorldPos.X, WorldPos.Y, W, _h),
        });
    }
}
