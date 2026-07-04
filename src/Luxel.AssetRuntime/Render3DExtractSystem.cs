using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Luxel.Ecs;
using Luxel.AssetsGpu;

namespace Luxel.AssetRuntime;

/// <summary>GPU シェーダが読む per-instance データ (80 バイト = mat4x4 world + vec4 color)。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InstanceData
{
    public Matrix4x4 World;
    public Vector4 Color;
    public const int Stride = 80;
}

/// <summary>
/// <see cref="GlobalTransform"/> + <see cref="Color3D"/> + <see cref="MeshRef"/> を持つ entity を
/// bindless instance バッファへ集約する Friflo <see cref="BaseSystem"/> (システム標準の 3D 抽出)。
///
/// <para><b>使い方</b> (Friflo SystemGroup 経由):
/// <code>
/// var root = new SystemRoot(world.Store) { new Render3DExtractSystem(world, device) };
/// root.Update(default);                     // Update ごとに Extract 相当が走る
/// gpuBuffer = system.InstanceBuffer;
/// </code>
/// </para>
///
/// <para><b>手動</b> (SystemGroup を使わない場合):
/// <code>
/// var sys = new Render3DExtractSystem(world, device);
/// sys.Extract();                            // 直接呼ぶ
/// </code>
/// </para>
///
/// <para>ユーザーが独自の抽出 System / ロジックを書きたい場合は本 System を使わず、
/// 同じ <see cref="InstanceData"/> レイアウトの <see cref="RenderBuffer{T}"/> を作って書けばよい。</para>
/// </summary>
public sealed class Render3DExtractSystem : BaseSystem, IDisposable
{
    private readonly World _world;
    private readonly GpuDevice _device;
    private RenderBuffer<InstanceData>? _renderBuffer;
    private int _capacity;

    public int InstanceCount { get; private set; }

    public Render3DExtractSystem(World world, GpuDevice device)
    {
        _world = world;
        _device = device;
    }

    /// <summary>抽出結果の GPU バッファ (bindless index 経由で shader が読む)。</summary>
    public GpuBuffer InstanceBuffer =>
        _renderBuffer?.Buffer ?? throw new InvalidOperationException("Extract() を先に呼んでください。");

    /// <summary>Friflo SystemGroup が Update するときの entry point。単体呼出は <see cref="Extract"/>。</summary>
    protected override void OnUpdateGroup() => Extract();

    /// <summary>1 フレーム分の抽出 → GPU 反映。Friflo group を使わずに手動で呼んでもよい。</summary>
    public void Extract()
    {
        var rows = new List<InstanceData>();
        _world.Query<GlobalTransform, Color3D, MeshRef>()
              .ForEachEntity((ref GlobalTransform gt, ref Color3D c, ref MeshRef _m, Entity e) =>
        {
            if (e.HasComponent<Visible>() && !e.GetComponent<Visible>().On) return;
            rows.Add(new InstanceData { World = gt.Matrix, Color = c.Rgba });
        });
        InstanceCount = rows.Count;

        int needed = Math.Max(1, rows.Count);
        if (_renderBuffer == null || _capacity < needed)
        {
            _renderBuffer?.Dispose();
            _capacity = Math.Max(needed, 32);
            _renderBuffer = new RenderBuffer<InstanceData>(_device, _capacity, "ecsInstances");
        }
        if (rows.Count > 0)
        {
            var span = _renderBuffer.Data;
            for (int i = 0; i < rows.Count; i++) span[i] = rows[i];
            _renderBuffer.MarkDirty();
        }
        _renderBuffer?.FlushImmediate();
    }

    public void Dispose()
    {
        _renderBuffer?.Dispose();
        _renderBuffer = null;
    }
}
