using Luxel.Ecs;
using Luxel.Assets;
using Friflo.Engine.ECS;
using Luxel.RenderGraph;
using Luxel.Resources;

namespace Luxel.AssetRuntime;

/// <summary>
/// DRAW-M3: <see cref="DrawMesh"/> + <see cref="DrawInstance"/> + <see cref="DrawMaterial"/> を持つ全 entity を
/// 走査し、<see cref="RenderGraph.RenderGraph"/> に import してから <see cref="DrawItem"/> のリストとして返す。
///
/// Sample 側はこのリストを for-loop で回して push constant を組み立てて Draw を発行するだけでよい。
/// push constant layout や pipeline binding は shader 依存なので本 collector は関知しない。
/// </summary>
public static class DrawableCollector
{
    /// <summary>1 entity 1 draw call ぶんのハンドル束。RG.Execute() 内で使う。</summary>
    public readonly struct DrawItem
    {
        public readonly Entity Entity;
        public readonly BufferHandle Vertex;
        public readonly BufferHandle Index;
        public readonly BufferHandle Instance;
        public readonly BufferHandle Material;
        public readonly BufferHandle Joint;         // Invalid なら skinning 無し
        public readonly int IndexCount;
        public readonly int InstanceOffset;
        public readonly int InstanceCount;
        public readonly int MaterialIndex;
        public readonly int JointCount;
        public bool HasSkinning => Joint.IsValid;

        internal DrawItem(Entity entity, BufferHandle v, BufferHandle i, BufferHandle inst, BufferHandle mat,
            BufferHandle joint, int indexCount, int instanceOffset, int instanceCount, int materialIndex, int jointCount)
        {
            Entity = entity;
            Vertex = v; Index = i; Instance = inst; Material = mat; Joint = joint;
            IndexCount = indexCount; InstanceOffset = instanceOffset; InstanceCount = instanceCount;
            MaterialIndex = materialIndex; JointCount = jointCount;
        }
    }

    /// <summary>DrawMesh + DrawInstance + DrawMaterial 3 点を持つ entity を全て collect。
    /// DrawSkinning があれば Joint handle も取得。</summary>
    public static List<DrawItem> Collect(Luxel.Ecs.World world, Luxel.RenderGraph.RenderGraph rg)
    {
        var items = new List<DrawItem>();
        world.Query<DrawMesh, DrawInstance, DrawMaterial>()
             .ForEachEntity((ref DrawMesh dm, ref DrawInstance di, ref DrawMaterial dma, Entity entity) =>
             {
                 BufferHandle hV = rg.ImportBuffer(dm.Vertex, "vert");
                 BufferHandle hI = rg.ImportBuffer(dm.Index, "idx");
                 BufferHandle hInst = rg.ImportBuffer(di.Buffer, "inst");
                 BufferHandle hMat = rg.ImportBuffer(dma.MaterialArray, "mat");
                 BufferHandle hJoint = BufferHandle.Invalid;
                 int jointCount = 0;
                 if (entity.HasComponent<DrawSkinning>())
                 {
                     var ds = entity.GetComponent<DrawSkinning>();
                     hJoint = rg.ImportBuffer(ds.JointBuffer, "joints");
                     jointCount = ds.JointCount;
                 }
                 items.Add(new DrawItem(entity, hV, hI, hInst, hMat, hJoint,
                     dm.IndexCount, di.InstanceOffset, di.InstanceCount <= 0 ? 1 : di.InstanceCount,
                     dma.MaterialIndex, jointCount));
             });
        return items;
    }
}
