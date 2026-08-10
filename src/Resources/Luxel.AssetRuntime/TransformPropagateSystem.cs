using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.Ecs;

namespace Luxel.AssetRuntime;

/// <summary>
/// <see cref="LocalTransform"/> + 任意 <see cref="Parent"/> から <see cref="GlobalTransform"/> を計算。
/// 深い階層に対応するため複数回反復 (最大 8 回)。
/// <para>
/// <b>使い方</b>:
/// <code>
/// TransformPropagateSystem.Run(world);
/// var world = entity.GetComponent&lt;GlobalTransform&gt;().Matrix;
/// </code>
/// </para>
/// </summary>
public static class TransformPropagateSystem
{
    public static void Run(World world)
    {
        // GlobalTransform が無い entity に追加 (初期化)
        var withoutGlobal = new List<Entity>();
        world.Query<LocalTransform>().ForEachEntity((ref LocalTransform _, Entity e) =>
        {
            if (!e.HasComponent<GlobalTransform>()) withoutGlobal.Add(e);
        });
        foreach (var e in withoutGlobal) e.AddComponent(new GlobalTransform(Matrix4x4.Identity));

        // 最大 8 回反復 (深い階層対応)
        for (int iter = 0; iter < 8; iter++)
        {
            bool anyChange = false;
            world.Query<LocalTransform, GlobalTransform>().ForEachEntity((ref LocalTransform local, ref GlobalTransform global, Entity e) =>
            {
                Matrix4x4 newGlobal;
                if (e.HasComponent<Parent>())
                {
                    var parent = e.GetComponent<Parent>().ParentEntity;
                    if (parent.HasComponent<GlobalTransform>())
                        newGlobal = local.Matrix * parent.GetComponent<GlobalTransform>().Matrix;
                    else
                        newGlobal = local.Matrix; // 親未解決
                }
                else
                {
                    newGlobal = local.Matrix;
                }
                if (newGlobal != global.Matrix)
                {
                    global = new GlobalTransform(newGlobal);
                    anyChange = true;
                }
            });
            if (!anyChange) break;
        }
    }
}
