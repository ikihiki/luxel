using System.Numerics;
using Luxel.Diagnostics;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.Graphics.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム内 DevTools オーバーレイ (F1 トグル) — Q05/Q12 の gizmo・DevStats を実ゲームに結線する。
/// ON で <see cref="Gizmos2D"/> (衝突タイル / <see cref="CameraRig2D"/> デッドゾーン・境界) と
/// <see cref="ParticleGizmos"/> (エミッタ + 生存数) を <see cref="DebugDraw"/> に溜め、シーンが最前面へ Flush する。
/// 同時にゲームの観測値を <see cref="DevStats"/> に載せる (DevTools 未接続なら zero-cost で no-op)。
/// gizmo カテゴリは <see cref="DebugDraw"/> のグローバル ON/OFF なので OFF 時は溜め込みも割り当てもゼロ。
/// </summary>
public sealed class CavernDevOverlay
{
    private static readonly uint TileCol = Color2D.Rgba(240, 120, 90);
    private static readonly uint DeadzoneCol = Color2D.Rgba(90, 220, 120), BoundsCol = Color2D.Rgba(90, 200, 240);
    private static readonly uint EmitterCol = Color2D.Rgba(235, 120, 235);
    private static readonly uint PanelBg = Color2D.Rgba(10, 12, 20, 180), PanelText = Color2D.Rgba(210, 235, 210);

    /// <summary>gizmo/統計オーバーレイが表示中か。</summary>
    public bool Enabled { get; private set; }

    /// <summary>表示 ON/OFF を切り替え、gizmo カテゴリの <see cref="DebugDraw"/> 有効/無効を合わせる。</summary>
    public void Toggle() => SetEnabled(!Enabled);

    public void SetEnabled(bool on)
    {
        Enabled = on;
        DebugDraw.SetEnabled(Gizmos2D.Tiles, on);
        DebugDraw.SetEnabled(Gizmos2D.Camera, on);
        DebugDraw.SetEnabled(ParticleGizmos.Emitters, on);
    }

    /// <summary>gizmo コマンドをワールド空間で溜める (有効カテゴリのみ実際に溜まる)。シーンが後で Flush する。</summary>
    public void EmitGizmos(CavernSim sim, CameraRig2D rig, RectF worldView, ParticleSystem fx, Vector2[] torches)
    {
        Gizmos2D.TileCollision(sim.Map, worldView, TileCol);
        Gizmos2D.CameraRig(rig, DeadzoneCol, BoundsCol);
        foreach (Vector2 t in torches) ParticleGizmos.Emitter(fx, t, EmitterCol);
        ParticleGizmos.Emitter(fx, sim.PlayerCenter, EmitterCol);
    }

    /// <summary>ゲームの観測値を DevStats に載せる (DevTools 未接続なら no-op)。毎フレーム呼ぶ。</summary>
    public void PublishStats(CavernSim? sim, GameState state, int particleAlive, float fps)
    {
        DevStats.Set("fps", MathF.Round(fps));
        DevStats.Set("state", state.ToString());
        DevStats.Set("particles", particleAlive);
        if (sim is not null)
        {
            DevStats.Set("hp", sim.Hp);
            DevStats.Set("coins", sim.Coins);
            DevStats.Set("keys", sim.Keys);
            DevStats.Set("player.x", MathF.Round(sim.PlayerPos.X));
            DevStats.Set("player.y", MathF.Round(sim.PlayerPos.Y));
        }
    }

    /// <summary>統計パネル (スクリーン空間、カメラにアンカーして右上へ)。gizmo Flush とは別に呼ぶ。</summary>
    public void DrawStatsPanel(Scene2D s, DebugTextDrawer text, Vector2 camCenter, float zoom,
        int screenW, int screenH, CavernSim? sim, GameState state, int particleAlive, float fps)
    {
        Vector2 tl = camCenter - new Vector2(screenW / (2f * zoom), screenH / (2f * zoom));
        Vector2 W(float sx, float sy) => tl + new Vector2(sx / zoom, sy / zoom);
        float Sz(float v) => v / zoom;

        string[] lines =
        [
            $"F1 DevTools  fps {MathF.Round(fps),3}",
            $"state {state}",
            sim is null ? "sim -" : $"hp {sim.Hp}  coin {sim.Coins}  key {sim.Keys}/{sim.RequiredKeys}",
            sim is null ? "" : $"pos {MathF.Round(sim.PlayerPos.X)},{MathF.Round(sim.PlayerPos.Y)}",
            $"particles {particleAlive}",
        ];

        const float panelW = 150, lineH = 13;
        float panelH = lines.Length * lineH + 8;
        Vector2 p = W(screenW - panelW - 6, 6);
        s.FillRoundedRect(PanelBg, p.X, p.Y, Sz(panelW), Sz(panelH), Sz(3));
        float y = 6 + 11;
        foreach (string ln in lines)
        {
            if (ln.Length > 0)
            {
                Vector2 lp = W(screenW - panelW, y);
                text(s, ln, lp.X, lp.Y, Sz(11), PanelText);
            }
            y += lineH;
        }
    }
}
