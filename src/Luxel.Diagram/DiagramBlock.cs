using System.Numerics;
using Luxel.Document;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Diagram;

/// <summary>
/// mermaid (サブセット) ダイアグラムの描画 widget。```mermaid フェンスの本文を
/// <see cref="MermaidParser"/> → <see cref="DiagramLayout"/> で配置し、Scene2D で描く
/// (箱 = Surface 地 + Border 枠、矢印 = TextMuted、ラベル = Text — テーマ切替は recolor のみ)。
/// 使える幅に収まらないときは全体を等比縮小する。
/// </summary>
[UiComponent]
public sealed partial class DiagramBlock : Widget
{
    private const float FontPx = 13;

    /// <summary>mermaid フェンス本文。</summary>
    [UiParam] private readonly Bindable<string> _source = "";
    /// <summary>使える最大幅 (0 = 制約幅)。超えると全体を等比縮小。</summary>
    [UiParam] private readonly Bindable<float> _maxWidth = new();

    private DiagramLayoutResult? _layout;
    private float _scale = 1f;

    /// <summary>デバッグ表示の補足 (配置済みノード数)。</summary>
    public override string? DebugDetail => $"{_layout?.Nodes.Count ?? 0} nodes";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _layout ??= DiagramLayout.Arrange(MermaidParser.Parse(Source.Get()),
            label => ctx.Font.Measure(label, FontPx));
        float maxW = MaxWidth.Get();
        float availW = maxW > 0 ? maxW : c.MaxW;
        _scale = !float.IsInfinity(availW) && availW > 0 && _layout.Width > availW
            ? availW / _layout.Width : 1f;
        Size = c.Constrain(new Size(_layout.Width * _scale, _layout.Height * _scale));
    }

    /// <summary>縮小前の自然幅 (レイアウト未実行なら 0)。</summary>
    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => _layout?.Width ?? 0;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode root = CreateRoot(ctx, parent, worldOrigin);
        if (_layout is not { Nodes.Count: > 0 } lay) return;
        float s = _scale;

        // 色グループ毎に 1 ノード (recolor でテーマ追従)。Z: 地 < 線 < 文字
        var fill = new Scene2D();     // 箱の地 (Surface)
        var stroke = new Scene2D();   // 箱の枠 (BorderColor)
        var lines = new Scene2D();    // エッジ + 矢頭 (TextMuted)
        var text = new Scene2D();     // ラベル (Text)
        var edgeText = new Scene2D(); // エッジラベル (TextMuted — lines と同色ノードに同居)

        foreach (DiagramNodeLayout n in lay.Nodes)
        {
            float x = n.X * s, y = n.Y * s, w = n.W * s, h = n.H * s;
            switch (n.Node.Shape)
            {
                case DiagramShape.Diamond:
                {
                    float cx = x + w / 2, cy = y + h / 2;
                    fill.BeginFill(Color2D.White).MoveTo(cx, y).LineTo(x + w, cy).LineTo(cx, y + h).LineTo(x, cy).Close().End();
                    stroke.StrokePolyline(Color2D.White, 1.4f,
                        new Vector2(cx, y), new Vector2(x + w, cy), new Vector2(cx, y + h), new Vector2(x, cy), new Vector2(cx, y));
                    break;
                }
                case DiagramShape.Rounded:
                    fill.FillRoundedRect(Color2D.White, x, y, w, h, h / 2);
                    stroke.StrokeRoundedRect(Color2D.White, 1.4f, x, y, w, h, h / 2);
                    break;
                default:
                    fill.FillRoundedRect(Color2D.White, x, y, w, h, 5);
                    stroke.StrokeRoundedRect(Color2D.White, 1.4f, x, y, w, h, 5);
                    break;
            }
            (float tw, float th) = ctx.Font.Measure(n.Node.Label, FontPx * s);
            ctx.Font.AppendText(text, n.Node.Label,
                x + (w - tw) / 2, y + (h - th) / 2 + ctx.Font.Ascent(FontPx * s), FontPx * s, Color2D.White);
        }

        foreach (DiagramEdgeLayout e in lay.Edges)
        {
            Vector2 p = e.From * s, q = e.To * s;
            lines.StrokeLine(Color2D.White, 1.4f, p.X, p.Y, q.X, q.Y);
            // 矢頭 (終端に長さ 7 の三角)
            Vector2 d = q - p;
            if (d.LengthSquared() > 0.01f)
            {
                d = Vector2.Normalize(d);
                var n = new Vector2(-d.Y, d.X);
                Vector2 b1 = q - d * 8 + n * 3.5f, b2 = q - d * 8 - n * 3.5f;
                lines.BeginFill(Color2D.White).MoveTo(q.X, q.Y).LineTo(b1.X, b1.Y).LineTo(b2.X, b2.Y).Close().End();
            }
            if (e.Edge.Label is { Length: > 0 } el)
            {
                (float tw, float th) = ctx.Font.Measure(el, FontPx * s * 0.9f);
                ctx.Font.AppendText(edgeText, el,
                    e.LabelCenter.X * s - tw / 2, e.LabelCenter.Y * s - th / 2 + ctx.Font.Ascent(FontPx * s * 0.9f) - 2,
                    FontPx * s * 0.9f, Color2D.White);
            }
        }

        UiNode Add(Scene2D scene, int z)
        {
            UiNode n = ctx.Canvas.AddChild(root);
            n.Z = z;
            n.Content = scene;
            return n;
        }
        UiNode fillN = Add(fill, 0);
        UiNode lineN = Add(lines, 1);
        UiNode strokeN = Add(stroke, 2);
        UiNode textN = Add(text, 3);
        UiNode edgeTextN = Add(edgeText, 3);
        ctx.Effect(() =>
        {
            Theme t = ctx.Theme.Value;
            fillN.Color = t.Surface;
            strokeN.Color = t.BorderColor;
            lineN.Color = t.TextMuted;
            textN.Color = t.Text;
            edgeTextN.Color = t.TextMuted;
        });
    }
}

/// <summary>```mermaid フェンス → embed 昇格 (Kit.Docs の fences 引数へ渡す)。widget 側の登録は
/// アプリが行う: <c>doc.Widgets.Register("mermaid", bc => Factories.DiagramBlock(((FencePayload)bc.Payload).Body, bc.MaxWidth))</c>
/// (BlockWidgetRegistry は Controls 層 — このアセンブリは Controls 非依存に保つ)。</summary>
public sealed class MermaidFenceResolver : IFenceResolver
{
    /// <summary>共有インスタンス (状態を持たない)。</summary>
    public static readonly MermaidFenceResolver Instance = new();

    /// <summary>info 先頭語が <c>mermaid</c> のフェンスだけ <see cref="FencePayload"/> に昇格する (他は null)。</summary>
    public IBlockPayload? Resolve(string info, string body)
        => info.Split(' ', 2)[0] is "mermaid" ? new FencePayload(info, body) : null;
}
