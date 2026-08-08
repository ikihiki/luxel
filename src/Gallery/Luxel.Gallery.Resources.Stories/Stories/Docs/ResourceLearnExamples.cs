namespace Luxel.Gallery.Stories;

/// <summary>Canonical Learn-to-example relationship. Learn pages use this mapping to emit structured story embeds.</summary>
internal static class ResourceLearnExamples
{
    internal static readonly IReadOnlyDictionary<string, string[]> Routes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Learn/Resources/Overview"] = ["Examples/Resources/HelloTextAsset", "Examples/Resources/PlayerStatsPipeline"],
            ["Learn/Resources/LoadingAndHandles"] = ["Examples/Resources/HelloTextAsset", "Examples/Resources/HotReloadRecovery"],
            ["Learn/Resources/SourcesAndUris"] = ["Examples/Resources/CustomPackageSource", "Examples/Resources/BrowserHttpAssets"],
            ["Learn/Resources/Steps"] = ["Examples/Resources/PlayerStatsPipeline", "Examples/Resources/ExtensionSelection"],
            ["Learn/Resources/RegistrationAndComposition"] = ["Examples/Resources/PlayerStatsPipeline", "Examples/Resources/ExtensionSelection"],
            ["Learn/Resources/PipelinesAndDag"] = ["Examples/Resources/SharedDependencyGraph", "Examples/Resources/PlayerStatsPipeline"],
            ["Learn/Resources/ScopesAndOwnership"] = ["Examples/Resources/ScopedRuntimeValues", "Examples/Resources/SharedDependencyGraph"],
            ["Learn/Resources/ReloadAndLifetime"] = ["Examples/Resources/HotReloadRecovery", "Examples/Resources/SharedDependencyGraph"],

            ["Learn/Resources/Assets/Overview"] = ["Examples/Resources/Assets/DocumentInspector", "Examples/Resources/Assets/GpuAssetRegistry"],
            ["Learn/Resources/Assets/DocumentAndSceneGraph"] = ["Examples/Resources/Assets/DocumentInspector", "Examples/Resources/Assets/AnimatedSceneGraph"],
            ["Learn/Resources/Assets/MeshesAndPrimitives"] = ["Examples/Resources/Assets/MeshPrimitiveInspector", "Examples/Resources/Assets/ShaderBufferInspector"],
            ["Learn/Resources/Assets/MaterialsTexturesAndSamplers"] = ["Examples/Resources/Assets/MaterialTextureInspector", "Examples/Resources/Assets/GpuAssetRegistry"],
            ["Learn/Resources/Assets/AnimationSkinCameraAndLight"] = ["Examples/Resources/Assets/AnimatedSceneGraph", "Examples/Resources/Gltf/RiggedSimpleSkinning"],
            ["Learn/Resources/Assets/LoadingAndGpu"] = ["Examples/Resources/Assets/GpuAssetRegistry", "Examples/Resources/Gltf/BoxDocumentLoad"],
            ["Learn/Resources/Assets/ShaderAbi"] = ["Examples/Resources/Assets/ShaderBufferInspector", "Examples/Resources/Gltf/MorphWeights"],

            ["Learn/Resources/Gltf/Overview"] = ["Examples/Resources/Gltf/BoxDocumentLoad", "Examples/Resources/Gltf/BoxScene"],
            ["Learn/Resources/Gltf/RegistrationAndLoading"] = ["Examples/Resources/Gltf/BoxDocumentLoad", "Examples/Resources/Gltf/ExternalBufferTrace"],
            ["Learn/Resources/Gltf/ExternalBuffersImagesAndUris"] = ["Examples/Resources/Gltf/ExternalBufferTrace", "Examples/Resources/Gltf/ExternalDependencyReload"],
            ["Learn/Resources/Gltf/ValidationAndDiagnostics"] = ["Examples/Resources/Gltf/MalformedAccessorDiagnostics", "Examples/Resources/Gltf/BoxDocumentLoad"],
            ["Learn/Resources/Gltf/SceneRuntime"] = ["Examples/Resources/Gltf/BoxScene", "Examples/Resources/Gltf/AnimatedBox"],
            ["Learn/Resources/Gltf/AnimationSkinningAndMorph"] = ["Examples/Resources/Gltf/AnimatedBox", "Examples/Resources/Gltf/RiggedSimpleSkinning", "Examples/Resources/Gltf/MorphWeights"],
            ["Learn/Resources/Gltf/ReloadAndLifetime"] = ["Examples/Resources/Gltf/ExternalDependencyReload", "Examples/Resources/Gltf/BoxScene"],
        };

    internal static StoryResult Attach(string learnRoute, StoryResult page)
    {
        if (!Routes.TryGetValue(learnRoute, out string[]? examples) || examples.Length == 0)
            throw new InvalidOperationException($"Resource Learn examples are not registered: {learnRoute}");

        string markdown = page.Markdown + "\n\n## Related runnable examples\n\n";
        for (int i = 0; i < examples.Length; i++) markdown += $"```luxel-story\n{i}\n```\n";
        return StoryResult.FromMarkdown(markdown, examples.Select(example => StoryReference.To(example)).ToArray());
    }
}
