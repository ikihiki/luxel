using System.Numerics;

namespace Luxel.Graphics.TwoD;

/// <summary>
/// 2D ゲーム機能の標準 gizmo (<see cref="DebugDraw"/> の上に載せる)。タイルマップの衝突タイル/Sweep 結果、
/// <see cref="CameraRig2D"/> のデッドゾーン矩形とワールド境界を最前面オーバーレイに描く。
/// 各ヘルパは先頭でカテゴリの ON/OFF を判定し、**OFF 時は列挙/割り当てゼロで抜ける** (購読者ゼロ規律)。
/// 物理 gizmo は別途 (タスク 21 ステージ③)。
/// </summary>
public static class Gizmos2D
{
    /// <summary>タイル (衝突/Sweep) の既定カテゴリ。</summary>
    public const string Tiles = "gizmo.tiles";
    /// <summary>カメラ (デッドゾーン/境界) の既定カテゴリ。</summary>
    public const string Camera = "gizmo.camera";

    /// <summary>表示矩形 <paramref name="worldView"/> と重なる**衝突タイル**のワイヤ矩形を描く。</summary>
    public static void TileCollision(TileMap map, RectF worldView, uint color, float width = 1.5f, string kind = Tiles)
    {
        if (!DebugDraw.IsEnabled(kind)) return;
        foreach ((int x, int y) in map.QueryAabb(worldView))
            DebugDraw.Rect(x * map.TileW, y * map.TileH, map.TileW, map.TileH, color, width, kind);
    }

    /// <summary>AABB <paramref name="box"/> を <paramref name="delta"/> だけ動かす Sweep の
    /// 開始位置 (<paramref name="startColor"/>) と衝突で切り詰めた解決位置 (<paramref name="resolvedColor"/>) を描く。</summary>
    public static void Sweep(TileMap map, RectF box, Vector2 delta, uint startColor, uint resolvedColor,
        float width = 1.5f, string kind = Tiles)
    {
        if (!DebugDraw.IsEnabled(kind)) return;
        DebugDraw.Rect(box.X, box.Y, box.W, box.H, startColor, width, kind);
        Vector2 moved = map.Sweep(box, delta, out _, out _);
        DebugDraw.Rect(box.X + moved.X, box.Y + moved.Y, box.W, box.H, resolvedColor, width, kind);
    }

    /// <summary><see cref="CameraRig2D"/> の中央デッドゾーン矩形 (<paramref name="deadzoneColor"/>) と
    /// ワールド境界 (<paramref name="boundsColor"/>、設定時のみ) を描く。</summary>
    public static void CameraRig(CameraRig2D rig, uint deadzoneColor, uint boundsColor, float width = 1.5f, string kind = Camera)
    {
        if (!DebugDraw.IsEnabled(kind)) return;
        Vector2 p = rig.Position;
        DebugDraw.Rect(p.X - rig.Deadzone.X * 0.5f, p.Y - rig.Deadzone.Y * 0.5f,
            rig.Deadzone.X, rig.Deadzone.Y, deadzoneColor, width, kind);
        if (rig.WorldBounds is RectF wb)
            DebugDraw.Rect(wb.X, wb.Y, wb.W, wb.H, boundsColor, width, kind);
    }
}
