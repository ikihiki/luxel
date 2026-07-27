using System.Numerics;
using Luxel.Ecs;
using Luxel.Physics;
using Luxel.Physics.Gizmos;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>
/// **メッシュ/凸包コライダー** (タスク 05 / Q16) — 静的な三角形スープ地形 (<see cref="MeshCollider"/>) に、
/// 球 (プリミティブ) と凸包 (<see cref="HullCollider"/> = 四面体) を落として静定させる。物理を固定 dt で回すので
/// 決定的。地形はワイヤグリッド、コライダーは gizmo (球=外接ボックス) と凸包の辺で描く (等角投影の Canvas2D)。
/// </summary>
public static class PhysicsMeshStories
{
    private static readonly Lazy<VectorFont> Font = new(() => Luxel.Gallery.GalleryFonts.Load(Luxel.Gallery.GalleryFonts.Regular));
    private const float W = 460, H = 300;
    private const string TerrainKind = "demo.mesh";

    private static Vector2 Iso(Vector3 w)
    {
        const float s = 34f, cx = 230f, cy = 190f;
        float sx = cx + (w.X - w.Z) * s * 0.87f;
        float sy = cy - w.Y * s + (w.X + w.Z) * s * 0.5f;
        return new Vector2(sx, sy);
    }

    private static float Height(float x, float z) => 0.5f * MathF.Sin(x * 0.9f) * MathF.Cos(z * 0.9f);

    [Story("Examples/3D/PhysicsMesh", Height = 340, Order = 131)]
    public static Widget PhysicsMeshDemo(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(W, H, draw: s =>
    {
        s.FillRect(Color2D.Rgba(20, 24, 30), 0, 0, W, H);

        // 波打つ地形メッシュ (7×7 グリッド)
        const int n = 6;
        const float size = 6f;
        var verts = new Vector3[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float wx = x / (float)n * size - size / 2, wz = z / (float)n * size - size / 2;
                verts[z * (n + 1) + x] = new Vector3(wx, Height(wx, wz), wz);
            }
        var idx = new List<int>();
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int a = z * (n + 1) + x, b = a + 1, c = a + (n + 1), d = c + 1;
                idx.AddRange([a, b, c, b, d, c]);   // Bepu の上面法線 winding
            }
        int[] indices = idx.ToArray();

        using var physics = new Luxel.Physics.PhysicsWorld();
        using var world = new Luxel.Ecs.World();
        var system = new Luxel.Physics.PhysicsStepSystem(world, physics);

        world.CreateEntity(new LocalTransform(Matrix4x4.Identity), MeshCollider.Static(verts, indices));
        var ball = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(-1.2f, 4f, 0.6f)),
            Collider.Sphere(0.45f), RigidBody.Dynamic());
        Vector3[] tetra = [new(0.4f, 0.4f, 0.4f), new(0.4f, -0.4f, -0.4f), new(-0.4f, 0.4f, -0.4f), new(-0.4f, -0.4f, 0.4f)];
        var hull = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(1.3f, 4f, -0.5f)),
            HullCollider.Dynamic(tetra));

        for (int i = 0; i < 150; i++) system.Run(1f / 60f);   // 2.5s で地形に載って静定

        DebugDraw.Reset();
        DebugDraw.Enable(TerrainKind);
        DebugDraw.Enable(PhysicsGizmos.Colliders);

        // 地形ワイヤグリッド (隣接頂点を結ぶ)
        uint terrainColor = Color2D.Rgba(120, 128, 145);
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                Vector3 p = verts[z * (n + 1) + x];
                if (x < n) DebugDraw.Line(p, verts[z * (n + 1) + x + 1], terrainColor, 1f, TerrainKind);
                if (z < n) DebugDraw.Line(p, verts[(z + 1) * (n + 1) + x], terrainColor, 1f, TerrainKind);
            }

        // 球コライダー (gizmo = 外接ボックス)
        PhysicsGizmos.DrawColliders(world,
            dynamicColor: Color2D.Rgba(110, 220, 130), staticColor: Color2D.Rgba(150, 156, 170),
            ccdColor: Color2D.Rgba(240, 96, 96), triggerColor: Color2D.Rgba(90, 200, 240), width: 1.6f);

        // 凸包の辺 (四面体 = 全 6 辺)。頂点は書き戻された LocalTransform で元の頂点原点座標を世界へ。
        Matrix4x4 hm = hull.GetComponent<LocalTransform>().Matrix;
        uint hullColor = Color2D.Rgba(235, 200, 110);
        for (int i = 0; i < tetra.Length; i++)
            for (int j = i + 1; j < tetra.Length; j++)
                DebugDraw.Line(Vector3.Transform(tetra[i], hm), Vector3.Transform(tetra[j], hm), hullColor, 1.6f, TerrainKind);

        Font.Value.AppendText(s, "static mesh terrain  +  convex hull (四面体) + sphere", 16, 20, 15, Color2D.Rgba(210, 214, 222));

        DebugDraw.Flush(s, Iso, (sc, t, x, y, h, c) => Font.Value.AppendText(sc, t, x, y, h, c));
    })));
}
