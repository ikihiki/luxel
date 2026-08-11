using Luxel.Controls;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>Resources学習コースの順序と前後ナビゲーションを管理する唯一の定義。</summary>
internal static class ResourceCourseCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Learn/Resources/Overview"] = "Resources学習ガイド",
        ["Learn/Resources/BuilderAndComposition"] = "Builderとcomposition",
        ["Learn/Resources/ExecutionDomains"] = "Execution domain",
        ["Learn/Resources/ResourceManagers"] = "Resource manager",
        ["Learn/Resources/IdentityAndHandles"] = "Identityとhandle",
        ["Learn/Resources/SourcesAndSteps"] = "SourceとStep",
        ["Learn/Resources/DependenciesAndPublication"] = "依存関係とpublication",
        ["Learn/Resources/OwnershipAndRetirement"] = "Ownershipとretirement",
        ["Learn/Resources/ReloadAndRecovery"] = "Reloadとrecovery",
        ["Learn/Resources/DiagnosticsAndMetrics"] = "Diagnosticsとmetrics",
        ["Learn/Resources/WasmExecution"] = "WASM execution",
        ["Learn/Resources/Assets/Overview"] = "アセットの概要",
        ["Learn/Resources/Assets/DocumentAndSceneGraph"] = "AssetDocumentとシーングラフ",
        ["Learn/Resources/Assets/MeshesAndPrimitives"] = "メッシュとプリミティブ",
        ["Learn/Resources/Assets/MaterialsTexturesAndSamplers"] = "マテリアル、テクスチャ、サンプラー",
        ["Learn/Resources/Assets/AnimationSkinCameraAndLight"] = "アニメーション、スキン、カメラ、ライト",
        ["Learn/Resources/Assets/LoadingAndGpu"] = "アセットとGPU manager",
        ["Learn/Resources/Assets/CustomGpuResourceTypes"] = "Custom GPU resource types",
        ["Learn/Resources/Assets/GpuMemoryAndIndexes"] = "GPU memoryとindex",
        ["Learn/Resources/Assets/DeviceLossAndRecovery"] = "Device lossとrecovery",
        ["Learn/Resources/Assets/ShaderAbi"] = "アセットのシェーダーABI",
        ["Learn/Resources/Gltf/Overview"] = "glTFの概要",
        ["Learn/Resources/Gltf/RegistrationAndLoading"] = "glTFの登録と読み込み",
        ["Learn/Resources/Gltf/ExternalBuffersImagesAndUris"] = "外部バッファ、画像、URI",
        ["Learn/Resources/Gltf/ValidationAndDiagnostics"] = "検証と診断",
        ["Learn/Resources/Gltf/SceneRuntime"] = "glTFシーンのランタイム",
        ["Learn/Resources/Gltf/AnimationSkinningAndMorph"] = "アニメーション、スキニング、モーフターゲット",
        ["Learn/Resources/Gltf/ReloadAndLifetime"] = "glTFの再読み込みと寿命",
    };

    internal static readonly string[] Routes =
    [
        "Learn/Resources/Overview",
        "Learn/Resources/BuilderAndComposition",
        "Learn/Resources/ExecutionDomains",
        "Learn/Resources/ResourceManagers",
        "Learn/Resources/IdentityAndHandles",
        "Learn/Resources/SourcesAndSteps",
        "Learn/Resources/DependenciesAndPublication",
        "Learn/Resources/OwnershipAndRetirement",
        "Learn/Resources/ReloadAndRecovery",
        "Learn/Resources/DiagnosticsAndMetrics",
        "Learn/Resources/WasmExecution",
        "Learn/Resources/Assets/Overview",
        "Learn/Resources/Assets/DocumentAndSceneGraph",
        "Learn/Resources/Assets/MeshesAndPrimitives",
        "Learn/Resources/Assets/MaterialsTexturesAndSamplers",
        "Learn/Resources/Assets/AnimationSkinCameraAndLight",
        "Learn/Resources/Assets/LoadingAndGpu",
        "Learn/Resources/Assets/CustomGpuResourceTypes",
        "Learn/Resources/Assets/GpuMemoryAndIndexes",
        "Learn/Resources/Assets/DeviceLossAndRecovery",
        "Learn/Resources/Assets/ShaderAbi",
        "Learn/Resources/Gltf/Overview",
        "Learn/Resources/Gltf/RegistrationAndLoading",
        "Learn/Resources/Gltf/ExternalBuffersImagesAndUris",
        "Learn/Resources/Gltf/ValidationAndDiagnostics",
        "Learn/Resources/Gltf/SceneRuntime",
        "Learn/Resources/Gltf/AnimationSkinningAndMorph",
        "Learn/Resources/Gltf/ReloadAndLifetime",
    ];

    internal static string Label(string route) => Labels.TryGetValue(route, out string? localized)
        ? localized
        : route[(route.LastIndexOf('/') + 1)..];

    internal static string LearningRouteMarkdown()
    {
        var lines = Routes.Skip(1).Select((route, index) =>
            $"{index + 1}. [{Label(route)}](story:{route})");
        return string.Join("\n", lines);
    }

    internal static (string? Previous, string? Next) Navigation(string path)
    {
        int index = Array.IndexOf(Routes, path);
        if (index < 0) throw new InvalidOperationException($"Resources course route is not registered: {path}");
        return (index > 0 ? Routes[index - 1] : null, index + 1 < Routes.Length ? Routes[index + 1] : null);
    }

    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        (string? previous, string? next) = Navigation(path);
        string navigation = previous is null && next is null ? "" : "\n\n"
            + (previous is null ? "" : $"**前へ:** [{Label(previous)}](story:{previous})")
            + (previous is not null && next is not null ? "　 " : "")
            + (next is null ? "" : $"**次:** [{Label(next)}](story:{next})");
        return new DocMarkdown($"**難易度:** {difficulty}　 **実行環境:** {environment}　 **バックエンド:** {backend}　 **前提知識:** {prerequisites}{navigation}");
    }
}
