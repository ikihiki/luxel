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
/// <para>コライダーワイヤ + CCD 色分け + トリガー色分け (<see cref="DrawColliders"/>) と、接触中ペアの
/// マーカ (<see cref="ContactMarkers"/>) を描く。接触マーカは v1 が接触点の詳細位置を公開しない (タスク 04 スコープ)
/// ため、2 entity の中心間midpoint に十字マーカを置く「接触インジケータ」として描く。</para>
/// </summary>
public static class PhysicsGizmos
{
    /// <summary>物理コライダー gizmo の既定カテゴリ。</summary>
    public const string Colliders = "gizmo.physics";
    /// <summary>接触インジケータ gizmo の既定カテゴリ。</summary>
    public const string Contacts = "gizmo.contacts";

    /// <summary>
    /// World 内の全コライダーをワイヤボックスで描く。動的は <paramref name="dynamicColor"/>、静的は
    /// <paramref name="staticColor"/>、CCD (連続衝突検出) 有効な動的ボディは <paramref name="ccdColor"/>、
    /// トリガーは <paramref name="triggerColor"/>。寸法は <see cref="Collider.RenderScale"/>
    /// (箱=実寸、球/カプセル=外接)、姿勢は各 entity の <see cref="LocalTransform"/> の回転 + 平行移動から採る。
    /// </summary>
    public static void DrawColliders(World world, uint dynamicColor, uint staticColor, uint ccdColor, uint triggerColor,
        float width = 1.5f, string kind = Colliders)
    {
        if (!DebugDraw.IsEnabled(kind)) return;

        world.Query<Collider, RigidBody, LocalTransform>().ForEachEntity(
            (ref Collider c, ref RigidBody b, ref LocalTransform t, Friflo.Engine.ECS.Entity _) =>
                DrawBox(c, t.Matrix, b.Continuous ? ccdColor : dynamicColor, width, kind));

        world.Query<Collider, StaticBody, LocalTransform>().ForEachEntity(
            (ref Collider c, ref StaticBody _, ref LocalTransform t, Friflo.Engine.ECS.Entity _) =>
                DrawBox(c, t.Matrix, staticColor, width, kind));

        world.Query<Collider, Trigger, LocalTransform>().ForEachEntity(
            (ref Collider c, ref Trigger _, ref LocalTransform t, Friflo.Engine.ECS.Entity _) =>
                DrawBox(c, t.Matrix, triggerColor, width, kind));
    }

    /// <summary>
    /// 接触中の Entity ペア (<see cref="PhysicsStepSystem.CurrentContacts"/>) の中心間midpoint に十字マーカを描く。
    /// v1 は接触点の詳細位置を公開しない (04 スコープ) ため、これは「どの 2 体が触れているか」を示すインジケータ。
    /// <see cref="PhysicsStepSystem.TrackCurrentContacts"/> を true にして収集させておくこと。
    /// </summary>
    public static void ContactMarkers(IReadOnlyList<EntityPair> currentContacts, uint color,
        float size = 0.15f, float width = 1.5f, string kind = Contacts)
    {
        if (!DebugDraw.IsEnabled(kind)) return;
        foreach (EntityPair p in currentContacts)
        {
            Vector3 mid = (Center(p.A) + Center(p.B)) * 0.5f;
            DebugDraw.Line(mid - new Vector3(size, 0, 0), mid + new Vector3(size, 0, 0), color, width, kind);
            DebugDraw.Line(mid - new Vector3(0, size, 0), mid + new Vector3(0, size, 0), color, width, kind);
            DebugDraw.Line(mid - new Vector3(0, 0, size), mid + new Vector3(0, 0, size), color, width, kind);
        }
    }

    private static Vector3 Center(Friflo.Engine.ECS.Entity e)
        => e.HasComponent<LocalTransform>() ? e.GetComponent<LocalTransform>().Matrix.Translation : default;

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
