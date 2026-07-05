using Luxel.Assets;
using Luxel.Ecs;
using Luxel.Resources;

namespace Luxel.AssetRuntime;

/// <summary>
/// AssetMeshRef を持つ全 entity に対して Resources から fragment URI で vertex/index/materials buffer を
/// Load 完了させたうえで <see cref="DrawMesh"/> / <see cref="DrawMaterial"/> を attach する。
/// direct-ref: fragment URI は SceneAssets の内部 index (primitive の登場順) から組み立てる。
/// </summary>
public static class DrawableAttacher
{
    public static void AttachMesh(Luxel.Ecs.World world, SceneAssets assets, ResourceSystem resources, string assetUri)
    {
        // Primitive → 登場順 index (fragment URI 用) を作る
        var primIdx = new Dictionary<AssetPrimitive, int>();
        int idx = 0;
        foreach (var (_, prims) in assets.MeshPrimitives)
            foreach (var p in prims) primIdx[p] = idx++;

        var pending = new List<Task>();
        var queue = new List<(Friflo.Engine.ECS.Entity Entity,
                              ResourceHandle<GpuBuffer> Vh, ResourceHandle<GpuBuffer> Ih, ResourceHandle<GpuBuffer> Mh,
                              int IndexCount, int VertexStride, int MaterialIndex)>();

        foreach (var (node, entity) in assets.NodeEntities)
        {
            if (!entity.HasComponent<AssetMeshRef>()) continue;
            var mesh = entity.GetComponent<AssetMeshRef>().Mesh;
            if (mesh is null || mesh.Primitives.Count == 0) continue;

            var prim = mesh.Primitives[0];
            if (!primIdx.TryGetValue(prim, out var pidx)) continue;
            if (!assets.Primitives.TryGetValue(prim, out var gpu)) continue;

            int matIndex = 0;
            if (entity.HasComponent<AssetMaterialRef>())
            {
                var mat = entity.GetComponent<AssetMaterialRef>().Material;
                if (mat is not null && assets.MaterialIndex.TryGetValue(mat, out var mi)) matIndex = mi;
            }

            var vh = resources.Load<GpuBuffer>($"{assetUri}#mesh/{pidx}/vertex");
            var ih = resources.Load<GpuBuffer>($"{assetUri}#mesh/{pidx}/index");
            var mh = resources.Load<GpuBuffer>($"{assetUri}#materials");
            pending.Add(vh.Ready);
            pending.Add(ih.Ready);
            pending.Add(mh.Ready);

            queue.Add((entity, vh, ih, mh, gpu.IndexCount, gpu.VertexStride, matIndex));
        }
        Task.WaitAll(pending.ToArray());
        resources.Pump();

        foreach (var q in queue)
        {
            q.Entity.AddComponent(new DrawMesh
            {
                Vertex = q.Vh.Value,
                Index = q.Ih.Value,
                IndexCount = q.IndexCount,
                VertexStride = q.VertexStride,
            });
            q.Entity.AddComponent(new DrawMaterial
            {
                MaterialArray = q.Mh.Value,
                MaterialIndex = q.MaterialIndex,
            });
        }
    }
}
