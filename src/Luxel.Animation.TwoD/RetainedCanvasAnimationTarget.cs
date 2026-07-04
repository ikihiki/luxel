using System.Numerics;
using Luxel.Animation;
using Luxel.TwoD;

namespace Luxel.Animation.TwoD;

/// <summary>
/// <see cref="Luxel.TwoD.RetainedCanvas"/> の <see cref="UiNode"/> への <see cref="IAnimationTarget"/> アダプタ。
/// 文字列 path 形式: "{nodeName}/{property}"。サポートする property:
/// <list type="bullet">
///   <item><c>transform</c> (Affine2D)</item>
///   <item><c>translation</c> (Vector2) / <c>translationX</c>/<c>translationY</c> (float)</item>
///   <item><c>scale</c> (Vector2) / <c>scaleX</c>/<c>scaleY</c> (float)</item>
///   <item><c>rotation</c> (float radians; 既存の translation/scale を保ったまま回転を再合成)</item>
///   <item><c>color</c> (uint RGBA)</item>
///   <item><c>opacity</c> (float)</item>
/// </list>
/// 各 setter は <see cref="UiNode"/> の既存 dirty 伝播 (transform slot / style slot のみ書込み) を起動するため、
/// segment データは触らず**部分更新**で済む。
/// </summary>
public sealed class RetainedCanvasAnimationTarget : IAnimationTarget
{
    private readonly Dictionary<string, UiNode> _nodes = new();
    private readonly Dictionary<string, Action<UiNode, object>> _customHandlers = new();

    /// <summary>name → UiNode のバインドを登録。</summary>
    public RetainedCanvasAnimationTarget Bind(string name, UiNode node)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
        ArgumentNullException.ThrowIfNull(node);
        _nodes[name] = node;
        return this;
    }

    /// <summary>標準 property 以外を扱う拡張点。</summary>
    public RetainedCanvasAnimationTarget RegisterPropertyHandler(string property, Action<UiNode, object> handler)
    {
        _customHandlers[property] = handler;
        return this;
    }

    public void Apply(string path, object value)
    {
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1) return;
        string nodeName = path[..slash];
        string property = path[(slash + 1)..];

        if (!_nodes.TryGetValue(nodeName, out var node)) return;

        switch (property)
        {
            case "transform":
                node.Transform = (Affine2D)value;
                break;
            case "translation":
                {
                    var t = node.Transform;
                    var v = (Vector2)value;
                    node.Transform = new Affine2D { A = t.A, B = t.B, C = t.C, D = t.D, E = v.X, F = v.Y };
                }
                break;
            case "translationX":
                {
                    var t = node.Transform;
                    node.Transform = new Affine2D { A = t.A, B = t.B, C = t.C, D = t.D, E = (float)value, F = t.F };
                }
                break;
            case "translationY":
                {
                    var t = node.Transform;
                    node.Transform = new Affine2D { A = t.A, B = t.B, C = t.C, D = t.D, E = t.E, F = (float)value };
                }
                break;
            case "scale":
                {
                    var v = (Vector2)value;
                    var t = node.Transform;
                    // 現在の scale を上書き、translation は維持。rotation 成分は scale で潰れるため簡素化
                    node.Transform = new Affine2D { A = v.X, B = 0, C = 0, D = v.Y, E = t.E, F = t.F };
                }
                break;
            case "scaleX":
                {
                    var t = node.Transform;
                    node.Transform = new Affine2D { A = (float)value, B = t.B, C = t.C, D = t.D, E = t.E, F = t.F };
                }
                break;
            case "scaleY":
                {
                    var t = node.Transform;
                    node.Transform = new Affine2D { A = t.A, B = t.B, C = t.C, D = (float)value, E = t.E, F = t.F };
                }
                break;
            case "rotation":
                {
                    // 現在の translation を保ったまま、scale を 1 にして回転を上書き
                    var t = node.Transform;
                    float r = (float)value;
                    float ca = MathF.Cos(r), sa = MathF.Sin(r);
                    node.Transform = new Affine2D { A = ca, B = sa, C = -sa, D = ca, E = t.E, F = t.F };
                }
                break;
            case "color":
                node.Color = (uint)value;
                break;
            case "opacity":
                node.Opacity = (float)value;
                break;
            default:
                if (_customHandlers.TryGetValue(property, out var h)) h(node, value);
                break;
        }
    }
}
