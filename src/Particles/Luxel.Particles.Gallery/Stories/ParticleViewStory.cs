using System.Numerics;
using Luxel.Particles;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;
using static Luxel.Particles.UI.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// <c>ParticleView</c> ([UiComponent]) のデモ — パーティクルを UI ツリーに直接埋め込み、widget の
/// <c>AddAnimation</c> Tick で駆動する (ゲームの ECS system 呼び出しに対する UI 埋め込み版)。
/// play は固定シード + 固定 dt で <c>Step</c> → <c>Snap</c> なので golden 決定的。
/// </summary>
[StoryMeta("Controls/Rendering/ParticleView")]
public static class ParticleViewStories
{
    private const float VW = 360, VH = 168;

    [Story(Path = "Controls/Rendering/ParticleView/Basic", ArgsEnabled = false,
        ShortDescription = "固定シードのバーストを UI ツリーへ埋め込み、Tick 駆動の描画を確認します。",
        LongDescription = "メモリ内の ParticleSystem に固定シードで粒子を放出し、同じ初期状態から決定的にアニメーションする基本例です。")]
    public static StoryResult View(StoryContext ctx)
    {
        var cfg = new ParticleConfig(
            Life: ParticleValue.Range(0.5f, 1.0f), Speed: ParticleValue.Range(70, 150),
            SpreadRadians: MathF.PI, BaseAngle: -MathF.PI / 2, Gravity: 240, Drag: 0.4f,
            Size: 5f, Color: new ParticleColor(Color2D.Rgba(120, 220, 255, 255), Color2D.Rgba(60, 90, 220, 0)),
            Shape: ParticleShape.Circle);
        var ps = new ParticleSystem(cfg, capacity: 120, seed: 0xCAFE);
        ps.Emit(new Vector3(VW / 2, VH / 2, 0), 80);

        ctx.Play(async d =>
        {
            await d.Step(14);   // 固定 dt で 14 フレーム進める (爆発が広がった途中)
            await d.Snap();
        });

        return ParticleView(ps, VW, VH, animated: true);
    }
}
