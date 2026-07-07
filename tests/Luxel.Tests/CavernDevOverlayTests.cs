using System.Numerics;
using LuxelCavern.Core;
using Luxel.Diagnostics;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.TwoD;

namespace Luxel.Tests;

/// <summary>グローバルな <see cref="DebugDraw"/> 状態を触るテストを直列化する (並列実行で干渉しないように)。</summary>
[CollectionDefinition("GlobalGizmo", DisableParallelization = true)]
public class GlobalGizmoCollection { }

/// <summary>
/// ゲーム内 DevTools オーバーレイ <see cref="CavernDevOverlay"/>: トグルが gizmo カテゴリを ON/OFF する /
/// 有効時のみ gizmo コマンドが溜まる (OFF はゼロ) / DevStats は未接続時 no-op。
/// </summary>
[Collection("GlobalGizmo")]
public class CavernDevOverlayTests
{
    public CavernDevOverlayTests() => DebugDraw.Reset();   // 静的状態をテスト間で分離

    private static ParticleSystem MakeFx() => new(
        new ParticleConfig(Life: 5f, Speed: 0f, SpreadRadians: 0, BaseAngle: 0, Gravity: 0, Drag: 0,
            Size: 1f, Color: ParticleColor.Const(0xFFFFFFFF)), capacity: 32, seed: 1);

    private static CameraRig2D MakeRig() => new()
    {
        Deadzone = new Vector2(120, 80),
        WorldBounds = new RectF(0, 0, 700, 384),
    };

    [Fact]
    public void Toggle_FlipsEnabledAndGizmoCategories()
    {
        var dev = new CavernDevOverlay();
        Assert.False(dev.Enabled);

        dev.Toggle();
        Assert.True(dev.Enabled);
        Assert.True(DebugDraw.IsEnabled(Gizmos2D.Tiles));
        Assert.True(DebugDraw.IsEnabled(Gizmos2D.Camera));
        Assert.True(DebugDraw.IsEnabled(ParticleGizmos.Emitters));

        dev.Toggle();
        Assert.False(dev.Enabled);
        Assert.False(DebugDraw.IsEnabled(Gizmos2D.Tiles));
    }

    [Fact]
    public void EmitGizmos_AccumulatesWhenEnabled()
    {
        var dev = new CavernDevOverlay();
        CavernSim sim = CavernTestLevel.CreateSim();
        CameraRig2D rig = MakeRig();
        ParticleSystem fx = MakeFx();
        var view = new RectF(0, 0, 400, 300);

        dev.SetEnabled(true);
        dev.EmitGizmos(sim, rig, view, fx, [new Vector2(150, 274)]);
        Assert.True(DebugDraw.PendingCount > 0);
    }

    [Fact]
    public void EmitGizmos_WhenDisabled_IsZeroAlloc()
    {
        var dev = new CavernDevOverlay();
        CavernSim sim = CavernTestLevel.CreateSim();
        ParticleSystem fx = MakeFx();

        dev.SetEnabled(false);
        dev.EmitGizmos(sim, MakeRig(), new RectF(0, 0, 400, 300), fx, [new Vector2(150, 274)]);
        Assert.Equal(0, DebugDraw.PendingCount);   // OFF カテゴリは溜め込みゼロ
    }

    [Fact]
    public void PublishStats_WhenDiagnosticsOff_IsNoOp()
    {
        DevStats.Clear();
        var dev = new CavernDevOverlay();
        CavernSim sim = CavernTestLevel.CreateSim();
        dev.PublishStats(sim, GameState.Playing, 5, 60f);   // DevTools 未接続 → Set は no-op
        Assert.Empty(DevStats.Snapshot());
    }
}
