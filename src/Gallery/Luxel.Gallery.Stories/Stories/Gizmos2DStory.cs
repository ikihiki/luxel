using System.Numerics;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>
/// **2D ゲーム gizmo** (タスク 21 ステージ②) — DebugDraw コアの上に載る機能別 gizmo を最前面オーバーレイで描く。
/// タイルマップの**衝突タイル**ワイヤ (<see cref="Gizmos2D.TileCollision"/>)、<see cref="CameraRig2D"/> の
/// **デッドゾーン矩形 + ワールド境界** (<see cref="Gizmos2D.CameraRig"/>)、**パーティクルエミッタ**位置 + 生存数
/// (<see cref="ParticleGizmos.Emitter"/>)。ゲーム画面 (塗り) を下地に、gizmo を on にした 1 枚を golden 化して
/// レイヤの回帰を守る (Canvas2D = Skia 可・決定的、worldToScreen は 2D 恒等)。物理 gizmo はステージ③ (Q14)。
/// </summary>
public static class Gizmos2DStories
{
    private static readonly Lazy<VectorFont> Font = new(() => Luxel.Gallery.GalleryFonts.Load(Luxel.Gallery.GalleryFonts.Regular));
    private const float W = 460, H = 280, Tile = 16;

    private static TileMap BuildMap()
    {
        var atlas = new SpriteAtlas("proc", [new("wall", new SpriteRect(0, 0, 16, 16))]);
        var ts = new TileSet(atlas, (int)Tile, (int)Tile, [new(1, new TileDef("wall", Solid: true))]);
        var map = new TileMap(28, 17, ts);
        for (int x = 0; x < 28; x++) { map.SetTile(x, 15, 1); map.SetTile(x, 16, 1); }   // 地面 2 段
        for (int y = 11; y <= 14; y++) map.SetTile(12, y, 1);                              // 壁柱
        return map;
    }

    [Story("Examples/2D/Gizmos2D", Height = 320, Order = 151)]
    public static Widget Gizmos2DDemo(StoryContext ctx) => ctx.Snap(Frame(Canvas2D(W, H, draw: s =>
    {
        TileMap map = BuildMap();

        // --- 下地: ゲーム画面 (衝突タイルを塗り + プレイヤー箱) ---
        s.FillRect(Color2D.Rgba(22, 26, 34), 0, 0, W, H);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.TileSet.IsSolid(map.Get(x, y)))
                    s.FillRect(Color2D.Rgba(72, 78, 96), x * Tile, y * Tile, Tile, Tile);
        s.FillRoundedRect(Color2D.Rgba(90, 170, 245), 176, 210, 16, 30, 3);   // プレイヤー (地面 y=240 の上)

        // --- gizmo (決定的に毎フレーム作り直す) ---
        DebugDraw.Reset();
        DebugDraw.Enable(Gizmos2D.Tiles);
        DebugDraw.Enable(Gizmos2D.Camera);
        DebugDraw.Enable(ParticleGizmos.Emitters);

        Gizmos2D.TileCollision(map, new RectF(0, 0, W, H), Color2D.Rgba(240, 120, 90), 1.5f);   // 衝突タイル (橙)

        var rig = new CameraRig2D { Deadzone = new Vector2(150, 100), WorldBounds = new RectF(0, 0, 28 * Tile, 17 * Tile) };
        rig.Target = new Vector2(230, 140);
        rig.SnapToTarget();
        Gizmos2D.CameraRig(rig, Color2D.Rgba(90, 220, 120), Color2D.Rgba(90, 200, 240), 1.5f);   // デッドゾーン緑 / 境界シアン

        var ps = new ParticleSystem(
            new ParticleConfig(Life: 5f, Speed: 0f, SpreadRadians: 0, BaseAngle: 0, Gravity: 0, Drag: 0,
                Size: 1f, Color: ParticleColor.Const(0xFFFFFFFF)), capacity: 64, seed: 1);
        ps.Emit(new Vector3(392, 70, 0), 24);
        ParticleGizmos.Emitter(ps, new Vector2(392, 70), Color2D.Rgba(235, 120, 235), 7f);   // エミッタ (マゼンタ) + alive

        DebugDraw.Flush(s, w => new Vector2(w.X, w.Y),
            (sc, t, x, y, h, c) => Font.Value.AppendText(sc, t, x, y, h, c));
    })));
}
