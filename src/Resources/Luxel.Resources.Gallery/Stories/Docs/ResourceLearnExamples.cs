namespace Luxel.Resources.Gallery.Stories;

/// <summary>Canonical Learn-to-example relationship. Learn pages use this mapping to emit structured story embeds.</summary>
internal static class ResourceLearnExamples
{
    internal static readonly IReadOnlyDictionary<string, string[]> Routes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Learn/Resources/Overview"] = ["Examples/Resources/ReadyBuilder"],
            ["Learn/Resources/BuilderAndComposition"] = ["Examples/Resources/ReadyBuilder"],
            ["Learn/Resources/ExecutionDomains"] = ["Examples/Resources/CustomExecutionDomain", "Examples/Resources/SerializedCompilerDomain"],
            ["Learn/Resources/ResourceManagers"] = ["Examples/Resources/TypedManagerBinding"],
            ["Learn/Resources/IdentityAndHandles"] = ["Examples/Resources/SharedRequestIdentity"],
            ["Learn/Resources/SourcesAndSteps"] = ["Examples/Resources/CustomSourceAndStep"],
            ["Learn/Resources/DependenciesAndPublication"] = ["Examples/Resources/DependencyPublication"],
            ["Learn/Resources/OwnershipAndRetirement"] = ["Examples/Resources/ScopedRetirement"],
            ["Learn/Resources/ReloadAndRecovery"] = ["Examples/Resources/ReloadKeepsLastGood"],
            ["Learn/Resources/DiagnosticsAndMetrics"] = ["Examples/Resources/DomainAndManagerMetrics"],
            ["Learn/Resources/WasmExecution"] = ["Examples/Resources/WasmCooperativeScheduling"],
            ["Learn/Resources/Assets/Overview"] = ["Examples/Resources/DocumentInspector"],
            ["Learn/Resources/Assets/DocumentAndSceneGraph"] = ["Examples/Resources/DocumentInspector"],
            ["Learn/Resources/Assets/MeshesAndPrimitives"] = ["Examples/Resources/MeshPrimitiveInspector"],
            ["Learn/Resources/Assets/MaterialsTexturesAndSamplers"] = ["Examples/Resources/MaterialTextureInspector"],
            ["Learn/Resources/Assets/AnimationSkinCameraAndLight"] = ["Examples/Resources/AnimatedSceneGraph"],
            ["Learn/Resources/Assets/LoadingAndGpu"] = ["Examples/Resources/GpuManagerInstallation"],
            ["Learn/Resources/Assets/CustomGpuResourceTypes"] = ["Examples/Resources/CustomGpuParticleBuffers"],
            ["Learn/Resources/Assets/GpuMemoryAndIndexes"] = ["Examples/Resources/GpuIndexRecycling", "Examples/Resources/GpuCompaction"],
            ["Learn/Resources/Assets/DeviceLossAndRecovery"] = ["Examples/Resources/DeviceLostRecovery"],
            ["Learn/Resources/Assets/ShaderAbi"] = ["Examples/Resources/ShaderBufferInspector", "Examples/Resources/CustomGpuStructRetirement"],
            ["Learn/Resources/Gltf/Overview"] = ["Examples/Resources/BoxDocumentLoad"],
            ["Learn/Resources/Gltf/RegistrationAndLoading"] = ["Examples/Resources/BoxDocumentLoad"],
            ["Learn/Resources/Gltf/ExternalBuffersImagesAndUris"] = ["Examples/Resources/ExternalBufferTrace"],
            ["Learn/Resources/Gltf/ValidationAndDiagnostics"] = ["Examples/Resources/MalformedAccessorDiagnostics"],
            ["Learn/Resources/Gltf/SceneRuntime"] = ["Examples/Resources/Gltf/GltfBox"],
            ["Learn/Resources/Gltf/AnimationSkinningAndMorph"] = ["Examples/Resources/Gltf/GltfAnimated", "Examples/Resources/Gltf/GltfSkinned", "Examples/Resources/Gltf/GltfMorph"],
            ["Learn/Resources/Gltf/ReloadAndLifetime"] = ["Examples/Resources/ExternalDependencyReload"],
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
