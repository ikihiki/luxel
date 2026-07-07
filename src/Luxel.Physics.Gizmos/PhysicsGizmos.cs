using System.Numerics;
using Luxel.Ecs;
using Luxel.TwoD;

namespace Luxel.Physics.Gizmos;

/// <summary>
/// 物理の標準 gizmo (<see cref="DebugDraw"/> の上に載せる)。ECS 上の <see cref="Collider"/> +
/// <see cref="RigidBody"/>/<see cref="StaticBody"/> をコライダーワイヤ (箱/球/カプセルは外接ボックス)
/// として最前面オーバーレイに描く。動的/静的/CCD 有効を色分けする。3D はゲームのカメラ (viewProj) を
/// <see cref="DebugDraw.Flush"/> の <see cref="WorldToScreen"/> に渡して投影する (2D は恒等)。
///
/// <para>各ヘルパは先頭でカテゴリの ON/OFF を判定し、<b>OFF 時は ECS 列挙も割り当ても行わず抜ける</b>
/// (購読者ゼロ規律)。</para>
///
/// <para><b>Q14 の範囲</b>: コライダーワイヤ + CCD 色分け。<b>接触点</b>とトリガーボリュームの gizmo は
/// 接触イベント (タスク 04) の実装後 (Q15) にこの流儀で追加する。</para>
/// </summary>
public static class PhysicsGizmos
{
    /// <summary>物理コライダー gizmo の既定カテゴリ。</summary>
    public const string Colliders = "gizmo.physics";

    /// <summary>
    /// World 内の全コライダーをワイヤボックスで描く。動的は <paramref name="dynamicColor"/>、静的は
    /// <paramref name="staticColor"/>、CCD (連続衝突検出) 有効な動的ボディは <paramref name="ccdColor"/>。
    /// 寸法は <see cref="Collider.RenderScale"/> (箱=実寸、球/カプセル=外接)、姿勢は各 entity の
    /// <see cref="LocalTransform"/> の回転 + 平行移動から採る。
    /// </summary>
    public static void DrawColliders(World world, uint dynamicColor, uint staticColor, uint ccdColor,
        float width = 1.5f, string kind = Colliders)
    {
        if (!DebugDraw.IsEnabled(kind)) return;

        world.Query<Collider, RigidBody, LocalTransform>().ForEachEntity(
            (ref Collider c, ref RigidBody b, ref LocalTransform t, Friflo.Engine.ECS.Entity _) =>
                DrawBox(c, t.Matrix, b.Continuous ? ccdColor : dynamicColor, width, kind));

        world.Query<Collider, StaticBody, LocalTransform>().ForEachEntity(
            (ref Collider c, ref StaticBody _, ref LocalTransform t, Friflo.Engine.ECS.Entity _) =>
                DrawBox(c, t.Matrix, staticColor, width, kind));
    }

    /// <summary>コライダーの外接ボックスを 12 辺のワイヤで描く (回転考慮の OBB)。</summary>
    private static void DrawBox(in Collider c, in Matrix4x4 m, uint color, float width, string kind)
    {
        // 姿勢は行列から (回転 + 平行移動)、寸法は Collider から採る
        // — 静的の LocalTransform は作者が任意に組める (スケール成分に依存しない) ため。
        if (!Matrix4x4.Decompose(m, out _, out Quaternion rot, out Vector3 pos))
        {
            rot = Quaternion.Identity;
            pos = m.Translation;
        }
        Vector3 h = c.RenderScale * 0.5f;

        // 8 隅 (符号ビット: bit0=X, bit1=Y, bit2=Z)
        Span<Vector3> corner = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            var local = new Vector3(
                (i & 1) == 0 ? -h.X : h.X,
                (i & 2) == 0 ? -h.Y : h.Y,
                (i & 4) == 0 ? -h.Z : h.Z);
            corner[i] = pos + Vector3.Transform(local, rot);
        }

        // 12 辺 (1 ビットだけ異なる隅の組)
        for (int i = 0; i < 8; i++)
        {
            if ((i & 1) == 0) DebugDraw.Line(corner[i], corner[i | 1], color, width, kind);
            if ((i & 2) == 0) DebugDraw.Line(corner[i], corner[i | 2], color, width, kind);
            if ((i & 4) == 0) DebugDraw.Line(corner[i], corner[i | 4], color, width, kind);
        }
    }
}
