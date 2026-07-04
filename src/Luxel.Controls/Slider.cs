using Luxel.TwoD;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Controls;

/// <summary>
/// スライダー。値=signal。クリック/ドラッグで設定、←→ で微調整。フィル/つまみは transform 部分更新。
/// <para>trackColor/fillColor/knobColor は [UiParam] Bindable、状態別は <c>On(WidgetState.Focused, ...)</c>、
/// <c>SliderSlot.Knob(() => widget)</c> で内部 primitives を slot 上書き可。</para>
/// </summary>
[UiComponent]
public sealed partial class Slider : Widget, ISlotted<SliderSlotKey>
{
    private readonly Signal<float> _value;
    private readonly float _min, _max;
    private const float DefaultWidth = 220f, H = 26, TrackH = 6, Knob = 18;
    private float EffectiveWidth = DefaultWidth;   // PerformLayout で解決 (% / em / vw 対応)

    // === スタイル ([UiParam] Bindable。focused 等の状態別は On(WidgetState.Focused, ...) の状態レイヤ) ===
    /// <summary>トラック色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam] public readonly Bindable<uint> TrackColor = new();
    /// <summary>フィル色。未設定 → テーマ Primary。</summary>
    [UiParam] public readonly Bindable<uint> FillColor = new();
    /// <summary>つまみ色。未設定 → テーマ Primary。</summary>
    [UiParam] public readonly Bindable<uint> KnobColor = new();

    // === Slot ===
    private readonly Dictionary<SliderSlotKey, Func<Widget>> _slots = new();
    public void SetSlot(SliderSlotKey key, Func<Widget> template) => _slots[key] = template;

    public Slider this[params ISlotPart[] slots]
    {
        get { foreach (var s in slots) s.ApplyTo(this); return this; }
    }

    internal Slider(Signal<float> value, float min = 0, float max = 1) { _value = value; _min = min; _max = max; }

    private float Frac => Math.Clamp((_value.Value - _min) / (_max - _min), 0, 1);
    private void SetFromX(float localX)
    {
        float f = Math.Clamp((localX - Knob / 2) / (EffectiveWidth - Knob), 0, 1);
        _value.Value = _min + f * (_max - _min);
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        EffectiveWidth = ResolveW(c, ctx, DefaultWidth);
        Size = c.Constrain(new Size(EffectiveWidth, H));
    }
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, DefaultWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;

        float ty = (H - TrackH) / 2;

        // track (slot 上書き可)
        UiNode track = ctx.Canvas.AddChild(node);
        if (_slots.TryGetValue(SliderSlotKey.Track, out var trackTmpl))
        {
            var customTrack = trackTmpl();
            customTrack.Layout(Constraints.Tight(new Size(EffectiveWidth, TrackH)), new LayoutContext { Font = ctx.Font });
            customTrack.Offset = new Point(0, ty);
            customTrack.Realize(ctx, node, world);
        }
        else
        {
            var trs = new Scene2D(); trs.FillRoundedRect(Color2D.White, 0, ty, EffectiveWidth, TrackH, TrackH / 2);
            track.Content = trs;
            ctx.Effect(() => track.Color = TrackColor.Or(ctx.Theme.Value.SurfaceAlt));
        }

        // fill
        UiNode fill = ctx.Canvas.AddChild(node); fill.Z = 1;
        var fs = new Scene2D(); fs.FillRoundedRect(Color2D.White, 0, ty, EffectiveWidth, TrackH, TrackH / 2); fill.Content = fs;
        ctx.Effect(() => fill.Color = FillColor.Or(ctx.Theme.Value.Primary));
        ctx.Effect(() => fill.Transform = Affine2D.Scale(Frac, 1));

        // knob (slot 上書き可)
        UiNode knob = ctx.Canvas.AddChild(node); knob.Z = 2;
        if (_slots.TryGetValue(SliderSlotKey.Knob, out var knobTmpl))
        {
            var customKnob = knobTmpl();
            customKnob.Layout(Constraints.Tight(new Size(Knob, H)), new LayoutContext { Font = ctx.Font });
            customKnob.Offset = new Point(0, 0);
            customKnob.Realize(ctx, knob, world);
        }
        else
        {
            var ks = new Scene2D(); ks.FillCircle(Color2D.White, Knob / 2, H / 2, Knob / 2); knob.Content = ks;
            ctx.Effect(() => knob.Color = KnobColor.Or(ctx.Theme.Value.Primary));
        }
        ctx.Effect(() => knob.Transform = Affine2D.Translate(Frac * (EffectiveWidth - Knob), 0));

        FocusRing.Add(ctx, node, -3, -3, EffectiveWidth + 6, H + 6, (H + 6) / 2, Focused);

        FocusTarget f = ctx.AddFocusable(onFocus: on => Focused.Value = on, onKey: ev =>
        {
            float step = (_max - _min) / 20f;
            if (ev.Key == Key.Left) { _value.Value = Math.Clamp(_value.Value - step, _min, _max); return true; }
            if (ev.Key == Key.Right) { _value.Value = Math.Clamp(_value.Value + step, _min, _max); return true; }
            return false;
        });
        ctx.AddHit(node, new Rect(0, 0, EffectiveWidth, H), focus: f,
            onClickPos: (lx, _) => SetFromX(lx),                 // 直接 Click 用 (テスト/DevTools)
            onDragStart: (lx, _) => SetFromX(lx),                // 押下で即設定
            onDrag: (lx, _) => SetFromX(lx));                    // ドラッグで追従 (矩形外も可)
    }
}
