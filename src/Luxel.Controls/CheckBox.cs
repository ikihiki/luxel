using System.Numerics;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// テーマ駆動のチェックボックス。チェック状態は signal 束縛、トグルは色/チェック印の部分更新。
/// <para>スタイルは [UiParam] な <see cref="Bindable{T}"/> フィールド。Tailwind の <c>checked:</c> は
/// <c>On(WidgetState.Checked, Bg(Tw.Blue500))</c> — <see cref="IsStateActive"/> が checked signal を読む。
/// 未設定はテーマ (checked ? Primary : SurfaceAlt) にフォールバック。</para>
/// </summary>
[UiComponent(Name = "Check")]
public sealed partial class CheckBox : Widget
{
    private readonly Signal<bool> _checked;
    private readonly string _label;
    private float _box = 20, _gap = 9, _fs = 15;   // レイアウト時にテーマから確定

    /// <summary>文字サイズ。未設定 → テーマ Font - 1。</summary>
    [UiParam] public readonly Bindable<float> FontSize = new();

    private void CacheMetrics(Theme t)
    {
        _box = t.CheckBox;
        _gap = t.CheckGap;
        _fs = FontSize.Or(t.Font - 1);
    }
    /// <summary>箱の色。未設定 → checked ? Primary : SurfaceAlt。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Background = new();
    /// <summary>ラベル色。未設定 → テーマ Text。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Foreground = new();
    /// <summary>チェック印の色。未設定 → テーマ OnAccent。</summary>
    [UiParam] public readonly Bindable<uint> CheckColor = new();
    [UiParam] public readonly Bindable<float> Opacity = new();

    internal CheckBox(Signal<bool> isChecked, string label)
    {
        _checked = isChecked;
        _label = label;
    }

    public override bool IsStateActive(WidgetState state)
        => state == WidgetState.Checked ? _checked.Value : base.IsStateActive(state);

    public override string? DebugDetail => _label;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        CacheMetrics(ctx.Theme);
        (float tw, float th) = ctx.Font.Measure(_label, _fs);
        Size = c.Constrain(new Size(_box + _gap + tw, MathF.Max(_box, th)));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => _box + _gap + ctx.Font.Measure(_label, FontSize.Or(ctx.Theme.Font - 1)).width;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        Signal<Theme> theme = ctx.Theme;
        float fontSize = _fs;
        float boxY = (Size.Height - _box) / 2f;
        var boxScene = new Scene2D();
        boxScene.FillRoundedRect(Color2D.White, 0, boxY, _box, _box, MathF.Max(2, theme.Peek().Radius - 2));
        node.Content = boxScene;

        UiNode check = ctx.Canvas.AddChild(node);
        var cs = new Scene2D();
        cs.StrokePolyline(Color2D.White, 2.2f,
            new Vector2(0.22f * _box, boxY + 0.52f * _box),
            new Vector2(0.42f * _box, boxY + 0.72f * _box),
            new Vector2(0.78f * _box, boxY + 0.28f * _box));
        check.Content = cs; check.Z = 1;

        UiNode label = ctx.Canvas.AddChild(node);
        (float _, float th) = ctx.Font.Measure(_label, fontSize);
        label.Transform = Affine2D.Translate(_box + _gap, (Size.Height - th) / 2f);
        var ls = new Scene2D();
        ctx.Font.AppendText(ls, _label, 0, ctx.Font.Ascent(fontSize), fontSize, Color2D.White);
        label.Content = ls;

        // 配色: 状態/テーマ変化で部分更新 (WrapSetter 経由で Transition 補間可)。
        uint currentBg = Color2D.White, currentFg = Color2D.White, currentCk = Color2D.White;
        float currentOpacity = 1f;

        void ApplyAll()
        {
            node.Color = StyleApply.MultiplyAlpha(currentBg, currentOpacity);
            label.Color = StyleApply.MultiplyAlpha(currentFg, currentOpacity);
            check.Color = currentCk;
            check.Opacity = _checked.Value ? currentOpacity : 0f;
        }

        Action<uint> bgSet = WrapSetter<uint>("Background", v => { currentBg = v; ApplyAll(); });
        Action<uint> fgSet = WrapSetter<uint>("Foreground", v => { currentFg = v; ApplyAll(); });
        Action<uint> ckSet = WrapSetter<uint>("CheckColor", v => { currentCk = v; ApplyAll(); });
        Action<float> opSet = WrapSetter<float>("Opacity", v => { currentOpacity = v; ApplyAll(); });

        ctx.Effect(() =>
        {
            bgSet(Background.Or(_checked.Value ? theme.Value.Primary : theme.Value.SurfaceAlt));
            fgSet(Foreground.Or(theme.Value.Text));
            ckSet(CheckColor.Or(theme.Value.OnAccent));
            opSet(Opacity.Or(1f));
        });

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            onClick: () => _checked.Value = !_checked.Value,
            onHover: h => Hovered.Value = h);
    }
}
