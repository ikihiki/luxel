namespace Luxel.Assets;

/// <summary>
/// 一括ロード (glTF/.glb/MMD 等) の結果を保持するコレクション。
/// 各 asset は <see cref="AssetDocument"/> 抜きで単体で使える (direct reference で連結された DOM)。
/// このコンテナ自体は「同時にロードされた asset の入れ物」以上の意味を持たない。
/// </summary>
public sealed class AssetDocument
{
    private readonly Lazy<AssetDocumentIndexTable> _indices;

    public AssetDocument()
    {
        _indices = new Lazy<AssetDocumentIndexTable>(() => new AssetDocumentIndexTable(this));
    }

    /// <summary>
    /// direct reference と安定 typed index を相互変換する document 固有テーブル。
    /// </summary>
    public AssetDocumentIndexTable Indices => _indices.Value;

    public AssetDocumentHandle<AssetNodeIndex> GetHandle(AssetNode value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetMeshIndex> GetHandle(AssetMesh value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetPrimitiveIndex> GetHandle(AssetPrimitive value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetMaterialIndex> GetHandle(AssetMaterial value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetTextureIndex> GetHandle(AssetTexture value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetSamplerIndex> GetHandle(AssetSampler value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetSkinIndex> GetHandle(AssetSkin value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetAnimationIndex> GetHandle(AssetAnimation value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetCameraIndex> GetHandle(AssetCamera value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetLightIndex> GetHandle(AssetLight value) => new(this, Indices.GetIndex(value));
    public AssetDocumentHandle<AssetSceneIndex> GetHandle(AssetScene value) => new(this, Indices.GetIndex(value));

    /// <summary>ロードされた全 mesh。</summary>
    public List<AssetMesh> Meshes { get; } = new();
    /// <summary>ロードされた全マテリアル。</summary>
    public List<AssetMaterial> Materials { get; } = new();
    /// <summary>ロードされた全テクスチャ。</summary>
    public List<AssetTexture> Textures { get; } = new();
    /// <summary>ロードされた全サンプラ。</summary>
    public List<AssetSampler> Samplers { get; } = new();
    /// <summary>ロードされた全スキン。</summary>
    public List<AssetSkin> Skins { get; } = new();
    /// <summary>ロードされた全アニメーション。</summary>
    public List<AssetAnimation> Animations { get; } = new();
    /// <summary>ロードされた全カメラ。</summary>
    public List<AssetCamera> Cameras { get; } = new();
    /// <summary>ロードされた全光源。</summary>
    public List<AssetLight> Lights { get; } = new();
    /// <summary>ロードされた全 node (scene graph 所属の有無を問わないフラットな一覧)。</summary>
    public List<AssetNode> Nodes { get; } = new();
    /// <summary>ロードされた全 scene (glTF の scenes[] 相当)。</summary>
    public List<AssetScene> Scenes { get; } = new();

    /// <summary>既定で表示する scene (glTF の scene 相当、null 可)。</summary>
    public AssetScene? DefaultScene { get; set; }

    // Metadata
    /// <summary>ロード元の形式識別子 ("gltf" / "glb" / "memory" 等)。</summary>
    public string SourceFormat { get; set; } = "memory";
    /// <summary>生成ツール名 (glTF の asset.generator)。</summary>
    public string? Generator { get; set; }
    /// <summary>著作権表記 (glTF の asset.copyright)。</summary>
    public string? Copyright { get; set; }
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
    /// <summary>document レベルの拡張データ (glTF の extensions)。</summary>
    public Dictionary<string, object>? Extensions { get; set; }
    /// <summary>使用している拡張名の一覧 (glTF の extensionsUsed)。</summary>
    public HashSet<string>? ExtensionsUsed { get; set; }
    /// <summary>必須拡張名の一覧 (glTF の extensionsRequired)。</summary>
    public HashSet<string>? ExtensionsRequired { get; set; }
}

/// <summary>
/// Scene graph の root。<see cref="Roots"/> は複数持てる (glTF 準拠、複数 disconnected tree を許容)。
/// AssetDocument は複数の <see cref="AssetScene"/> を保持できる (glTF の scenes[] 相当)。
/// </summary>
public sealed class AssetScene
{
    /// <summary>名前 (glTF の name)。</summary>
    public string? Name { get; set; }
    /// <summary>root node の一覧 (直接参照、複数可)。</summary>
    public List<AssetNode> Roots { get; } = new();
    /// <summary>アプリ固有の追加データ (glTF の extras)。</summary>
    public Dictionary<string, object>? Extras { get; set; }
}
