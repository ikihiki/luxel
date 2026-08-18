using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// Asset* を GPU へ upload + cache する service。legacy direct-reference API に加えて、
/// document identity + typed stable index 単位の installation state を管理する。
/// </summary>
public sealed class AssetGpuRegistry : IDisposable
{
    private GpuDevice _device;
    private GpuDeviceGeneration _generation;
    private bool _disposed;
    private readonly Dictionary<AssetMesh, GpuMesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetMaterial, GpuMaterial> _materials = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetTexture, GpuTexture> _textures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetSampler, GpuSampler> _samplers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetSkin, GpuSkin> _skins = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetDocument, AssetGpuDocumentState> _documents = new(ReferenceEqualityComparer.Instance);

    public GpuMaterialArray MaterialArray { get; private set; }
    public GpuSampler DefaultSampler { get; private set; }
    public GpuDeviceGeneration DeviceGeneration => _generation;

    public AssetGpuRegistry(GpuDevice device, GpuDeviceGeneration? generation = null)
    {
        _device = device;
        _generation = generation ?? new GpuDeviceGeneration(Guid.NewGuid().ToString("N"), 1);
        DefaultSampler = _device.CreateSampler(GpuSamplerFilter.Linear, GpuSamplerAddress.Repeat);
        MaterialArray = new GpuMaterialArray(_device);
    }

    internal void ActivateGeneration(GpuDevice device, GpuDeviceGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (_disposed) throw new ObjectDisposedException(nameof(AssetGpuRegistry));
        DisposeResources();
        _device = device;
        _generation = generation;
        DefaultSampler = _device.CreateSampler(GpuSamplerFilter.Linear, GpuSamplerAddress.Repeat);
        MaterialArray = new GpuMaterialArray(_device);
    }

    // ResourceSystem-owned factories. Values returned by these methods are not registry cached.
    internal GpuPipeline Create(GpuPipelineRequest request, GpuShaderCode code)
        => request.IsCompute
            ? _device.CreateComputePipeline(code, request.ComputeEntry)
            : _device.CreateGraphicsPipeline(code, request.Graphics);

    internal GpuBuffer Create(float[] data)
    {
        var buffer = _device.Malloc(checked((ulong)data.Length * sizeof(float)), GpuMemoryKind.HostMapped);
        try
        {
            data.AsSpan().CopyTo(buffer.Span<float>(data.Length));
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    internal GpuTexture Create(GpuTextureRequest request) => request.Kind switch
    {
        GpuTextureRequestKind.Sampled => _device.CreateTexture(request.Width, request.Height, request.Data.Span, request.Format),
        GpuTextureRequestKind.RenderTarget => _device.CreateRenderTarget(request.Width, request.Height, request.Format),
        GpuTextureRequestKind.DepthTarget => _device.CreateDepthTarget(request.Width, request.Height, request.Format),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown texture request kind."),
    };

    internal GpuSampler Create(GpuSamplerRequest request) => _device.CreateSampler(request.Filter, request.Address);
    internal GpuBuffer Create(GpuBufferRequest request) => _device.Malloc(request.SizeInBytes, request.Kind);

    // Legacy direct-reference registration remains source-compatible.
    public GpuTexture Register(AssetTexture tex)
    {
        if (_textures.TryGetValue(tex, out var cached)) return cached;
        return _textures[tex] = GpuAssetFactory.Upload(tex, _device);
    }

    public GpuSampler Register(AssetSampler samp)
    {
        if (_samplers.TryGetValue(samp, out var cached)) return cached;
        return _samplers[samp] = GpuAssetFactory.Upload(samp, _device);
    }

    public GpuMaterial Register(AssetMaterial mat)
    {
        if (_materials.TryGetValue(mat, out var cached)) return cached;
        RegisterMaterialDependencies(mat, Register, Register);
        var gpu = GpuAssetFactory.Upload(mat, _device, _textures, _samplers, DefaultSampler);
        _materials[mat] = gpu;
        MaterialArray.Register(gpu);
        return gpu;
    }

    public GpuMesh Register(AssetMesh mesh)
    {
        if (_meshes.TryGetValue(mesh, out var cached)) return cached;
        foreach (var prim in mesh.Primitives)
            if (prim.Material is not null) Register(prim.Material);
        return _meshes[mesh] = GpuAssetFactory.Upload(mesh, _device, _materials);
    }

    public GpuSkin Register(AssetSkin skin)
    {
        if (_skins.TryGetValue(skin, out var cached)) return cached;
        return _skins[skin] = GpuAssetFactory.Upload(skin, _device);
    }

    /// <summary>document 固有の installation state を取得する。未登録なら空 state を作る。</summary>
    public AssetGpuDocumentState GetDocumentState(AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_documents.TryGetValue(document, out var state)) return state;
        state = new AssetGpuDocumentState(document);
        _documents.Add(document, state);
        return state;
    }

    public bool TryGetDocumentState(AssetDocument document, out AssetGpuDocumentState? state)
        => _documents.TryGetValue(document, out state);

    public GpuTexture Register(AssetDocument document, AssetTextureIndex index)
    {
        var state = GetDocumentState(document);
        if (state.Textures.TryGetValue(index, out var cached)) return cached;
        AssetTexture asset = document.Indices.Resolve(index);
        var gpu = RegisterDocumentTexture(state, asset);
        _textures.TryAdd(asset, gpu); // legacy Resolve(asset) compatibility (first document wins)
        state.Textures[index] = gpu;
        return gpu;
    }

    public GpuSampler Register(AssetDocument document, AssetSamplerIndex index)
    {
        var state = GetDocumentState(document);
        if (state.Samplers.TryGetValue(index, out var cached)) return cached;
        AssetSampler asset = document.Indices.Resolve(index);
        var gpu = RegisterDocumentSampler(state, asset);
        _samplers.TryAdd(asset, gpu); // legacy Resolve(asset) compatibility
        state.Samplers[index] = gpu;
        return gpu;
    }

    public GpuMaterial Register(AssetDocument document, AssetMaterialIndex index)
    {
        var state = GetDocumentState(document);
        if (state.Materials.TryGetValue(index, out var cached)) return cached;
        AssetMaterial asset = document.Indices.Resolve(index);
        RegisterMaterialDependencies(asset,
            texture => RegisterDocumentTexture(state, texture),
            sampler => RegisterDocumentSampler(state, sampler));
        var gpu = GpuAssetFactory.Upload(asset, _device, state.TextureObjects, state.SamplerObjects, DefaultSampler);
        state.MaterialObjects[asset] = gpu;
        _materials.TryAdd(asset, gpu); // legacy Resolve(asset) compatibility
        state.Materials[index] = gpu;
        MaterialArray.Register(gpu);
        return gpu;
    }

    public GpuMesh Register(AssetDocument document, AssetMeshIndex index)
    {
        var state = GetDocumentState(document);
        if (state.Meshes.TryGetValue(index, out var cached)) return cached;
        AssetMesh asset = document.Indices.Resolve(index);
        foreach (AssetPrimitive primitive in asset.Primitives)
        {
            if (primitive.Material is null) continue;
            if (document.Indices.TryGetIndex(primitive.Material, out AssetMaterialIndex materialIndex))
                Register(document, materialIndex);
            else
                RegisterDocumentMaterial(state, primitive.Material);
        }
        var gpu = GpuAssetFactory.Upload(asset, _device, state.MaterialObjects);
        state.MeshObjects[asset] = gpu;
        _meshes.TryAdd(asset, gpu); // legacy Resolve(asset) compatibility
        state.Meshes[index] = gpu;
        for (int i = 0; i < asset.Primitives.Count; i++)
        {
            AssetPrimitiveIndex primitiveIndex = document.Indices.GetIndex(asset.Primitives[i]);
            state.Primitives[primitiveIndex] = gpu.Primitives[i];
        }
        return gpu;
    }

    public GpuSkin Register(AssetDocument document, AssetSkinIndex index)
    {
        var state = GetDocumentState(document);
        if (state.Skins.TryGetValue(index, out var cached)) return cached;
        AssetSkin asset = document.Indices.Resolve(index);
        var gpu = GpuAssetFactory.Upload(asset, _device);
        state.SkinObjects[asset] = gpu;
        _skins.TryAdd(asset, gpu); // legacy Resolve(asset) compatibility
        state.Skins[index] = gpu;
        return gpu;
    }

    /// <summary>Document 全体を dependency 順で一度だけ install する。</summary>
    public void Register(AssetDocument document)
    {
        var state = GetDocumentState(document);
        if (state.IsInstalled) return;
        foreach (var asset in document.Samplers) Register(document, document.Indices.GetIndex(asset));
        foreach (var asset in document.Textures) Register(document, document.Indices.GetIndex(asset));
        foreach (var asset in document.Materials) Register(document, document.Indices.GetIndex(asset));
        foreach (var asset in document.Meshes) Register(document, document.Indices.GetIndex(asset));
        foreach (var asset in document.Skins) Register(document, document.Indices.GetIndex(asset));
        MaterialArray.FlushImmediate();
        state.IsInstalled = true;
    }

    public GpuMesh Register(AssetDocumentHandle<AssetMeshIndex> handle) => Register(handle.Document, handle.Index);
    public GpuMaterial Register(AssetDocumentHandle<AssetMaterialIndex> handle) => Register(handle.Document, handle.Index);
    public GpuTexture Register(AssetDocumentHandle<AssetTextureIndex> handle) => Register(handle.Document, handle.Index);
    public GpuSampler Register(AssetDocumentHandle<AssetSamplerIndex> handle) => Register(handle.Document, handle.Index);
    public GpuSkin Register(AssetDocumentHandle<AssetSkinIndex> handle) => Register(handle.Document, handle.Index);

    public GpuMesh? Resolve(AssetMesh mesh) => _meshes.TryGetValue(mesh, out var value) ? value : null;
    public GpuMaterial? Resolve(AssetMaterial mat) => _materials.TryGetValue(mat, out var value) ? value : null;
    public GpuTexture? Resolve(AssetTexture tex) => _textures.TryGetValue(tex, out var value) ? value : null;
    public GpuSampler? Resolve(AssetSampler samp) => _samplers.TryGetValue(samp, out var value) ? value : null;
    public GpuSkin? Resolve(AssetSkin skin) => _skins.TryGetValue(skin, out var value) ? value : null;

    public GpuMesh? Resolve(AssetDocument document, AssetMeshIndex index)
        => GetDocumentState(document).Meshes.TryGetValue(index, out var value) ? value : null;
    public GpuPrimitive? Resolve(AssetDocument document, AssetPrimitiveIndex index)
        => GetDocumentState(document).Primitives.TryGetValue(index, out var value) ? value : null;
    public GpuMaterial? Resolve(AssetDocument document, AssetMaterialIndex index)
        => GetDocumentState(document).Materials.TryGetValue(index, out var value) ? value : null;
    public GpuTexture? Resolve(AssetDocument document, AssetTextureIndex index)
        => GetDocumentState(document).Textures.TryGetValue(index, out var value) ? value : null;
    public GpuSampler? Resolve(AssetDocument document, AssetSamplerIndex index)
        => GetDocumentState(document).Samplers.TryGetValue(index, out var value) ? value : null;
    public GpuSkin? Resolve(AssetDocument document, AssetSkinIndex index)
        => GetDocumentState(document).Skins.TryGetValue(index, out var value) ? value : null;

    public uint? VertexBindlessIndex(AssetMesh mesh, int primitive = 0)
        => Resolve(mesh) is { } gpu && (uint)primitive < (uint)gpu.Primitives.Count
            ? gpu.Primitives[primitive].VertexBuffer?.BindlessIndex : null;
    public uint? IndexBindlessIndex(AssetMesh mesh, int primitive = 0)
        => Resolve(mesh) is { } gpu && (uint)primitive < (uint)gpu.Primitives.Count
            ? gpu.Primitives[primitive].IndexBuffer?.BindlessIndex : null;
    public int? MaterialArrayIndex(AssetMaterial mat)
        => Resolve(mat) is { } gpu ? MaterialArray.IndexOf(gpu) : null;
    public uint? JointBindlessIndex(AssetSkin skin)
        => Resolve(skin)?.JointMatrices?.Buffer?.BindlessIndex;

    public uint? VertexBindlessIndex(AssetDocument document, AssetPrimitiveIndex primitive)
        => Resolve(document, primitive)?.VertexBuffer?.BindlessIndex;
    public uint? IndexBindlessIndex(AssetDocument document, AssetPrimitiveIndex primitive)
        => Resolve(document, primitive)?.IndexBuffer?.BindlessIndex;
    public int? MaterialArrayIndex(AssetDocument document, AssetMaterialIndex material)
        => Resolve(document, material) is { } gpu ? MaterialArray.IndexOf(gpu) : null;
    public uint? JointBindlessIndex(AssetDocument document, AssetSkinIndex skin)
        => Resolve(document, skin)?.JointMatrices?.Buffer?.BindlessIndex;

    private GpuTexture RegisterDocumentTexture(AssetGpuDocumentState state, AssetTexture asset)
    {
        if (state.TextureObjects.TryGetValue(asset, out var cached)) return cached;
        var gpu = GpuAssetFactory.Upload(asset, _device);
        _textures.TryAdd(asset, gpu);
        state.TextureObjects.Add(asset, gpu);
        if (state.Document.Indices.TryGetIndex(asset, out AssetTextureIndex index)) state.Textures[index] = gpu;
        return gpu;
    }

    private GpuSampler RegisterDocumentSampler(AssetGpuDocumentState state, AssetSampler asset)
    {
        if (state.SamplerObjects.TryGetValue(asset, out var cached)) return cached;
        var gpu = GpuAssetFactory.Upload(asset, _device);
        _samplers.TryAdd(asset, gpu);
        state.SamplerObjects.Add(asset, gpu);
        if (state.Document.Indices.TryGetIndex(asset, out AssetSamplerIndex index)) state.Samplers[index] = gpu;
        return gpu;
    }

    private GpuMaterial RegisterDocumentMaterial(AssetGpuDocumentState state, AssetMaterial asset)
    {
        if (state.MaterialObjects.TryGetValue(asset, out var cached)) return cached;
        RegisterMaterialDependencies(asset,
            texture => RegisterDocumentTexture(state, texture),
            sampler => RegisterDocumentSampler(state, sampler));
        var gpu = GpuAssetFactory.Upload(asset, _device, state.TextureObjects, state.SamplerObjects, DefaultSampler);
        _materials.TryAdd(asset, gpu);
        state.MaterialObjects.Add(asset, gpu);
        if (state.Document.Indices.TryGetIndex(asset, out AssetMaterialIndex index)) state.Materials[index] = gpu;
        MaterialArray.Register(gpu);
        return gpu;
    }

    private static void RegisterMaterialDependencies(
        AssetMaterial material, Func<AssetTexture, GpuTexture> registerTexture,
        Func<AssetSampler, GpuSampler> registerSampler)
    {
        AssetTexture?[] textures =
        [
            material.BaseColorTexture?.Texture,
            material.MetallicRoughnessTexture?.Texture,
            material.NormalTexture?.Texture,
            material.OcclusionTexture?.Texture,
            material.EmissiveTexture?.Texture,
        ];
        foreach (AssetTexture? texture in textures)
        {
            if (texture is null) continue;
            registerTexture(texture);
            if (texture.Sampler is not null) registerSampler(texture.Sampler);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeResources();
    }

    private void DisposeResources()
    {
        var disposed = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        void DisposeOnce(IDisposable value)
        {
            if (disposed.Add(value)) value.Dispose();
        }

        foreach (var state in _documents.Values)
        {
            foreach (var value in state.MeshObjects.Values) DisposeOnce(value);
            foreach (var value in state.SkinObjects.Values) DisposeOnce(value);
            foreach (var value in state.TextureObjects.Values) DisposeOnce(value);
            foreach (var value in state.SamplerObjects.Values) DisposeOnce(value);
        }
        foreach (var value in _meshes.Values) DisposeOnce(value);
        foreach (var value in _skins.Values) DisposeOnce(value);
        foreach (var value in _textures.Values) DisposeOnce(value);
        foreach (var value in _samplers.Values) DisposeOnce(value);
        DisposeOnce(DefaultSampler);
        DisposeOnce(MaterialArray);
        _documents.Clear();
        _meshes.Clear(); _materials.Clear(); _textures.Clear(); _samplers.Clear(); _skins.Clear();
    }
}
