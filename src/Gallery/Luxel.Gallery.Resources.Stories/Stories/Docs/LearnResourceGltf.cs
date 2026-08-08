using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>glTF import, dependency, diagnostics, runtime, and lifetime course.</summary>
public static class LearnResourceGltf
{
    [Story("Learn/Resources/Gltf/Overview", Order = 15, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/Overview", $$"""
        # glTF overview

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/Overview", "Beginner", "Tools / Headless / Runtime", "Luxel.Assets.Gltf", "Assets shader ABI")}}

        `Luxel.Assets.Gltf` parses `.gltf` JSON and `.glb` containers into the format-neutral `AssetDocument`. The importer resolves buffers, images, accessors, node relationships, skins, and animation; it does not create a GPU device or render a scene.

        ```text
        .gltf/.glb + external buffers/images
          → GltfResourceStep
          → AssetDocument
          → inspect, validate, upload individual assets, or build an ECS scene
        ```

        This branch separates registration/loading, external dependency resolution, diagnostics, runtime expansion, deformation, and reload lifetime. Start with CPU document loading before adding `SceneAssets` or a renderer.
        """);

    [Story("Learn/Resources/Gltf/RegistrationAndLoading", Order = 16, Toc = true)]
    public static StoryResult RegistrationAndLoading(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/RegistrationAndLoading", $$"""
        # Register and load glTF

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/RegistrationAndLoading", "Beginner", "Tools / Headless / Runtime", "Resources + Assets.Gltf", "glTF overview")}}

        Register the importer explicitly; ResourceSystem does not scan assemblies. The generic overload is the reflection-free path for browser and trimmed hosts.

        ```csharp
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> document =
            resources.Load<AssetDocument>("models/Box.glb");
        await document.Ready;
        if (!document.HasValue) throw document.Error!;
        ```

        `.gltf` and `.glb` are selected by the step's extensions. A successful handle contains CPU objects only. Inspect `Scenes`, `Nodes`, and `Meshes` before deciding whether to upload one primitive or build a complete runtime scene.

        For direct non-Resource tooling, `GltfParser`, decoder, validator, and converter form the lower-level import path. Prefer `GltfResourceStep` when URI dependencies, caching, reload, and ownership matter.
        """);

    [Story("Learn/Resources/Gltf/ExternalBuffersImagesAndUris", Order = 17, Toc = true)]
    public static StoryResult ExternalBuffersImagesAndUris(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/ExternalBuffersImagesAndUris", $$"""
        # External buffers, images, and URIs

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/ExternalBuffersImagesAndUris", "Intermediate", "File / HTTP / workspace", "Resources dependency DAG", "Registration and loading")}}

        JSON glTF may reference sibling `.bin` and image files. Resolve each reference against the document URI with `ResourceUri.Resolve`; do not combine raw filesystem paths inside the importer. This preserves `file`, `http`, `https`, and `workspace` schemes plus normalized relative segments.

        `GltfResourceStep` loads external bytes through `LoadContext.Load<byte[]>()`. Each returned handle becomes a dependency edge, so the same external buffer is shared and its update can reload the document. Data URIs and GLB buffer chunks are decoded from the container and need no external Source.

        ```text
        scene.gltf node
          ├─ depends on geometry.bin byte[] node
          └─ depends on albedo.png byte[] node
        ```

        The Source for every referenced scheme must be registered before the load. HTTP relative references retain the authority of the base document URI.
        """);

    [Story("Learn/Resources/Gltf/ValidationAndDiagnostics", Order = 18, Toc = true)]
    public static StoryResult ValidationAndDiagnostics(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/ValidationAndDiagnostics", $$"""
        # Validation and diagnostics

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/ValidationAndDiagnostics", "Intermediate", "Import tools / CI", "GltfValidator + decoder", "External buffers and images")}}

        Validation happens before unsafe accessor reads become asset arrays. Check buffer-view ranges, accessor component and element types, byte offsets/strides, sparse data, index ranges, image payloads, and referenced indices. Diagnostics should include the failing semantic or index rather than only a generic parse failure.

        Treat importer failure as a resource failure: the initial handle becomes `Failed`; a reload failure keeps the previous good `AssetDocument` and records `LastReloadError`. Callers should display diagnostics without discarding a scene that is still valid.

        For CI, load representative `.gltf` and `.glb` fixtures headlessly and assert document counts plus malformed-accessor errors. Rendering tests are complementary; they should not be the first place malformed binary layout is detected.
        """);

    [Story("Learn/Resources/Gltf/SceneRuntime", Order = 19, Toc = true)]
    public static StoryResult SceneRuntime(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/SceneRuntime", $$"""
        # glTF scene runtime

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/SceneRuntime", "Intermediate", "ECS / GPU runtime", "AssetRuntime", "Validation and diagnostics")}}

        `SceneBuilder.Build(world, document, device)` expands document nodes into ECS entities and creates `SceneAssets` GPU state. The Resources equivalent registers `SceneAssetsResourceStep` for `AssetDocument → SceneAssets`. This is separate from uploading an individual `AssetMesh` through `AssetGpuRegistry`.

        `SceneAssets.NodeEntities` maps CPU nodes to entities. Run `TransformPropagateSystem` after local transforms change. `SceneRenderExtractor` or `DrawableCollector` then writes instance data and supplies imported GPU buffers to the RenderGraph.

        ```text
        AssetDocument → SceneBuilder → entity hierarchy + SceneAssets
                                      → transform propagation
                                      → instance/material/primitive extraction
                                      → draw pass
        ```

        Keep `SceneAssets` alive through extraction and draw submission. It owns GPU state and mappings used by runtime systems.
        """);

    [Story("Learn/Resources/Gltf/AnimationSkinningAndMorph", Order = 20, Toc = true)]
    public static StoryResult AnimationSkinningAndMorph(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/AnimationSkinningAndMorph", $$"""
        # Animation, skinning, and morph targets

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/AnimationSkinningAndMorph", "Intermediate", "Game loop / ECS / GPU", "AssetRuntime scene shaders", "Scene runtime")}}

        Per frame, sample animation channels, propagate transforms, calculate skin joints, update morph/instance buffers, and only then extract draw data.

        ```text
        1. SceneAnimationPlayer.Sample(time)
        2. TransformPropagateSystem.Run(world)
        3. SkinningSystem.Run(world, sceneAssets)
        4. flush joint, morph-weight, and instance RenderBuffers
        5. extract and render
        ```

        Translation and scale use linear or step interpolation; rotation uses quaternion interpolation or step. Weight channels update morph weights. The current sampler does not perform full glTF cubic-spline tangent evaluation.

        Joint order matches inverse-bind-matrix order, and vertex `Joints0` values index that list. Morph buffers store targets by target then vertex; shaders add weighted deltas. Select the 56-byte skinned vertex shader for skin data and the morph variant for morph buffers—there is no assumption that one universal shader combines every feature.
        """);

    [Story("Learn/Resources/Gltf/ReloadAndLifetime", Order = 21, Toc = true)]
    public static StoryResult ReloadAndLifetime(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/ReloadAndLifetime", $$"""
        # glTF reload and lifetime

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/ReloadAndLifetime", "Intermediate", "Editor / Game loop", "Resources + AssetRuntime", "Animation, skinning, and morph")}}

        Enable `Watch()` before loading. Changes to the root document or any external buffer/image byte node reload the dependent `AssetDocument`; a `SceneAssets` resource built from it is then re-created through the dependency DAG.

        Replacement is published at `ResourceSystem.Pump()`. Keep using the last good scene when import fails, expose `LastReloadError`, and swap only after a complete new document/runtime value succeeds. Owned old values are deferred for disposal; GPU-backed values require the installed idle hook before destruction.

        Safe teardown order is: stop extraction/draw use, dispose runtime handles/scopes, pump deferred disposal, dispose GPU asset installation/runtime owners, then dispose the device. Holding the CPU document does not keep `SceneAssets` alive, and holding `SceneAssets` does not replace an explicit application owner for the device.
        """);
}
