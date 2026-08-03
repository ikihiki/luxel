using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// Asset* を GPU へ upload + cache する service。<see cref="ResourceSystem"/> の DI 経由で
/// Step が受け取って利用する (通常は user コードは Step 経由で間接的にアクセス)。
///
/// <para><b>セットアップ</b>: プログラム起動時に registry を作って DI に登録し、必要な Step を追加する。
/// <code>
/// var registry = new AssetGpuRegistry(device);
/// resources.AddService(registry);
/// resources.AddStep&lt;AssetMeshToGpuStep&gt;();      // ctor で (GpuDevice, AssetGpuRegistry) を受け取る
/// resources.AddStep&lt;AssetMaterialToGpuStep&gt;();
/// resources.AddStep&lt;AssetTextureToGpuStep&gt;();
/// </code></para>
///
/// <para><b>Resource system を使わない場合</b>は <see cref="GpuAssetFactory"/> を直接呼んで
/// sample 側で Dictionary を管理する。</para>
/// </summary>
public sealed class AssetGpuRegistry : IDisposable
{
    private readonly GpuDevice _device;

    private readonly Dictionary<AssetMesh, GpuMesh> _meshes = new();
    private readonly Dictionary<AssetMaterial, GpuMaterial> _materials = new();
    private readonly Dictionary<AssetTexture, GpuTexture> _textures = new();
    private readonly Dictionary<AssetSampler, GpuSampler> _samplers = new();
    private readonly Dictionary<AssetSkin, GpuSkin> _skins = new();

    public GpuMaterialArray MaterialArray { get; }
    public GpuSampler DefaultSampler { get; }

    /// <summary>コンストラクタ DI: Step が Resource system 経由で本 registry を受け取る。</summary>
    public AssetGpuRegistry(GpuDevice device)
    {
        _device = device;
        DefaultSampler = _device.CreateSampler(GpuSamplerFilter.Linear, GpuSamplerAddress.Repeat);
        MaterialArray = new GpuMaterialArray(_device);
    }

    // ==================== Scoped resource creation ====================

    /// <summary>
    /// ResourceSystem の creation Step から利用する device-bound factory。
    /// 返却値の所有者は ResourceSystem node であり、registry の asset cache には保持しない。
    /// </summary>
    internal GpuPipeline Create(GpuPipelineRequest request)
        => request.IsCompute
            ? _device.CreateComputePipeline(request.Code, request.ComputeEntry)
            : _device.CreateGraphicsPipeline(request.Code, request.Raster, request.VertexEntry, request.PixelEntry);

    /// <summary>ResourceSystem-owned texture を生成する。asset cache には保持しない。</summary>
    internal GpuTexture Create(GpuTextureRequest request) => request.Kind switch
    {
        GpuTextureRequestKind.Sampled =>
            _device.CreateTexture(request.Width, request.Height, request.Data.Span, request.Format),
        GpuTextureRequestKind.RenderTarget =>
            _device.CreateRenderTarget(request.Width, request.Height, request.Format),
        GpuTextureRequestKind.DepthTarget =>
            _device.CreateDepthTarget(request.Width, request.Height, request.Format),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown texture request kind."),
    };

    /// <summary>ResourceSystem-owned sampler を生成する。asset cache には保持しない。</summary>
    internal GpuSampler Create(GpuSamplerRequest request)
        => _device.CreateSampler(request.Filter, request.Address);

    /// <summary>ResourceSystem-owned buffer を生成する。asset cache には保持しない。</summary>
    internal GpuBuffer Create(GpuBufferRequest request)
        => _device.Malloc(request.SizeInBytes, request.Kind);

    // ==================== Register (programmatic upload) ====================

    public GpuTexture Register(AssetTexture tex)
    {
        if (_textures.TryGetValue(tex, out var cached)) return cached;
        var gpu = GpuAssetFactory.Upload(tex, _device);
        _textures[tex] = gpu;
        return gpu;
    }

    public GpuSampler Register(AssetSampler samp)
    {
        if (_samplers.TryGetValue(samp, out var cached)) return cached;
        var gpu = GpuAssetFactory.Upload(samp, _device);
        _samplers[samp] = gpu;
        return gpu;
    }

    public GpuMaterial Register(AssetMaterial mat)
    {
        if (_materials.TryGetValue(mat, out var cached)) return cached;
        var gpu = GpuAssetFactory.Upload(mat, _device, _textures, _samplers, DefaultSampler);
        _materials[mat] = gpu;
        MaterialArray.Register(gpu);
        return gpu;
    }

    public GpuMesh Register(AssetMesh mesh)
    {
        if (_meshes.TryGetValue(mesh, out var cached)) return cached;
        // primitive の material も自動 register
        foreach (var prim in mesh.Primitives)
            if (prim.Material is not null) Register(prim.Material);
        var gpu = GpuAssetFactory.Upload(mesh, _device, _materials);
        _meshes[mesh] = gpu;
        return gpu;
    }

    public GpuSkin Register(AssetSkin skin)
    {
        if (_skins.TryGetValue(skin, out var cached)) return cached;
        var gpu = GpuAssetFactory.Upload(skin, _device);
        _skins[skin] = gpu;
        return gpu;
    }

    /// <summary>Document 全体を depend 順 (Texture → Sampler → Material → Mesh → Skin) で登録。</summary>
    public void Register(AssetDocument doc)
    {
        foreach (var s in doc.Samplers) Register(s);
        foreach (var t in doc.Textures) Register(t);
        foreach (var m in doc.Materials) Register(m);
        foreach (var mesh in doc.Meshes) Register(mesh);
        foreach (var skin in doc.Skins) Register(skin);
        MaterialArray.FlushImmediate();
    }

    // ==================== Resolve (lookup) ====================

    public GpuMesh? Resolve(AssetMesh mesh) => _meshes.TryGetValue(mesh, out var g) ? g : null;
    public GpuMaterial? Resolve(AssetMaterial mat) => _materials.TryGetValue(mat, out var g) ? g : null;
    public GpuTexture? Resolve(AssetTexture tex) => _textures.TryGetValue(tex, out var g) ? g : null;
    public GpuSampler? Resolve(AssetSampler samp) => _samplers.TryGetValue(samp, out var g) ? g : null;
    public GpuSkin? Resolve(AssetSkin skin) => _skins.TryGetValue(skin, out var g) ? g : null;

    // ==================== Shader-facing index lookup ====================

    /// <summary>ECS extractor が使う: AssetMesh.Primitives[primitive] の vertex buffer bindless index を返す。</summary>
    public uint? VertexBindlessIndex(AssetMesh mesh, int primitive = 0)
    {
        if (!_meshes.TryGetValue(mesh, out var g) || primitive >= g.Primitives.Count) return null;
        return g.Primitives[primitive].VertexBuffer?.BindlessIndex;
    }

    public uint? IndexBindlessIndex(AssetMesh mesh, int primitive = 0)
    {
        if (!_meshes.TryGetValue(mesh, out var g) || primitive >= g.Primitives.Count) return null;
        return g.Primitives[primitive].IndexBuffer?.BindlessIndex;
    }

    public int? MaterialArrayIndex(AssetMaterial mat)
    {
        if (!_materials.TryGetValue(mat, out var gm)) return null;
        return MaterialArray.IndexOf(gm);
    }

    /// <summary>skin.JointMatrices buffer の bindless index (shader が joint 行列を読むため)。</summary>
    public uint? JointBindlessIndex(AssetSkin skin)
        => _skins.TryGetValue(skin, out var g) ? g.JointMatrices?.Buffer?.BindlessIndex : null;

    public void Dispose()
    {
        foreach (var g in _meshes.Values) g.Dispose();
        foreach (var g in _skins.Values) g.Dispose();
        foreach (var g in _textures.Values) g.Dispose();
        foreach (var g in _samplers.Values) g.Dispose();
        DefaultSampler?.Dispose();
        MaterialArray?.Dispose();
        _meshes.Clear(); _materials.Clear(); _textures.Clear(); _samplers.Clear(); _skins.Clear();
    }
}
