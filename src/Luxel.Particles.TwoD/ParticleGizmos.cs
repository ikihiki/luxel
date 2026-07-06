using System.Numerics;
using Luxel.TwoD;

namespace Luxel.Particles.TwoD;

/// <summary>
/// パーティクルの標準 gizmo (<see cref="DebugDraw"/> の上に載せる)。エミッタ位置に十字マーカ + 円を描き、
/// 生存数 (<see cref="ParticleSystem.Alive"/>) をラベル表示する。パーティクルは ECS エンティティではない
/// (SoA 内部) ので ECS タブに出ない — この gizmo と DevStats が観測の口。OFF 時は割り当てゼロで抜ける。
/// </summary>
public static class ParticleGizmos
{
    /// <summary>エミッタ gizmo の既定カテゴリ。</summary>
    public const string Emitters = "gizmo.particles";

    /// <summary><paramref name="pos"/> にエミッタマーカ (十字 + 円) と <c>alive N</c> ラベルを描く。</summary>
    public static void Emitter(ParticleSystem ps, Vector2 pos, uint color, float radius = 6f, string kind = Emitters)
    {
        if (!DebugDraw.IsEnabled(kind)) return;
        DebugDraw.Circle(pos.X, pos.Y, radius, color, 1.5f, 16, kind);
        DebugDraw.Line(new Vector2(pos.X - radius, pos.Y), new Vector2(pos.X + radius, pos.Y), color, 1f, kind);
        DebugDraw.Line(new Vector2(pos.X, pos.Y - radius), new Vector2(pos.X, pos.Y + radius), color, 1f, kind);
        DebugDraw.Text(pos.X + radius + 3, pos.Y - radius, $"alive {ps.Alive}", color, 12f, kind);
    }
}
