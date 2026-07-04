namespace Luxel.Assets;

/// <summary>
/// 一括ロード (glTF/.glb/MMD 等) の結果を保持するコレクション。
/// 各 asset は <see cref="AssetDocument"/> 抜きで単体で使える (direct reference で連結された DOM)。
/// このコンテナ自体は「同時にロードされた asset の入れ物」以上の意味を持たない。
/// </summary>
public sealed class AssetDocument
{
    public List<AssetMesh> Meshes { get; } = new();
    public List<AssetMaterial> Materials { get; } = new();
    public List<AssetTexture> Textures { get; } = new();
    public List<AssetSampler> Samplers { get; } = new();
    public List<AssetSkin> Skins { get; } = new();
    public List<AssetAnimation> Animations { get; } = new();
    public List<AssetCamera> Cameras { get; } = new();
    public List<AssetLight> Lights { get; } = new();
    public List<AssetNode> Nodes { get; } = new();
    public List<AssetScene> Scenes { get; } = new();

    public AssetScene? DefaultScene { get; set; }

    // Metadata
    public string SourceFormat { get; set; } = "memory";
    public string? Generator { get; set; }
    public string? Copyright { get; set; }
    public Dictionary<string, object>? Extras { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public HashSet<string>? ExtensionsUsed { get; set; }
    public HashSet<string>? ExtensionsRequired { get; set; }
}

/// <summary>
/// Scene graph の root。<see cref="Roots"/> は複数持てる (glTF 準拠、複数 disconnected tree を許容)。
/// AssetDocument は複数の <see cref="AssetScene"/> を保持できる (glTF の scenes[] 相当)。
/// </summary>
public sealed class AssetScene
{
    public string? Name { get; set; }
    public List<AssetNode> Roots { get; } = new();
    public Dictionary<string, object>? Extras { get; set; }
}
