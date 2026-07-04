using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI.Styling;

using Luxel.UI;
using TAlign = Luxel.Typography.TextAlign;

namespace Luxel.Controls;

/// <summary>
/// テキスト。既定は 1 行互換 (従来どおり)。<see cref="Wrap"/>/<see cref="TextAlign"/> を指定するか
/// 内容に \n を含むと <see cref="TextLayout"/> による複数行レイアウト (折り返し/禁則/整列/Justify) になる。
/// 内容は <see cref="Func{String}"/> で束縛でき、signal 変化で更新される。
/// <para>色/不透明度/サイズは [UiParam] な <see cref="Bindable{T}"/> フィールド。
/// 状態別の上書きは生成された <c>.When(WidgetState.Hover, color: ...)</c> が状態レイヤで積む。</para>
/// </summary>
[UiComponent]
public sealed partial class Text : Widget
{
    /// <summary>テキスト内容。値/Signal/Func/$"..." を受け、内容変化で再ラスタライズされる。</summary>
    [UiParam] public readonly BindableString Content = "";

    [UiParam] public readonly Bindable<float> FontSize = 16f;
    /// <summary>文字色。未設定 → 既定 (濃灰)。テーマ追従は <c>Bind.From(() => UiTheme.T.Text)</c>。</summary>
    [UiParam(Stateable = true)] public readonly Bindable<uint> Color = new();
    [UiParam] public readonly Bindable<float> Opacity = new();

    /// <summary>折り返し (既定 None = 1 行互換)。Word=語境界優先、Char=文字単位 (禁則は両方有効)。</summary>
    [UiParam] public readonly Bindable<TextWrap> Wrap = new();
    /// <summary>行の水平整列 (既定 Left)。Center/Right/Justify はボックス幅 (Width 指定 or 親の利用可能幅) 内。</summary>
    [UiParam] public readonly Bindable<TAlign> TextAlign = new();
    /// <summary>行送り倍率 (既定 1.2)。複数行時のみ使用。</summary>
    [UiParam] public readonly Bindable<float> LineHeight = new();
    /// <summary>最大行数 (0 = 無制限)。超過分は切り捨てて末尾に … を付ける。</summary>
    [UiParam] public readonly Bindable<int> MaxLines = new();
    /// <summary>ボックス内の縦整列 (Height 指定時のみ効く)。</summary>
    [UiParam] public readonly Bindable<TextVAlign> VerticalAlign = new();

    // 複数行レイアウト (PerformLayout で構築、Realize が描画に使う。null = 1 行互換パス)
    private TextLayout? _layout;
    private TextLayoutOptions? _layoutOpts;
    private string? _layoutText;

    private static readonly uint DefaultColor = Color2D.Rgba(25, 25, 30);

    internal Text() { }
    internal Text(Func<string> getter) => Content = getter;
    internal Text(string value) => Content = value;
    [UiCtor] internal Text(BindableString content) => Content = content;

    /// <summary>色をリアクティブに束縛する (テーマ追従など)。signal/テーマ変化で recolor 部分更新。</summary>
    public Text Bind(Func<uint> color) { Color.SetBase(Luxel.UI.Bind.From(color)); return this; }

    public override string? DebugDetail => Content.Get();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        string content = Content.Get();
        TextWrap wrap = Wrap.Or(TextWrap.None);
        TAlign align = TextAlign.Or(TAlign.Left);
        int maxLines = MaxLines.Or(0);
        TextVAlign valign = VerticalAlign.Or(TextVAlign.Top);
        if (wrap == TextWrap.None && align == TAlign.Left && maxLines == 0 && valign == TextVAlign.Top
            && !content.Contains('\n'))
        {
            // 1 行互換パス (従来とピクセル同一)
            _layout = null;
            (float w, float h) = ctx.Font.Measure(content, FontSize.Get());
            Size = c.Constrain(new Size(ResolveW(c, ctx, w), ResolveH(c, ctx, h)));
            return;
        }

        // 複数行: ボックス幅 = 明示 Width > 親の利用可能幅 > ∞、高さ = 明示 Height (縦整列の基準)
        Length lw = Width.Or(default);
        Length lh = Height.Or(default);
        float maxW = lw.IsSet ? lw.Resolve(c.MaxW, ctx, float.PositiveInfinity)
                   : float.IsInfinity(c.MaxW) ? float.PositiveInfinity : c.MaxW;
        _layoutOpts = new TextLayoutOptions
        {
            MaxWidth = maxW, Wrap = wrap, Align = align, LineHeight = LineHeight.Or(1.2f),
            MaxLines = maxLines, VAlign = valign,
            MaxHeight = lh.IsSet ? lh.Resolve(c.MaxH, ctx, float.PositiveInfinity) : float.PositiveInfinity,
        };
        _layoutText = content;
        _layout = TextLayout.Get(ctx.Font, content, FontSize.Get(), _layoutOpts);
        Size = c.Constrain(new Size(ResolveW(c, ctx, _layout.Width), ResolveH(c, ctx, _layout.Height)));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
    {
        // 折り返し前の自然幅 (段落ごとの最長行)
        string content = Content.Get();
        if (!content.Contains('\n')) return ctx.Font.Measure(content, FontSize.Get()).width;
        float w = 0;
        foreach (string para in content.Split('\n'))
            w = MathF.Max(w, ctx.Font.Measure(para, FontSize.Get()).width);
        return w;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        float ascent = ctx.Font.Ascent(FontSize.Get());

        // 内容束縛: signal 変化で再ラスタライズ → フル再構築 (v1)。
        // 複数行は同じボックス設定で再レイアウト (サイズ変化のリアクティブ再レイアウトは将来)。
        ctx.Effect(() =>
        {
            string content = Content.Get();
            var s = new Scene2D();
            if (_layout is not null && _layoutOpts is not null)
            {
                if (content != _layoutText)
                {
                    _layoutText = content;
                    _layout = TextLayout.Get(ctx.Font, content, FontSize.Get(), _layoutOpts);
                }
                _layout.Draw(s, 0, 0, Color2D.White);
            }
            else
            {
                ctx.Font.AppendText(s, content, 0, ascent, FontSize.Get(), Color2D.White);
            }
            node.Content = s;
        });

        // 配色: 状態/テーマ変化で recolor 部分更新 (WrapSetter 経由で Transition 補間可)。
        uint currentFg = DefaultColor;
        float currentOpacity = 1f;
        void ApplyAll() => node.Color = StyleApply.MultiplyAlpha(currentFg, currentOpacity);

        Action<uint> fgSet = WrapSetter<uint>("Color", v => { currentFg = v; ApplyAll(); });
        Action<float> opSet = WrapSetter<float>("Opacity", v => { currentOpacity = v; ApplyAll(); });

        ctx.Effect(() =>
        {
            fgSet(Color.Or(DefaultColor));
            opSet(Opacity.Or(1f));
        });
    }
}
