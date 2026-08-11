namespace Luxel.Assets;

/// <summary>AssetDocument 内で安定した node index。</summary>
public readonly record struct AssetNodeIndex(int Value);
/// <summary>AssetDocument 内で安定した mesh index。</summary>
public readonly record struct AssetMeshIndex(int Value);
/// <summary>AssetDocument 内で安定した primitive index。mesh をまたいで document 内一意。</summary>
public readonly record struct AssetPrimitiveIndex(int Value);
/// <summary>AssetDocument 内で安定した material index。</summary>
public readonly record struct AssetMaterialIndex(int Value);
/// <summary>AssetDocument 内で安定した texture index。</summary>
public readonly record struct AssetTextureIndex(int Value);
/// <summary>AssetDocument 内で安定した sampler index。</summary>
public readonly record struct AssetSamplerIndex(int Value);
/// <summary>AssetDocument 内で安定した skin index。</summary>
public readonly record struct AssetSkinIndex(int Value);
/// <summary>AssetDocument 内で安定した animation index。</summary>
public readonly record struct AssetAnimationIndex(int Value);
/// <summary>AssetDocument 内で安定した camera index。</summary>
public readonly record struct AssetCameraIndex(int Value);
/// <summary>AssetDocument 内で安定した light index。</summary>
public readonly record struct AssetLightIndex(int Value);
/// <summary>AssetDocument 内で安定した scene index。</summary>
public readonly record struct AssetSceneIndex(int Value);

/// <summary>
/// document identity と typed index の組。異なる document の同じ数値 index を明確に区別する。
/// </summary>
public readonly record struct AssetDocumentHandle<TIndex>(AssetDocument Document, TIndex Index)
    where TIndex : struct;

/// <summary>
/// <see cref="AssetDocument"/> の direct-reference DOM に安定 index を付与するテーブル。
/// 初めて観測した順に index を払い出し、その後 List が並べ替えられても既存 index は変わらない。
/// </summary>
public sealed class AssetDocumentIndexTable
{
    private readonly AssetDocument _document;
    private readonly object _gate = new();
    private readonly StableMap<AssetNode> _nodes = new();
    private readonly StableMap<AssetMesh> _meshes = new();
    private readonly StableMap<AssetPrimitive> _primitives = new();
    private readonly StableMap<AssetMaterial> _materials = new();
    private readonly StableMap<AssetTexture> _textures = new();
    private readonly StableMap<AssetSampler> _samplers = new();
    private readonly StableMap<AssetSkin> _skins = new();
    private readonly StableMap<AssetAnimation> _animations = new();
    private readonly StableMap<AssetCamera> _cameras = new();
    private readonly StableMap<AssetLight> _lights = new();
    private readonly StableMap<AssetScene> _scenes = new();
    private readonly Dictionary<AssetMesh, List<AssetPrimitiveIndex>> _meshPrimitives =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetPrimitive, AssetMeshIndex> _primitiveMeshes =
        new(ReferenceEqualityComparer.Instance);

    internal AssetDocumentIndexTable(AssetDocument document) => _document = document;

    public AssetNodeIndex GetIndex(AssetNode value) { lock (_gate) { Synchronize(); return new(_nodes.GetRequired(value)); } }
    public AssetMeshIndex GetIndex(AssetMesh value) { lock (_gate) { Synchronize(); return new(_meshes.GetRequired(value)); } }
    public AssetPrimitiveIndex GetIndex(AssetPrimitive value) { lock (_gate) { Synchronize(); return new(_primitives.GetRequired(value)); } }
    public AssetMaterialIndex GetIndex(AssetMaterial value) { lock (_gate) { Synchronize(); return new(_materials.GetRequired(value)); } }
    public AssetTextureIndex GetIndex(AssetTexture value) { lock (_gate) { Synchronize(); return new(_textures.GetRequired(value)); } }
    public AssetSamplerIndex GetIndex(AssetSampler value) { lock (_gate) { Synchronize(); return new(_samplers.GetRequired(value)); } }
    public AssetSkinIndex GetIndex(AssetSkin value) { lock (_gate) { Synchronize(); return new(_skins.GetRequired(value)); } }
    public AssetAnimationIndex GetIndex(AssetAnimation value) { lock (_gate) { Synchronize(); return new(_animations.GetRequired(value)); } }
    public AssetCameraIndex GetIndex(AssetCamera value) { lock (_gate) { Synchronize(); return new(_cameras.GetRequired(value)); } }
    public AssetLightIndex GetIndex(AssetLight value) { lock (_gate) { Synchronize(); return new(_lights.GetRequired(value)); } }
    public AssetSceneIndex GetIndex(AssetScene value) { lock (_gate) { Synchronize(); return new(_scenes.GetRequired(value)); } }

    public bool TryGetIndex(AssetNode value, out AssetNodeIndex index) { bool found = TryGet(_nodes, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetMesh value, out AssetMeshIndex index) { bool found = TryGet(_meshes, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetPrimitive value, out AssetPrimitiveIndex index) { bool found = TryGet(_primitives, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetMaterial value, out AssetMaterialIndex index) { bool found = TryGet(_materials, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetTexture value, out AssetTextureIndex index) { bool found = TryGet(_textures, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetSampler value, out AssetSamplerIndex index) { bool found = TryGet(_samplers, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetSkin value, out AssetSkinIndex index) { bool found = TryGet(_skins, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetAnimation value, out AssetAnimationIndex index) { bool found = TryGet(_animations, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetCamera value, out AssetCameraIndex index) { bool found = TryGet(_cameras, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetLight value, out AssetLightIndex index) { bool found = TryGet(_lights, value, out int raw); index = new(raw); return found; }
    public bool TryGetIndex(AssetScene value, out AssetSceneIndex index) { bool found = TryGet(_scenes, value, out int raw); index = new(raw); return found; }

    public AssetNode Resolve(AssetNodeIndex index) => Resolve(_nodes, index.Value);
    public AssetMesh Resolve(AssetMeshIndex index) => Resolve(_meshes, index.Value);
    public AssetPrimitive Resolve(AssetPrimitiveIndex index) => Resolve(_primitives, index.Value);
    public AssetMaterial Resolve(AssetMaterialIndex index) => Resolve(_materials, index.Value);
    public AssetTexture Resolve(AssetTextureIndex index) => Resolve(_textures, index.Value);
    public AssetSampler Resolve(AssetSamplerIndex index) => Resolve(_samplers, index.Value);
    public AssetSkin Resolve(AssetSkinIndex index) => Resolve(_skins, index.Value);
    public AssetAnimation Resolve(AssetAnimationIndex index) => Resolve(_animations, index.Value);
    public AssetCamera Resolve(AssetCameraIndex index) => Resolve(_cameras, index.Value);
    public AssetLight Resolve(AssetLightIndex index) => Resolve(_lights, index.Value);
    public AssetScene Resolve(AssetSceneIndex index) => Resolve(_scenes, index.Value);

    /// <summary>mesh 内 primitive の初回観測順。返却順と index は List の並べ替え後も変化しない。</summary>
    public IReadOnlyList<AssetPrimitiveIndex> GetPrimitiveIndices(AssetMeshIndex mesh)
    {
        lock (_gate)
        {
            Synchronize();
            AssetMesh value = _meshes.Resolve(mesh.Value);
            return _meshPrimitives[value].ToArray();
        }
    }

    public AssetMeshIndex GetMeshIndex(AssetPrimitiveIndex primitive)
    {
        lock (_gate)
        {
            Synchronize();
            AssetPrimitive value = _primitives.Resolve(primitive.Value);
            return _primitiveMeshes[value];
        }
    }

    private bool TryGet<T>(StableMap<T> map, T value, out int index) where T : class
    {
        lock (_gate)
        {
            Synchronize();
            return map.TryGet(value, out index);
        }
    }

    private T Resolve<T>(StableMap<T> map, int index) where T : class
    {
        lock (_gate) { Synchronize(); return map.Resolve(index); }
    }

    private void Synchronize()
    {
        _nodes.AddRange(_document.Nodes);
        _materials.AddRange(_document.Materials);
        _textures.AddRange(_document.Textures);
        _samplers.AddRange(_document.Samplers);
        _skins.AddRange(_document.Skins);
        _animations.AddRange(_document.Animations);
        _cameras.AddRange(_document.Cameras);
        _lights.AddRange(_document.Lights);
        _scenes.AddRange(_document.Scenes);

        foreach (AssetMesh mesh in _document.Meshes)
        {
            var meshIndex = new AssetMeshIndex(_meshes.Add(mesh));
            if (!_meshPrimitives.TryGetValue(mesh, out List<AssetPrimitiveIndex>? ordered))
            {
                ordered = new();
                _meshPrimitives.Add(mesh, ordered);
            }
            foreach (AssetPrimitive primitive in mesh.Primitives)
            {
                int raw = _primitives.Add(primitive);
                if (!_primitiveMeshes.TryGetValue(primitive, out AssetMeshIndex owner))
                {
                    _primitiveMeshes.Add(primitive, meshIndex);
                    ordered.Add(new AssetPrimitiveIndex(raw));
                }
                else if (owner != meshIndex)
                {
                    throw new InvalidOperationException("An AssetPrimitive cannot belong to multiple meshes in one AssetDocument.");
                }
            }
        }
    }

    private sealed class StableMap<T> where T : class
    {
        private readonly Dictionary<T, int> _indices = new(ReferenceEqualityComparer.Instance);
        private readonly List<T> _values = new();

        public int Add(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_indices.TryGetValue(value, out int index)) return index;
            index = _values.Count;
            _indices.Add(value, index);
            _values.Add(value);
            return index;
        }

        public void AddRange(IEnumerable<T> values)
        {
            foreach (T value in values) Add(value);
        }

        public bool TryGet(T value, out int index) => _indices.TryGetValue(value, out index);

        public int GetRequired(T value) => _indices.TryGetValue(value, out int index)
            ? index
            : throw new ArgumentException("The asset does not belong to this AssetDocument.", nameof(value));

        public T Resolve(int index) => index >= 0 && index < _values.Count
            ? _values[index]
            : throw new ArgumentOutOfRangeException(nameof(index), index, "Asset index is outside this document.");
    }
}
