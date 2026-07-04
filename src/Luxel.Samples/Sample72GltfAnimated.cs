using System.Numerics;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 72: Khronos <c>BoxAnimated.glb</c> を読み込み → SceneAnimationPlayer で時刻別に sample。
/// </summary>
public static class Sample72GltfAnimated
{
    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 72: glTF (BoxAnimated.glb) Animation player demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "BoxAnimated.glb"),
            Path.Combine(AppContext.BaseDirectory, "tools", "khronos-samples", "BoxAnimated.glb"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "BoxAnimated.glb"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            Console.Error.WriteLine("FAILED: BoxAnimated.glb not found");
            return 1;
        }

        var doc = new GltfLoader().LoadAsync(path).GetAwaiter().GetResult();
        Console.WriteLine($"  animations: {doc.Animations.Count}");
        if (doc.Animations.Count == 0) { Console.Error.WriteLine("FAILED: no animation"); return 1; }
        var anim = doc.Animations[0];
        Console.WriteLine($"  anim '{anim.Name}': {anim.Channels.Count} channels, duration {anim.Duration:F2}s");

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);

        var player = new SceneAnimationPlayer(world, assets, anim);

        // 3 つの時刻で sample → animated node の Translation を観測
        // BoxAnimated は node 1 を上下に動かす
        int animatedNode = anim.Channels[0].TargetNodeIndex;
        var entity = assets.NodeEntities[animatedNode];

        Vector3[] observed = new Vector3[3];
        float[] times = { 0f, anim.Duration * 0.5f, anim.Duration };
        for (int i = 0; i < 3; i++)
        {
            player.Sample(times[i]);
            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            var lt = entity.GetComponent<Luxel.Ecs.LocalTransform>();
            Matrix4x4.Decompose(lt.Matrix, out _, out _, out observed[i]);
            Console.WriteLine($"  t={times[i]:F2}s: T={observed[i]}");
        }

        // BoxAnimated は rotation animation. translation 比較ではなく、rotation 行列の変化を見る
        // → LocalTransform.Matrix 全体の Frobenius norm 差を確認
        player.Sample(0f);
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        var m0 = entity.GetComponent<Luxel.Ecs.LocalTransform>().Matrix;
        player.Sample(anim.Duration * 0.5f);
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        var m1 = entity.GetComponent<Luxel.Ecs.LocalTransform>().Matrix;
        float diff = MatNorm(m0 - m1);
        Console.WriteLine($"  matrix diff (0s vs 0.5*dur): {diff:F4}");
        bool ok = diff > 1e-3f;
        Console.WriteLine(ok ? "OK: SC-M5 (SceneAnimationPlayer 補間) 動作" : "FAILED: no animation change");
        return ok ? 0 : 1;
    }

    private static float MatNorm(Matrix4x4 m)
    {
        float s = 0;
        s += m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13 + m.M14 * m.M14;
        s += m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23 + m.M24 * m.M24;
        s += m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33 + m.M34 * m.M34;
        s += m.M41 * m.M41 + m.M42 * m.M42 + m.M43 * m.M43 + m.M44 * m.M44;
        return MathF.Sqrt(s);
    }
}
