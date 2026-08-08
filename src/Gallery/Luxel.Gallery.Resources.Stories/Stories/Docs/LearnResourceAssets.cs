using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>CPU asset class families and their optional GPU representation.</summary>
public static class LearnResourceAssets
{
    [Story("Learn/Resources/Assets/Overview", Order = 8, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/Overview", $$"""
        # Assets overview

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/Overview", "Beginner", "Tools / Headless / Runtime", "CPU assets / optional GPU", "Resources reload and lifetime")}}

        `Luxel.Assets` is the format-neutral CPU model used after import. It does not load URIs and does not own a GPU device. `Luxel.Resources` supplies cache and lifetime, import packages such as `Luxel.Assets.Gltf` create an `AssetDocument`, and `Luxel.AssetsGpu` optionally mirrors individual CPU objects on a device.

        ```text
        source bytes → importer → AssetDocument (CPU)
                                   ├─ inspection / validation / tools
                                   ├─ AssetGpuRegistry → GpuMesh / GpuMaterial / GpuTexture / GpuSkin
                                   └─ AssetRuntime → ECS scene and render extraction
        ```

        ## Class-family route

        1. [Document and scene graph](story:Learn/Resources/Assets/DocumentAndSceneGraph)
        2. [Meshes and primitives](story:Learn/Resources/Assets/MeshesAndPrimitives)
        3. [Materials, textures, and samplers](story:Learn/Resources/Assets/MaterialsTexturesAndSamplers)
        4. [Animation, skin, camera, and light](story:Learn/Resources/Assets/AnimationSkinCameraAndLight)
        5. [Loading and GPU mirrors](story:Learn/Resources/Assets/LoadingAndGpu)
        6. [Shader ABI](story:Learn/Resources/Assets/ShaderAbi)

        Import-format behavior belongs in the separate [glTF branch](story:Learn/Resources/Gltf/Overview).
        """);

    [Story("Learn/Resources/Assets/DocumentAndSceneGraph", Order = 9, Toc = true)]
    public static StoryResult DocumentAndSceneGraph(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/DocumentAndSceneGraph", $$"""
        # AssetDocument and scene graph

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/DocumentAndSceneGraph", "Beginner", "Tools / Headless / Runtime", "CPU asset model", "Assets overview")}}

        `AssetDocument` groups meshes, materials, textures, samplers, skins, animations, cameras, lights, nodes, and scenes produced by one import. Relationships are direct object references rather than public integer indices.

        `AssetScene.Roots` selects root nodes. Each `AssetNode` stores children and optional mesh, skin, camera, or light attachments. `LocalMatrix` uses the node's explicit matrix when present; otherwise it composes scale, rotation, and translation. The CPU model has no parent pointer, so traversal carries the parent transform.

        ```csharp
        static void Visit(AssetNode node, Matrix4x4 parent)
        {
            Matrix4x4 world = node.LocalMatrix * parent;
            if (node.Mesh is { } mesh) Inspect(mesh, world);
            foreach (AssetNode child in node.Children) Visit(child, world);
        }
        ```

        Keep the owning document or another explicit owner alive while retaining its referenced objects. The objects are ordinary CPU values; a document handle does not implicitly lease GPU mirrors.
        """);

    [Story("Learn/Resources/Assets/MeshesAndPrimitives", Order = 10, Toc = true)]
    public static StoryResult MeshesAndPrimitives(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/MeshesAndPrimitives", $$"""
        # Meshes and primitives

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/MeshesAndPrimitives", "Beginner", "Tools / Runtime", "CPU mesh model", "Document and scene graph")}}

        `AssetMesh.Primitives` partitions a mesh into draw-sized units. An `AssetPrimitive` owns an `AssetVertexBuffer`, optional indices, optional material, and zero or more morph targets. Positions are required; normals, tangents, two UV sets, color, joints, and weights are optional and must agree on vertex count when present.

        | Family | Important members |
        | --- | --- |
        | `AssetVertexBuffer` | positions, normals, tangents, UVs, color, joints, weights |
        | `AssetPrimitive` | attributes, indices, material, morph targets, topology data |
        | `AssetMorphTarget` | position / normal / tangent deltas |
        | `AssetAabb` | local bounds used for inspection and culling inputs |

        Validate attribute lengths and index ranges before upload. A GPU factory chooses a 32-byte ordinary vertex layout or 56-byte skinned layout from the available attributes; shader selection must match that stride.
        """);

    [Story("Learn/Resources/Assets/MaterialsTexturesAndSamplers", Order = 11, Toc = true)]
    public static StoryResult MaterialsTexturesAndSamplers(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/MaterialsTexturesAndSamplers", $$"""
        # Materials, textures, and samplers

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/MaterialsTexturesAndSamplers", "Beginner", "Tools / Runtime", "CPU material model", "Meshes and primitives")}}

        `AssetMaterial` stores PBR metallic-roughness inputs, alpha mode, double-sided state, unlit/custom shader selection, and texture references. `AssetTexture` stores decoded pixels and format metadata. `AssetSampler` stores filtering and U/V wrap. `AssetTextureRef` connects a material slot to a texture plus UV-set and transform information.

        The data model is intentionally broader than the current standard scene shader. Current material GPU data includes base color plus one texture and sampler bindless index; metallic, roughness, normal, occlusion, and emissive values require a matching ABI and shader extension before they affect pixels.

        Upload deduplication is by CPU object identity in `AssetGpuRegistry`. Registering a material recursively registers referenced texture and sampler objects, so their lifetime must cover the material mirror.
        """);

    [Story("Learn/Resources/Assets/AnimationSkinCameraAndLight", Order = 12, Toc = true)]
    public static StoryResult AnimationSkinCameraAndLight(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/AnimationSkinCameraAndLight", $$"""
        # Animation, skin, camera, and light

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/AnimationSkinCameraAndLight", "Intermediate", "Tools / ECS runtime", "CPU asset model", "Materials, textures, and samplers")}}

        `AssetAnimation` contains samplers and node/path channels for translation, rotation, scale, and morph weights. `AssetSkin` keeps joint-node references, inverse-bind matrices in the same order, and an optional skeleton root. `AssetCamera` stores perspective or orthographic parameters. `AssetLight` stores directional, point, or spot data.

        These classes describe imported intent; they do not update a world. The runtime must sample animation, propagate node transforms, calculate joint matrices, update morph weights, then extract draw data. Cameras and lights likewise need renderer-specific buffer encoding.

        ```text
        animation sample → local node values → transform propagation
                                             ├─ skin joint matrices
                                             ├─ morph weights
                                             └─ camera/light/render extraction
        ```

        The glTF-specific interpolation and deformation path is covered by [Animation, skinning, and morph](story:Learn/Resources/Gltf/AnimationSkinningAndMorph).
        """);

    [Story("Learn/Resources/Assets/LoadingAndGpu", Order = 13, Toc = true)]
    public static StoryResult LoadingAndGpu(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/LoadingAndGpu", $$"""
        # Loading assets and creating GPU mirrors

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/LoadingAndGpu", "Intermediate", "Standalone / Browser / Game", "Resources + AssetsGpu", "Animation, skin, camera, and light")}}

        Importers are registered as Resource steps. For glTF, register `GltfResourceStep` and load `AssetDocument`; for programmatic assets, publish or create the CPU object directly.

        ```csharp
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> document =
            resources.Load<AssetDocument>("models/scene.glb");
        await document.Ready;
        ```

        `InstallAssetGpuLifecycle(device)` creates a device-bound `AssetGpuRegistry`, registers `AssetTexture → GpuTexture`, `AssetSampler → GpuSampler`, `AssetMaterial → GpuMaterial`, `AssetMesh → GpuMesh`, and `AssetSkin → GpuSkin` steps, and installs the deferred-dispose idle hook.

        ```csharp
        using AssetGpuInstallation installation = resources.InstallAssetGpuLifecycle(device);
        using ResourceScope scope = resources.CreateScope("scene/main");
        ResourceHandle<GpuMesh> mesh =
            scope.Create<AssetMesh, GpuMesh>("player", cpuMesh);
        await mesh.Ready;
        ```

        End draws first, then dispose scopes/handles, then the installation/registry, and finally the device. CPU handles and GPU handles are separate leases.
        """);

    [Story("Learn/Resources/Assets/ShaderAbi", Order = 14, Toc = true)]
    public static StoryResult ShaderAbi(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/ShaderAbi", $$"""
        # Asset shader ABI

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/ShaderAbi", "Intermediate", "GPU runtime", "Slang / bindless resources", "Loading and GPU mirrors")}}

        CPU asset classes are encoded into fixed-stride GPU records. C# field order, struct packing, shader offsets, and the selected vertex variant must change together.

        | Record | Current stride |
        | --- | ---: |
        | ordinary vertex | 32 bytes: position 12, normal 12, UV 8 |
        | skinned vertex | 56 bytes: ordinary vertex, packed joints 8, weights 16 |
        | scene instance | 80 bytes |
        | `MaterialGpuData` | 32 bytes |
        | joint matrix | 64 bytes |
        | morph delta | 24 bytes |

        Scene shaders read vertex and optional index buffers by bindless index. Material data contains base color, texture index, sampler index, and flags. Skinning reads four joints and weights; morphing adds weighted position and normal deltas before world transformation.

        When extending the ABI, update the C# encoder, shader decoder, stride assertions/tests, root arguments, and pipeline variant in one reviewable change. A field added only to `AssetMaterial` has no rendering effect until all those layers consume it.
        """);
}
