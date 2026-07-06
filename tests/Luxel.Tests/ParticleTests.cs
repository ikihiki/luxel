using System.Numerics;
using Luxel.Animation;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.TwoD;

namespace Luxel.Tests;

/// <summary>
/// パーティクルシステム (タスク 16, コア + .TwoD) の GPU 不要・決定的テスト:
/// xorshift 決定性 / ParticleValue (Const/Range/Curve) / ParticleColor 補間 / Emit・容量・連続放出 /
/// 積分 (速度/重力/抗力) / 寿命除去 / 発生順の安定 / フォースフック / ParticleNode.BuildScene のパス数。
/// </summary>
public class ParticleTests
{
    private static ParticleConfig Straight(float speed = 100, float gravity = 0, float drag = 0, float life = 1f, float size = 4f)
        => new(Life: life, Speed: speed, SpreadRadians: 0f, BaseAngle: 0f, Gravity: gravity, Drag: drag,
               Size: size, Color: ParticleColor.Const(0xFFFFFFFF));

    // ---- Xorshift64 ----

    [Fact]
    public void Xorshift_SameSeed_SameSequence()
    {
        var a = new Xorshift64(42);
        var b = new Xorshift64(42);
        for (int i = 0; i < 20; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void Xorshift_FloatInUnitRange()
    {
        var r = new Xorshift64(7);
        for (int i = 0; i < 1000; i++)
        {
            float f = r.NextFloat();
            Assert.True(f >= 0f && f < 1f);
        }
    }

    [Fact]
    public void Xorshift_ZeroSeed_FallsBackToDefault()
        => Assert.NotEqual(0ul, new Xorshift64(0).NextULong());

    // ---- ParticleValue ----

    [Fact]
    public void ParticleValue_Const_SampleAndEval()
    {
        ParticleValue v = 5f;   // 暗黙変換
        var rng = new Xorshift64(1);
        Assert.Equal(5f, v.Sample(ref rng));
        Assert.Equal(5f, v.Eval(0.5f));
        Assert.False(v.IsAnimated);
    }

    [Fact]
    public void ParticleValue_Range_SampleWithinBounds()
    {
        ParticleValue v = ParticleValue.Range(10, 20);
        var rng = new Xorshift64(99);
        for (int i = 0; i < 200; i++)
        {
            float s = v.Sample(ref rng);
            Assert.InRange(s, 10f, 20f);
        }
    }

    [Fact]
    public void ParticleValue_Curve_LerpsOverLifetime()
    {
        ParticleValue v = ParticleValue.Curved(0, 10, LinearCurve.Instance);
        Assert.True(v.IsAnimated);
        Assert.Equal(0f, v.Eval(0f), 3);
        Assert.Equal(5f, v.Eval(0.5f), 3);
        Assert.Equal(10f, v.Eval(1f), 3);
        Assert.Equal(10f, v.Eval(2f), 3);   // clamp
    }

    // ---- ParticleColor ----

    [Fact]
    public void ParticleColor_LerpsAllChannels()
    {
        var pc = new ParticleColor(Color2D.Rgba(255, 0, 0, 255), Color2D.Rgba(0, 0, 255, 0));
        Assert.Equal(Color2D.Rgba(255, 0, 0, 255), pc.Eval(0f));
        Assert.Equal(Color2D.Rgba(0, 0, 255, 0), pc.Eval(1f));
        uint mid = pc.Eval(0.5f);
        Assert.Equal(127, (int)(mid & 0xFF));            // R
        Assert.Equal(127, (int)((mid >> 16) & 0xFF));    // B
        Assert.Equal(127, (int)((mid >> 24) & 0xFF));    // A
    }

    // ---- Emit / capacity / integration ----

    [Fact]
    public void Emit_AddsAliveDeterministically()
    {
        var ps = new ParticleSystem(Straight(), capacity: 100, seed: 1);
        ps.Emit(new Vector3(0, 0, 0), 10);
        Assert.Equal(10, ps.Alive);
    }

    [Fact]
    public void Emit_OverCapacity_Ignored()
    {
        var ps = new ParticleSystem(Straight(), capacity: 3, seed: 1);
        ps.Emit(Vector3.Zero, 10);
        Assert.Equal(3, ps.Alive);
    }

    [Fact]
    public void Update_IntegratesVelocity()
    {
        var ps = new ParticleSystem(Straight(speed: 100), capacity: 4, seed: 1);
        ps.Emit(Vector3.Zero, 1);
        ps.Update(0.1f);
        Assert.Equal(10f, ps.Buffer.PosX[0], 3);   // 100 * 0.1
        Assert.Equal(0f, ps.Buffer.PosY[0], 3);
    }

    [Fact]
    public void Update_AppliesGravity()
    {
        var ps = new ParticleSystem(Straight(speed: 0, gravity: 300), capacity: 4, seed: 1);
        ps.Emit(Vector3.Zero, 1);
        ps.Update(0.1f);
        Assert.Equal(30f, ps.Buffer.VelY[0], 3);   // 300 * 0.1
        Assert.Equal(3f, ps.Buffer.PosY[0], 3);    // 30 * 0.1
    }

    [Fact]
    public void Update_DragReducesSpeed()
    {
        var ps = new ParticleSystem(Straight(speed: 100, drag: 0.5f), capacity: 4, seed: 1);
        ps.Emit(Vector3.Zero, 1);
        ps.Update(0.1f);
        Assert.Equal(95f, ps.Buffer.VelX[0], 3);   // 100 * (1 - 0.5*0.1)
    }

    [Fact]
    public void Update_RemovesExpired()
    {
        var ps = new ParticleSystem(Straight(life: 0.25f), capacity: 4, seed: 1);
        ps.Emit(Vector3.Zero, 1);
        ps.Update(0.1f);
        ps.Update(0.1f);
        Assert.Equal(1, ps.Alive);   // age 0.2 < 0.25
        ps.Update(0.1f);
        Assert.Equal(0, ps.Alive);   // age 0.3 >= 0.25
    }

    [Fact]
    public void Update_PreservesSpawnOrderWhenMiddleDies()
    {
        var ps = new ParticleSystem(Straight(speed: 0, life: 1f), capacity: 8, seed: 1);
        ps.Emit(Vector3.Zero, 3);
        // 手動でタグ付け: 位置で識別、真ん中を短命に
        ParticleBuffer b = ps.Buffer;
        b.PosX[0] = 10; b.PosX[1] = 20; b.PosX[2] = 30;
        b.LifeMax[1] = 0.05f;   // 真ん中が死ぬ
        ps.Update(0.1f);
        Assert.Equal(2, ps.Alive);
        Assert.Equal(10f, b.PosX[0], 3);   // 発生順維持 (10 → 30、入れ替わらない)
        Assert.Equal(30f, b.PosX[1], 3);
    }

    [Fact]
    public void SetEmission_EmitsAtRate()
    {
        var ps = new ParticleSystem(Straight(life: 100f), capacity: 100, seed: 1);
        ps.SetEmission(Vector3.Zero, rate: 8f);
        // 8 * 0.25 = 2.0 (float でも厳密) を 3 回 → 6 個
        ps.Update(0.25f);
        ps.Update(0.25f);
        ps.Update(0.25f);
        Assert.Equal(6, ps.Alive);
    }

    [Fact]
    public void Forces_HookModifiesVelocityBeforeIntegration()
    {
        var ps = new ParticleSystem(Straight(speed: 100), capacity: 4, seed: 1)
        {
            Forces = (s, dt) => { for (int i = 0; i < s.Count; i++) s.VelX[i] = 0f; },
        };
        ps.Emit(Vector3.Zero, 1);
        ps.Update(0.1f);
        Assert.Equal(0f, ps.Buffer.PosX[0], 3);   // フックで VelX=0 にしたので動かない
    }

    [Fact]
    public void Determinism_SameSeedSameResult()
    {
        var cfg = new ParticleConfig(Life: ParticleValue.Range(0.5f, 1.0f), Speed: ParticleValue.Range(50, 150),
            SpreadRadians: 3f, BaseAngle: 0f, Gravity: 200, Drag: 0.1f, Size: ParticleValue.Range(2, 6),
            Color: ParticleColor.Const(0xFFFFFFFF));
        var a = new ParticleSystem(cfg, 64, seed: 12345);
        var b = new ParticleSystem(cfg, 64, seed: 12345);
        for (int f = 0; f < 20; f++) { a.Emit(new Vector3(5, 5, 0), 3); a.Update(1f / 60); b.Emit(new Vector3(5, 5, 0), 3); b.Update(1f / 60); }
        Assert.Equal(a.Alive, b.Alive);
        for (int i = 0; i < a.Alive; i++)
        {
            Assert.Equal(a.Buffer.PosX[i], b.Buffer.PosX[i], 4);
            Assert.Equal(a.Buffer.PosY[i], b.Buffer.PosY[i], 4);
        }
    }

    // ---- ParticleNode.BuildScene (.TwoD) ----

    [Fact]
    public void BuildScene_OnePathPerAliveParticle()
    {
        var ps = new ParticleSystem(Straight(), capacity: 32, seed: 1);
        ps.Emit(Vector3.Zero, 5);
        Scene2D scene = ParticleNode.BuildScene(ps);
        Assert.Equal(5, scene.CountEncoded().Paths);
    }
}
