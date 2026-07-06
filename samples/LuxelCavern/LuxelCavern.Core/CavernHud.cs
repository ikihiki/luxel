using System.Numerics;
using Luxel.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム HUD (HP ハート・コイン数・鍵数) と一時停止オーバーレイの描画 (純ロジック・GPU 非依存)。
/// カメラでワールドが変換される <see cref="Scene2D"/> に描くため、**カメラにアンカーしてスクリーン空間**へ置く
/// (screen(px) → world = カメラ中心とズームから逆算)。テキストは <see cref="DebugTextDrawer"/> 委譲で描く。
/// Gallery ストーリーからも実時間 exe からも同じ HUD を使える。
/// </summary>
public static class CavernHud
{
    private static readonly uint HeartOn = Color2D.Rgba(226, 72, 82);
    private static readonly uint HeartOff = Color2D.Rgba(70, 72, 84);
    private static readonly uint CoinColor = Color2D.Rgba(250, 210, 70);
    private static readonly uint KeyColor = Color2D.Rgba(240, 205, 90);
    private static readonly uint TextColor = Color2D.Rgba(245, 245, 250);

    /// <summary>HUD を描く。<paramref name="camCenter"/>/<paramref name="zoom"/> は描画に使うカメラ、
    /// <paramref name="screenW"/>/<paramref name="screenH"/> は表示ピクセルサイズ。</summary>
    public static void Draw(Scene2D s, DebugTextDrawer text, CavernSim sim,
        Vector2 camCenter, float zoom, int screenW, int screenH)
    {
        Vector2 tl = camCenter - new Vector2(screenW / (2f * zoom), screenH / (2f * zoom));
        Vector2 W(float sx, float sy) => tl + new Vector2(sx / zoom, sy / zoom);   // screen px → world
        float Sz(float sz) => sz / zoom;

        // HP ハート
        for (int i = 0; i < sim.MaxHp; i++)
        {
            Vector2 p = W(8 + i * 15, 8);
            s.FillRoundedRect(i < sim.Hp ? HeartOn : HeartOff, p.X, p.Y, Sz(11), Sz(11), Sz(3));
        }

        // コイン (アイコン + ×N)
        Vector2 cp = W(9, 28);
        s.FillCircle(CoinColor, cp.X + Sz(5), cp.Y + Sz(5), Sz(5), 12);
        Vector2 ct = W(20, 26);
        text(s, $"×{sim.Coins}", ct.X, ct.Y + Sz(11), Sz(13), TextColor);

        // 鍵 (アイコン + N/必要数)
        Vector2 kp = W(9, 44);
        s.FillRoundedRect(KeyColor, kp.X, kp.Y + Sz(3), Sz(10), Sz(5), Sz(2));
        Vector2 kt = W(22, 42);
        text(s, $"{sim.Keys}/{sim.RequiredKeys}", kt.X, kt.Y + Sz(11), Sz(13), TextColor);
    }

    /// <summary>一時停止オーバーレイ (画面全体を暗くして中央に文言)。</summary>
    public static void DrawPauseOverlay(Scene2D s, DebugTextDrawer text,
        Vector2 camCenter, float zoom, int screenW, int screenH, string title, string hint)
    {
        Vector2 tl = camCenter - new Vector2(screenW / (2f * zoom), screenH / (2f * zoom));
        float ww = screenW / zoom, wh = screenH / zoom;
        s.FillRect(Color2D.Rgba(10, 12, 20, 170), tl.X, tl.Y, ww, wh);   // 半透明の暗幕
        Vector2 c = camCenter;
        text(s, title, c.X - title.Length * 5f / zoom, c.Y, 20f / zoom, Color2D.Rgba(245, 245, 250));
        text(s, hint, c.X - hint.Length * 3f / zoom, c.Y + 24f / zoom, 12f / zoom, Color2D.Rgba(200, 205, 220));
    }
}
