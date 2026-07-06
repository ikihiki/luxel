using Luxel.TwoD;

namespace Luxel.Particles.TwoD;

/// <summary>
/// <see cref="ParticleSystem"/> を <see cref="RetainedCanvas"/> の 1 ノードへ描く 2D 統合。
/// <see cref="UiNode.ContentColors"/> = true + <see cref="UiNode.ReserveContent"/> で容量を確保しきり、
/// 毎フレーム生存パーティクルのパス列を Content 差し替えで書き込む (Breakout の手法を部品化 — 容量内なら
/// 構造 Rebuild は起きず Segment 再アップロードのみ)。色/サイズは寿命 t で <see cref="ParticleConfig"/> の
/// カーブから評価する。ワールド座標で描くのでスクロール/ズームは <c>Camera2D</c> 側。
/// </summary>
public sealed class ParticleNode
{
    private readonly ParticleSystem _system;
    private readonly int _circleSegments;

    public ParticleNode(RetainedCanvas canvas, UiNode parent, ParticleSystem system, int circleSegments = 12)
    {
        _system = system;
        _circleSegments = circleSegments;
        Node = canvas.AddChild(parent);
        Node.ContentColors = true;
        int segPerParticle = system.Config.Shape == ParticleShape.Circle ? circleSegments : 4;
        Node.ReserveContent(segments: system.Capacity * segPerParticle, paths: system.Capacity);
    }

    /// <summary>描画先ノード (Z/親変換はここ経由)。</summary>
    public UiNode Node { get; }

    /// <summary><see cref="ParticleSystem.Update"/> 後に生存パーティクルを描き直す。</summary>
    public void Sync() => Node.Content = BuildScene(_system, _circleSegments);

    /// <summary>生存パーティクルを <see cref="Scene2D"/> へ焼く (色は per-particle、absoluteColor)。GPU 非依存 — テスト可。</summary>
    public static Scene2D BuildScene(ParticleSystem system, int circleSegments = 12)
    {
        var s = new Scene2D();
        ParticleBuffer b = system.Buffer;
        ParticleConfig cfg = system.Config;
        bool animSize = cfg.Size.IsAnimated;

        for (int i = 0; i < b.Count; i++)
        {
            float t01 = b.Age[i] / MathF.Max(1e-6f, b.LifeMax[i]);
            float size = animSize ? cfg.Size.Eval(t01) : b.Size[i];
            float half = MathF.Max(0f, size) * 0.5f;
            float x = b.PosX[i], y = b.PosY[i];
            uint color = ParticleColor.Multiply(cfg.Color.Eval(t01), b.Tint[i]);

            s.BeginFill(color, absoluteColor: true);
            if (cfg.Shape == ParticleShape.Circle)
            {
                for (int k = 0; k < circleSegments; k++)
                {
                    float a = k / (float)circleSegments * MathF.Tau;
                    float px = x + MathF.Cos(a) * half, py = y + MathF.Sin(a) * half;
                    if (k == 0) s.MoveTo(px, py); else s.LineTo(px, py);
                }
                s.Close();
            }
            else
            {
                s.MoveTo(x - half, y - half).LineTo(x + half, y - half)
                 .LineTo(x + half, y + half).LineTo(x - half, y + half).Close();
            }
            s.End();
        }
        return s;
    }
}
