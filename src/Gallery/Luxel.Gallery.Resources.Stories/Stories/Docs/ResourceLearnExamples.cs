namespace Luxel.Gallery.Stories;

/// <summary>Canonical Learn-to-example relationship. Learn pages use this mapping to emit structured story embeds.</summary>
internal static class ResourceLearnExamples
{
    internal static readonly IReadOnlyDictionary<string, string[]> Routes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Learn/Resources/Overview"] = ["Examples/Resources/HelloTextAsset", "Examples/Resources/Gltf/BoxDocumentLoad"],
            ["Learn/Resources/LoadingAndHandles"] = ["Examples/Resources/HelloTextAsset", "Examples/Resources/HotReloadRecovery"],
            ["Learn/Resources/SourcesAndUris"] = ["Examples/Resources/CustomPackageSource", "Examples/Resources/BrowserHttpAssets", "Examples/Resources/Gltf/ExternalBufferTrace"],
            ["Learn/Resources/Steps"] = ["Examples/Resources/PlayerStatsPipeline", "Examples/Resources/ExtensionSelection"],
            ["Learn/Resources/RegistrationAndComposition"] = ["Examples/Resources/PlayerStatsPipeline", "Examples/Resources/ExtensionSelection", "Examples/Resources/BrowserHttpAssets"],
            ["Learn/Resources/PipelinesAndDag"] = ["Examples/Resources/SharedDependencyGraph", "Examples/Resources/Gltf/ExternalBufferTrace"],
            ["Learn/Resources/ScopesAndOwnership"] = ["Examples/Resources/ScopedRuntimeValues", "Examples/Resources/Assets/GpuAssetRegistry"],
            ["Learn/Resources/ReloadAndLifetime"] = ["Examples/Resources/HotReloadRecovery", "Examples/Resources/Gltf/ExternalDependencyReload"],

            ["Learn/Resources/Assets/Overview"] = ["Examples/Resources/Assets/DocumentInspector", "Examples/Resources/Assets/GpuAssetRegistry", "Examples/Resources/Gltf/BoxScene"],
            ["Learn/Resources/Assets/DocumentAndSceneGraph"] = ["Examples/Resources/Assets/DocumentInspector", "Examples/Resources/Assets/AnimatedSceneGraph"],
            ["Learn/Resources/Assets/MeshesAndPrimitives"] = ["Examples/Resources/Assets/MeshPrimitiveInspector", "Examples/Resources/Gltf/MorphWeights"],
            ["Learn/Resources/Assets/MaterialsTexturesAndSamplers"] = ["Examples/Resources/Assets/MaterialTextureInspector", "Examples/Resources/Gltf/BoxScene"],
            ["Learn/Resources/Assets/AnimationSkinCameraAndLight"] = ["Examples/Resources/Assets/AnimatedSceneGraph", "Examples/Resources/Gltf/AnimatedBox", "Examples/Resources/Gltf/RiggedSimpleSkinning", "Examples/Resources/Gltf/MorphWeights"],
            ["Learn/Resources/Assets/LoadingAndGpu"] = ["Examples/Resources/Assets/GpuAssetRegistry", "Examples/Resources/Gltf/BoxScene"],
            ["Learn/Resources/Assets/ShaderAbi"] = ["Examples/Resources/Assets/ShaderBufferInspector", "Examples/Resources/Gltf/RiggedSimpleSkinning", "Examples/Resources/Gltf/MorphWeights"],

            ["Learn/Resources/Gltf/Overview"] = ["Examples/Resources/Gltf/BoxDocumentLoad", "Examples/Resources/Gltf/BoxScene"],
            ["Learn/Resources/Gltf/RegistrationAndLoading"] = ["Examples/Resources/Gltf/BoxDocumentLoad", "Examples/Resources/BrowserHttpAssets"],
            ["Learn/Resources/Gltf/ExternalBuffersImagesAndUris"] = ["Examples/Resources/Gltf/ExternalBufferTrace", "Examples/Resources/Gltf/ExternalDependencyReload"],
            ["Learn/Resources/Gltf/ValidationAndDiagnostics"] = ["Examples/Resources/Gltf/MalformedAccessorDiagnostics", "Examples/Resources/Gltf/BoxDocumentLoad"],
            ["Learn/Resources/Gltf/SceneRuntime"] = ["Examples/Resources/Gltf/BoxScene", "Examples/Resources/Assets/GpuAssetRegistry"],
            ["Learn/Resources/Gltf/AnimationSkinningAndMorph"] = ["Examples/Resources/Gltf/AnimatedBox", "Examples/Resources/Gltf/RiggedSimpleSkinning", "Examples/Resources/Gltf/MorphWeights"],
            ["Learn/Resources/Gltf/ReloadAndLifetime"] = ["Examples/Resources/Gltf/ExternalDependencyReload", "Examples/Resources/HotReloadRecovery"],
        };

    internal static StoryResult Attach(string learnRoute, StoryResult page)
    {
        if (!Routes.TryGetValue(learnRoute, out string[]? examples) || examples.Length == 0)
            throw new InvalidOperationException($"Resource Learn examples are not registered: {learnRoute}");

        const string primaryEmbed = "```luxel-story\n0\n```";
        string markdown = InsertPrimaryExample(page.Markdown, primaryEmbed);
        return StoryResult.FromMarkdown(markdown, StoryReference.To(examples[0]));
    }

    private static string InsertPrimaryExample(string markdown, string embed)
    {
        int firstHeading = markdown.IndexOf("\n## ", StringComparison.Ordinal);
        int insertion = firstHeading < 0 ? -1 : markdown.IndexOf("\n## ", firstHeading + 1, StringComparison.Ordinal);
        if (insertion < 0)
        {
            string[] paragraphs = markdown.Split(["\n\n"], StringSplitOptions.None);
            int concept = Array.FindIndex(paragraphs, paragraph =>
            {
                string text = paragraph.TrimStart();
                return text.Length > 0 && !text.StartsWith('#') && !text.StartsWith('<')
                    && !text.StartsWith("| ") && !text.StartsWith("```", StringComparison.Ordinal);
            });
            if (concept >= 0)
            {
                var result = paragraphs.ToList();
                result.Insert(concept + 1, embed);
                return string.Join("\n\n", result);
            }
            return markdown + "\n\n" + embed;
        }
        return markdown.Insert(insertion, "\n\n" + embed + "\n");
    }
}
