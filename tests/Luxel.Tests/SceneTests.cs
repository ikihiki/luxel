using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Ecs;
using Luxel.Resources;

namespace Luxel.Tests;

public class SceneTests
{
    [Fact]
    public void SceneDocument_BuildsHierarchy_InEcs()
    {
        // 親 (translation=10,0,0) + 子 (translation=0,5,0) の 2-node scene
        var doc = new AssetDocument();
        var rootNode = new AssetNode { Name = "root", Translation = new Vector3(10, 0, 0) };
        var childNode = new AssetNode { Name = "child", Translation = new Vector3(0, 5, 0) };
        rootNode.Children.Add(childNode);
        doc.Nodes.Add(rootNode);
        doc.Nodes.Add(childNode);
        var scene = new AssetScene();
        scene.Roots.Add(rootNode);
        doc.Scenes.Add(scene);
        doc.DefaultScene = scene;

        var world = new Luxel.Ecs.World();
        // texture/mesh upload は GpuDevice 必要なので、ここでは Nodes 部分のみ確認
        // → SceneBuilder.Build は GpuDevice を要求するため、Build せず直接 entity を生成して検証

        // ECS 階層の手動構築 (Build の Node 展開ロジック等価)
        var rootEntity = world.CreateEntity(new Luxel.Ecs.LocalTransform(doc.Nodes[0].LocalMatrix));
        var childEntity = world.CreateEntity(new Luxel.Ecs.LocalTransform(doc.Nodes[1].LocalMatrix));
        childEntity.AddComponent(new Luxel.Ecs.Parent(rootEntity));

        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        var childWorld = childEntity.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix;
        Vector3 pos = Vector3.Transform(Vector3.Zero, childWorld);
        Assert.Equal(10f, pos.X, precision: 4);
        Assert.Equal(5f, pos.Y, precision: 4);
    }

    [Fact]
    public void SceneNode_LocalMatrix_FromTRS()
    {
        var n = new AssetNode
        {
            Translation = new Vector3(1, 2, 3),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
        };
        Vector3 p = Vector3.Transform(Vector3.Zero, n.LocalMatrix);
        Assert.Equal(new Vector3(1, 2, 3), p);
    }

    [Fact]
    public void SceneLoaders_DispatchByExtension()
    {
        AssetLoaders.Clear();
        var stub = new StubLoader();
        AssetLoaders.Register(stub);
        // 拡張子が一致しない場合は例外
        Assert.Throws<NotSupportedException>(() => AssetLoaders.LoadAsync("foo.unknown").GetAwaiter().GetResult());
        AssetLoaders.Clear();
    }

    private sealed class StubLoader : IAssetLoader
    {
        public bool CanLoad(string path) => path.EndsWith(".stub");
        public Task<AssetDocument> LoadAsync(string path) => Task.FromResult(new AssetDocument());
    }

    [Fact]
    public void SceneAnimationPlayer_SamplesTranslation()
    {
        // 0s で T=(0,0,0)、1s で T=(10,0,0) の translation channel
        var assets = new SceneAssets();
        var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.Identity));
        var targetNode = new AssetNode();
        assets.NodeEntities[targetNode] = e;

        var anim = new AssetAnimation { Name = "test", Duration = 1.0f };
        anim.Channels.Add(new AssetAnimationChannel
        {
            TargetNode = targetNode,
            Path = AssetAnimationPath.Translation,
            Sampler = new AssetAnimationSampler
            {
                Times = new[] { 0f, 1f },
                Values = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0) },
                Interpolation = AssetInterpolation.Linear,
            },
        });

        var player = new SceneAnimationPlayer(world, assets, anim);
        player.Sample(0.5f);  // 中間 = (5,0,0)
        var lt = e.GetComponent<Luxel.Ecs.LocalTransform>();
        Matrix4x4.Decompose(lt.Matrix, out _, out _, out var trans);
        Assert.Equal(5f, trans.X, precision: 4);
        Assert.Equal(0f, trans.Y, precision: 4);
    }

    [Fact]
    public async Task ResourceSystem_DedupsParallelGltfLoads()
    {
        var json = """
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"name":"r"}]}
        """;
        var files = new MemoryFileSystem();
        files.Set("scene.gltf", System.Text.Encoding.UTF8.GetBytes(json));
        using var resources = new ResourceSystem(
            sources: [new FileSource(files)],
            steps: [new GltfResourceStep()]);

        using ResourceHandle<AssetDocument> first = resources.Load<AssetDocument>("scene.gltf");
        using ResourceHandle<AssetDocument> second = resources.Load<AssetDocument>("scene.gltf");
        await Task.WhenAll(first.Ready, second.Ready);

        Assert.Same(first.Value, second.Value);
    }
}
