using System.Numerics;
using Luxel.Ecs;
using Luxel.Physics;
using Luxel.Physics.Gizmos;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **物理 gizmo** (タスク 21 ステージ③ = Q14) — DebugDraw の上に載る <see cref="PhysicsGizmos"/> が
/// ECS 上の <see cref="Collider"/> をワイヤボックスで描く (箱=実寸、球/カプセルは外接ボックス)。
/// <b>動的 (緑) / 静的 (灰) / CCD 有効 (赤)</b> を色分け。3D → 2D はゲームのカメラを
/// <see cref="WorldToScreen"/> に渡して投影する — ここでは決定的な等角投影で 1 枚を golden 化し、描画層の
/// 回帰を守る (Canvas2D = Skia 可・決定的。Bepu は回さず authored pose で純粋に gizmo だけを試す)。
/// 接触点/トリガーの gizmo は接触イベント (タスク 04 / Q15) 実装後にこの流儀で追加。
/// </summary>
public static class PhysicsGizmosStories
{
    private static readonly Lazy<VectorFont> Font = new(() => Luxel.Gallery.GalleryFonts.Load(Luxel.Gallery.GalleryFonts.Regular));
    private const float W = 460, H = 300;

    /// <summary>等角投影 (worldToScreen): X-Z 平面を斜めに、Y を画面上方向へ。</summary>
    private static Vector2 Iso(Vector3 w)
    {
        const float s = 30f, cx = 230f, cy = 208f;
        float sx = cx + (w.X - w.Z) * s * 0.87f;
        float sy = cy - w.Y * s + (w.X + w.Z) * s * 0.5f;
        return new Vector2(sx, sy);
    }

    [Story("Demos/3D/PhysicsGizmos", Height = 340, Order = 129)]
    public static Widget PhysicsGizmosDemo(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(W, H, draw: s =>
    {
        s.FillRect(Color2D.Rgba(20, 24, 30), 0, 0, W, H);

        using var world = new Luxel.Ecs.World();
        // 床 (静的)
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            Collider.Box(6, 1, 6), new StaticBody());
        // 動的な箱 (通常)
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(-1.4f, 0.6f, 0.8f)),
            Collider.Box(1.2f, 1.2f, 1.2f), RigidBody.Dynamic());
        // 動的な箱 (回転) — OBB ワイヤが姿勢に追従することを見せる
        world.CreateEntity(new LocalTransform(
                Matrix4x4.CreateFromYawPitchRoll(0.6f, 0.3f, 0.2f) * Matrix4x4.CreateTranslation(0.2f, 1.5f, -0.6f)),
            Collider.Box(1f, 1f, 1f), RigidBody.Dynamic());
        // CCD 有効の弾 (球 → 外接ボックス、赤で色分け)
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(1.7f, 0.5f, 1.2f)),
            Collider.Sphere(0.5f), RigidBody.Dynamic(ccd: true));

        DebugDraw.Reset();
        DebugDraw.Enable(PhysicsGizmos.Colliders);
        PhysicsGizmos.DrawColliders(world,
            dynamicColor: Color2D.Rgba(110, 220, 130),   // 動的 = 緑
            staticColor: Color2D.Rgba(150, 156, 170),    // 静的 = 灰
            ccdColor: Color2D.Rgba(240, 96, 96),         // CCD = 赤
            width: 1.6f);

        Font.Value.AppendText(s, "physics colliders  —  dynamic / static / CCD", 16, 20, 15, Color2D.Rgba(210, 214, 222));

        DebugDraw.Flush(s, Iso, (sc, t, x, y, h, c) => Font.Value.AppendText(sc, t, x, y, h, c));
    })));
}
