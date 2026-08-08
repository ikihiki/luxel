using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.AssetsGpu;
using Luxel.Ecs;
using Luxel.Graphics.RenderGraph;
using Luxel.Resources;

namespace Luxel.AssetRuntime;

/// <summary>GPU shader が読む per-instance (80B = mat4 world + vec4 baseColor)。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneInstanceData
{
    public Matrix4x4 World;
    public Vector4 BaseColor;
    public const int Stride = 80;
}

/// <summary>
/// SceneAssets + ECS から bindless instance buffer を生成。
/// direct-ref: <see cref="AssetMeshRef.Mesh"/> の primitive[0] を代表として instance を生成。
/// </summary>
public sealed class SceneRenderExtractor : IRenderExtractor, IDisposable
{
    private readonly Luxel.Ecs.World _world;
    private readonly SceneAssets _assets;
    private RenderBuffer<SceneInstanceData>? _renderBuffer;
    private int _capacity;
    public int InstanceCount { get; private set; }

    /// <summary>Extract 後、primitive ごとの (Primitive, InstanceStart, InstanceCount)。</summary>
    public List<(AssetPrimitive Primitive, int InstanceStart, int InstanceCount)> DrawList { get; } = new();

    public SceneRenderExtractor(Luxel.Ecs.World world, SceneAssets assets)
    {
        _world = world;
        _assets = assets;
    }

    public GpuBuffer InstanceBuffer => _renderBuffer?.Buffer ?? throw new InvalidOperationException("Extract() first");

    public void Extract(ExtractContext ctx)
    {
        var byPrim = new Dictionary<AssetPrimitive, List<SceneInstanceData>>();
        _world.Query<AssetMeshRef>().ForEachEntity((ref AssetMeshRef mr, Entity e) =>
        {
            var mesh = mr.Mesh;
            if (mesh is null || mesh.Primitives.Count == 0) return;

            // 代表 primitive: 現状は primitive[0] のみ (multi-primitive は SceneBuilder で child entity 化される想定)
            var prim = mesh.Primitives[0];
            if (!_assets.Primitives.ContainsKey(prim)) return;

            Matrix4x4 worldM = e.HasComponent<Luxel.Ecs.GlobalTransform>()
                ? e.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix
                : Matrix4x4.Identity;

            Vector4 color = Vector4.One;
            if (e.HasComponent<AssetMaterialRef>())
            {
                var mat = e.GetComponent<AssetMaterialRef>().Material;
                if (mat is not null && _assets.MaterialIndex.TryGetValue(mat, out var mi))
                    color = _assets.MaterialArray[mi].BaseColor;
            }
            if (!byPrim.TryGetValue(prim, out var list))
            {
                list = new List<SceneInstanceData>();
                byPrim[prim] = list;
            }
            list.Add(new SceneInstanceData { World = worldM, BaseColor = color });
        });

        DrawList.Clear();
        var all = new List<SceneInstanceData>();
        foreach (var (prim, list) in byPrim)
        {
            DrawList.Add((prim, all.Count, list.Count));
            all.AddRange(list);
        }
        InstanceCount = all.Count;

        int needed = Math.Max(1, InstanceCount);
        if (_renderBuffer == null || _capacity < needed)
        {
            _renderBuffer?.Dispose();
            _capacity = Math.Max(needed, 32);
            _renderBuffer = new RenderBuffer<SceneInstanceData>(ctx.Device, _capacity, "sceneInstances");
        }
        if (InstanceCount > 0)
        {
            var span = _renderBuffer.Data;
            for (int i = 0; i < InstanceCount; i++) span[i] = all[i];
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
