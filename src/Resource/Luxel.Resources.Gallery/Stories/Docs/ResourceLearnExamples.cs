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
            ["Learn/Resources/Assets/Overview"] = ["Examples/Resources/Assets/DocumentInspector"],
            ["Learn/Resources/Assets/DocumentAndSceneGraph"] = ["Examples/Resources/Assets/DocumentInspector"],
            ["Learn/Resources/Assets/MeshesAndPrimitives"] = ["Examples/Resources/Assets/MeshPrimitiveInspector"],
            ["Learn/Resources/Assets/MaterialsTexturesAndSamplers"] = ["Examples/Resources/Assets/MaterialTextureInspector"],
            ["Learn/Resources/Assets/AnimationSkinCameraAndLight"] = ["Examples/Resources/Assets/AnimatedSceneGraph"],
            ["Learn/Resources/Assets/LoadingAndGpu"] = ["Examples/Resources/Assets/GpuManagerInstallation"],
            ["Learn/Resources/Assets/CustomGpuResourceTypes"] = ["Examples/Resources/Assets/CustomGpuParticleBuffers"],
            ["Learn/Resources/Assets/GpuMemoryAndIndexes"] = ["Examples/Resources/Assets/GpuIndexRecycling", "Examples/Resources/Assets/GpuCompaction"],
            ["Learn/Resources/Assets/DeviceLossAndRecovery"] = ["Examples/Resources/Assets/DeviceLostRecovery"],
            ["Learn/Resources/Assets/ShaderAbi"] = ["Examples/Resources/Assets/ShaderBufferInspector", "Examples/Resources/Assets/CustomGpuStructRetirement"],
            ["Learn/Resources/Gltf/Overview"] = ["Examples/Resources/Gltf/BoxDocumentLoad"],
            ["Learn/Resources/Gltf/RegistrationAndLoading"] = ["Examples/Resources/Gltf/BoxDocumentLoad"],
            ["Learn/Resources/Gltf/ExternalBuffersImagesAndUris"] = ["Examples/Resources/Gltf/ExternalBufferTrace"],
            ["Learn/Resources/Gltf/ValidationAndDiagnostics"] = ["Examples/Resources/Gltf/MalformedAccessorDiagnostics"],
            ["Learn/Resources/Gltf/SceneRuntime"] = ["Examples/Resources/Gltf/BoxScene"],
            ["Learn/Resources/Gltf/AnimationSkinningAndMorph"] = ["Examples/Resources/Gltf/AnimatedBox", "Examples/Resources/Gltf/RiggedSimpleSkinning", "Examples/Resources/Gltf/MorphWeights"],
            ["Learn/Resources/Gltf/ReloadAndLifetime"] = ["Examples/Resources/Gltf/ExternalDependencyReload"],
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
