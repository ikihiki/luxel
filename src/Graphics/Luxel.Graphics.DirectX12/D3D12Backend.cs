using Luxel.Graphics.Abstraction;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Luxel.Graphics.DirectX12;

/// <summary>
/// DirectX 12 バックエンド。D3D12/HLSL には生 64bit ポインタが無いため、ブログの
/// ポインタモデルを SM6.6 bindless (ResourceDescriptorHeap) でエミュレーションする。
/// 全パイプライン共通の固定ルートシグネチャ = root SRV(t0)=ルート引数 + 直接インデックスヒープ。
/// </summary>
public sealed unsafe class D3D12Backend : IGpuBackend
{
    private const int HeapCapacity = 100_000;
    private const uint RtvCapacity = 64;
    private const uint SamplerCapacity = 1024;
    private const uint DsvCapacity = 64;
    private const uint PushConstantDwords = 48; // 192 バイト (shadow map で mat4×2 を渡すため拡張)。
                                                // D3D12 root signature 上限は 64 DWord = 256B、48 DWord で余裕あり。

    private GpuLifecycleSource _lifecycle = null!;
    private IDXGIFactory4 _factory = null!;
    private ID3D12Device _device = null!;
    private ID3D12CommandQueue _queue = null!;
    private ID3D12DescriptorHeap _resourceHeap = null!;
    private ID3D12RootSignature _rootSignature = null!;
    private uint _descriptorSize;
    private CpuDescriptorHandle _heapCpuStart;
    private readonly DescriptorSlotAllocator _resourceSlots = new(HeapCapacity);
    private readonly object _queueLock = new();   // queue submit/wait の直列化 (OneShotSubmit と MainQueue で共有)

    private ID3D12DescriptorHeap _rtvHeap = null!;
    private uint _rtvSize;
    private CpuDescriptorHandle _rtvStart;
    private readonly DescriptorSlotAllocator _rtvSlots = new(RtvCapacity);

    private ID3D12DescriptorHeap _samplerHeap = null!;
    private uint _samplerSize;
    private CpuDescriptorHandle _samplerStart;
    private readonly DescriptorSlotAllocator _samplerSlots = new(SamplerCapacity);

    private ID3D12DescriptorHeap _dsvHeap = null!;
    private uint _dsvSize;
    private CpuDescriptorHandle _dsvStart;
    private readonly DescriptorSlotAllocator _dsvSlots = new(DsvCapacity);
    private ID3D12CommandQueue UploadQueue => _queue;
    private bool _disposed;

    private D3D12Backend() { }

    public string Name { get; private set; } = "Direct3D12";
    public GpuBackendKind Kind => GpuBackendKind.D3D12;
    public IGpuBackendQueue MainQueue { get; private set; } = null!;

    public static D3D12Backend Create(bool enableDebug = true, IGpuLifecycleSink? lifecycleSink = null,
        string? deviceId = null, ulong generation = 1)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Direct3D 12 is available only on Windows.");
        var backend = new D3D12Backend
        {
            _lifecycle = new GpuLifecycleSource(GpuBackendKind.D3D12, "Direct3D12", lifecycleSink, deviceId, generation),
        };
        backend._lifecycle.DeviceEvent(GpuDeviceLifecycleState.Creating);
        try
        {
            backend.Initialize(enableDebug);
            backend._lifecycle.SetBackendName(backend.Name);
            backend._lifecycle.DeviceEvent(GpuDeviceLifecycleState.Ready);
            return backend;
        }
        catch (Exception exception)
        {
            backend._lifecycle.DeviceEvent(GpuDeviceLifecycleState.Faulted, GpuLifecycleReason.Unknown,
                message: exception.Message, exception: exception);
            backend.Dispose();
            throw;
        }
    }

    private void Initialize(bool enableDebug)
    {
        // 注: Agility SDK のローカルランタイムは Windows 開発者モードが必要なため使わない (OS ランタイムを使用)。
        if (enableDebug && D3D12GetDebugInterface(out ID3D12Debug debug).Success)
        {
            debug.EnableDebugLayer();
            debug.Dispose();
        }

        // DXGI のデバッグフラグは Graphics Tools 未導入だと INVALID_CALL になるため使わない。
        CreateDXGIFactory2(false, out _factory).CheckError();

        for (uint i = 0; _factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
        {
            AdapterDescription1 desc = adapter.Description1;
            if ((desc.Flags & AdapterFlags.Software) != AdapterFlags.None)
            {
                adapter.Dispose();
                continue;
            }
            if (D3D12CreateDevice(adapter, FeatureLevel.Level_12_1, out ID3D12Device? device).Success)
            {
                _device = device!;
                Name = $"Direct3D12 / {desc.Description}";
                adapter.Dispose();
                break;
            }
            adapter.Dispose();
        }
        if (_device is null) throw new InvalidOperationException("D3D12 対応アダプタが見つかりません。");

        _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));

        var heapDesc = new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            HeapCapacity, DescriptorHeapFlags.ShaderVisible, 0);
        _resourceHeap = _device.CreateDescriptorHeap(heapDesc);
        _descriptorSize = _device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        _heapCpuStart = _resourceHeap.GetCPUDescriptorHandleForHeapStart();

        var rtvHeapDesc = new DescriptorHeapDescription(
            DescriptorHeapType.RenderTargetView, RtvCapacity, DescriptorHeapFlags.None, 0);
        _rtvHeap = _device.CreateDescriptorHeap(rtvHeapDesc);
        _rtvSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _rtvStart = _rtvHeap.GetCPUDescriptorHandleForHeapStart();

        var samplerHeapDesc = new DescriptorHeapDescription(
            DescriptorHeapType.Sampler, SamplerCapacity, DescriptorHeapFlags.ShaderVisible, 0);
        _samplerHeap = _device.CreateDescriptorHeap(samplerHeapDesc);
        _samplerSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.Sampler);
        _samplerStart = _samplerHeap.GetCPUDescriptorHandleForHeapStart();

        var dsvHeapDesc = new DescriptorHeapDescription(
            DescriptorHeapType.DepthStencilView, DsvCapacity, DescriptorHeapFlags.None, 0);
        _dsvHeap = _device.CreateDescriptorHeap(dsvHeapDesc);
        _dsvSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        _dsvStart = _dsvHeap.GetCPUDescriptorHandleForHeapStart();

        CreateRootSignature();

        MainQueue = new D3D12Queue(_device, _queue, _rootSignature, _resourceHeap, _samplerHeap, _queueLock, _lifecycle);
    }

    /// <summary>Creates a DXGI presentation surface for a Win32 HWND.</summary>
    public GpuSurface CreateSurface(nint hwnd, uint width, uint height)
    {
        if (hwnd == 0) throw new ArgumentException("A non-zero Win32 HWND is required.", nameof(hwnd));
        return new GpuSurface(this, new D3D12Surface(_factory, _device, _queue, hwnd, width, height));
    }

    private void CreateRootSignature()
    {
        // 全パイプライン共通の固定ルートシグネチャ:
        //   param 0 = root 32bit 定数 (b0)   = ルート引数 (固定容量 192B)
        //   param 1 = descriptor table (u0, space1, unbounded) = bindless バッファ配列
        const uint unbounded = unchecked((uint)-1);
        var uavRange = new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, unbounded, 0, 1, 0, DescriptorRangeFlags.DescriptorsVolatile);
        var srvRange = new DescriptorRange1(DescriptorRangeType.ShaderResourceView, unbounded, 0, 1, 0, DescriptorRangeFlags.DescriptorsVolatile);
        var samplerRange = new DescriptorRange1(DescriptorRangeType.Sampler, unbounded, 0, 2, 0, DescriptorRangeFlags.None);
        var rootParams = new[]
        {
            new RootParameter1(new RootConstants(0, 0, PushConstantDwords), ShaderVisibility.All),  // b0 = ルート引数
            new RootParameter1(new RootDescriptorTable1(new[] { uavRange }), ShaderVisibility.All),     // u0,space1 = buffers
            new RootParameter1(new RootDescriptorTable1(new[] { srvRange }), ShaderVisibility.All),     // t0,space1 = textures
            new RootParameter1(new RootDescriptorTable1(new[] { samplerRange }), ShaderVisibility.All), // s0,space2 = samplers
        };
        var desc = new RootSignatureDescription1(RootSignatureFlags.None, rootParams, null);
        var versioned = new VersionedRootSignatureDescription(desc);
        string err = D3D12SerializeVersionedRootSignature(versioned, out Vortice.Direct3D.Blob blob);
        if (!string.IsNullOrEmpty(err))
            Console.Error.WriteLine($"[D3D12] RS serialize error: {err}");
        _rootSignature = _device.CreateRootSignature(0, blob);
        blob.Dispose();
    }

    // ---- gpuMalloc -----------------------------------------------------------

    public IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
    {
        HeapType heapType = kind switch
        {
            GpuMemoryKind.HostMapped => HeapType.GpuUpload,
            GpuMemoryKind.DeviceLocal => HeapType.Default,
            GpuMemoryKind.HostCached => HeapType.Readback,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        bool isReadback = kind == GpuMemoryKind.HostCached;
        ResourceFlags resourceFlags = isReadback
            ? ResourceFlags.None
            : ResourceFlags.AllowUnorderedAccess;
        ResourceStates initialState = isReadback
            ? ResourceStates.CopyDest
            : ResourceStates.Common;
        var resDesc = ResourceDescription.Buffer(size, resourceFlags);
        ID3D12Resource resource = _device.CreateCommittedResource(
            new HeapProperties(heapType), HeapFlags.None, resDesc, initialState);

        // CPU マップ (host-visible なヒープのみ)。バッファは COMMON から暗黙昇格する。
        void* mapped = null;
        bool hostVisible = heapType is HeapType.GpuUpload or HeapType.Upload or HeapType.Readback;
        if (hostVisible) mapped = resource.Map<byte>(0);

        // Readback heaps can only be copy destinations and cannot expose a UAV.
        // They are CPU-only staging resources, so they do not need a bindless slot.
        if (isReadback)
        {
            return new D3D12Buffer(resource, size, resource.GPUVirtualAddress,
                uint.MaxValue, mapped, kind, static () => { });
        }

        // raw UAV を bindless ヒープに登録。
        uint index;
        try
        {
            index = _resourceSlots.Allocate();
        }
        catch
        {
            if (mapped != null) resource.Unmap(0);
            resource.Dispose();
            throw;
        }
        var cpu = new CpuDescriptorHandle(_heapCpuStart, (int)index, _descriptorSize);
        var uavDesc = new UnorderedAccessViewDescription
        {
            Format = Format.R32_Typeless,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView
            {
                FirstElement = 0,
                NumElements = (uint)(size / 4),
                StructureByteStride = 0,
                CounterOffsetInBytes = 0,
                Flags = BufferUnorderedAccessViewFlags.Raw,
            },
        };
        try
        {
            _device.CreateUnorderedAccessView(resource, null, uavDesc, cpu);
        }
        catch
        {
            _resourceSlots.Free(index);
            if (mapped != null) resource.Unmap(0);
            resource.Dispose();
            throw;
        }

        return new D3D12Buffer(resource, size, resource.GPUVirtualAddress, index, mapped, kind,
            () => _resourceSlots.Free(index));
    }

    // ---- Pipelines -----------------------------------------------------------

    public IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint)
    {
        var psoDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = shaderBlob.ToArray(),
        };
        try
        {
            ID3D12PipelineState pso = _device.CreateComputePipelineState(psoDesc);
            return new D3D12Pipeline(pso, isCompute: true);
        }
        catch
        {
            DumpDebugMessages();
            throw;
        }
    }

    public IGpuBackendPipeline CreateGraphicsPipeline(
        ReadOnlySpan<byte> vsBlob, ReadOnlySpan<byte> psBlob, GpuGraphicsPipelineDesc description)
    {
        byte[] vertex = vsBlob.ToArray();
        byte[] pixel = psBlob.ToArray();
        return new D3D12Pipeline(description, key => CreateGraphicsPipelineVariant(vertex, pixel, key));
    }

    private ID3D12PipelineState CreateGraphicsPipelineVariant(byte[] vsBlob, byte[] psBlob,
        GpuGraphicsPipelineVariantKey key)
    {
        RasterizerDescription rasterizer = RasterizerDescription.CullNone;
        rasterizer.CullMode = key.Rasterizer.CullMode switch
        {
            GpuCullMode.Front => CullMode.Front,
            GpuCullMode.Back => CullMode.Back,
            _ => CullMode.None,
        };
        rasterizer.FrontCounterClockwise = key.Rasterizer.FrontFace == GpuFrontFace.CounterClockwise;

        var psoDesc = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vsBlob.ToArray(),
            PixelShader = psBlob.ToArray(),
            InputLayout = new InputLayoutDescription(),   // 空 = vertex pulling
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RasterizerState = rasterizer,
            // NonPremultiplied = straight alpha (SrcAlpha, InvSrcAlpha)。Vulkan 側と一致させる。
            BlendState = key.Blend.Mode == GpuBlendMode.AlphaBlend
                ? BlendDescription.NonPremultiplied : BlendDescription.Opaque,
            DepthStencilState = CreateDepthStencil(key.DepthStencil),
            RenderTargetFormats = new[] { ToDxgiFormat(key.Attachments.ColorFormat) },
            DepthStencilFormat = key.Attachments.DepthStencilFormat is { } depthFormat ? ToDxgiFormat(depthFormat) : Format.Unknown,
            SampleMask = uint.MaxValue,
            SampleDescription = new SampleDescription(1, 0),
        };
        ID3D12PipelineState pso = _device.CreateGraphicsPipelineState(psoDesc);
        return pso;
    }

    public IGpuBackendTexture CreateRenderTarget(uint width, uint height, GpuFormat format)
    {
        Format dxgi = ToDxgiFormat(format);
        var desc = ResourceDescription.Texture2D(dxgi, width, height, 1, 1, 1, 0,
            ResourceFlags.AllowRenderTarget, TextureLayout.Unknown, 0);
        ID3D12Resource res = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None, desc, ResourceStates.Common);

        uint idx;
        try
        {
            idx = _rtvSlots.Allocate();
        }
        catch
        {
            res.Dispose();
            throw;
        }
        var handle = new CpuDescriptorHandle(_rtvStart, (int)idx, _rtvSize);
        try
        {
            _device.CreateRenderTargetView(res, null, handle);
        }
        catch
        {
            _rtvSlots.Free(idx);
            res.Dispose();
            throw;
        }
        return new D3D12Texture(res, width, height, format, dxgi, handle, 0, ResourceStates.Common,
            () => _rtvSlots.Free(idx));
    }

    public IGpuBackendTexture CreateDepthTarget(uint width, uint height, GpuFormat format)
    {
        Format dxgi = ToDxgiFormat(format);
        var desc = ResourceDescription.Texture2D(dxgi, width, height, 1, 1, 1, 0,
            ResourceFlags.AllowDepthStencil, TextureLayout.Unknown, 0);
        var clear = new ClearValue(dxgi, 1.0f, 0);
        ID3D12Resource res = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None, desc, ResourceStates.DepthWrite, clear);

        uint idx;
        try
        {
            idx = _dsvSlots.Allocate();
        }
        catch
        {
            res.Dispose();
            throw;
        }
        var handle = new CpuDescriptorHandle(_dsvStart, (int)idx, _dsvSize);
        try
        {
            _device.CreateDepthStencilView(res, null, handle);
        }
        catch
        {
            _dsvSlots.Free(idx);
            res.Dispose();
            throw;
        }
        return new D3D12Texture(res, width, height, format, dxgi, default, 0, ResourceStates.DepthWrite,
            () => _dsvSlots.Free(idx))
        {
            Dsv = handle,
        };
    }

    public unsafe IGpuBackendTexture CreateSampledTexture(uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data)
    {
        Format dxgi = ToDxgiFormat(format);
        var desc = ResourceDescription.Texture2D(dxgi, width, height, 1, 1, 1, 0,
            ResourceFlags.None, TextureLayout.Unknown, 0);
        ID3D12Resource tex = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None, desc, ResourceStates.Common);

        // フットプリント (行ピッチは 256B アライン) を取得し、staging に行ごとにコピー。
        Span<PlacedSubresourceFootPrint> fp = stackalloc PlacedSubresourceFootPrint[1];
        Span<uint> numRows = stackalloc uint[1];
        Span<ulong> rowSizes = stackalloc ulong[1];
        _device.GetCopyableFootprints(desc, 0, 1, 0, fp, numRows, rowSizes, out ulong totalBytes);

        PlacedSubresourceFootPrint footprint = fp[0];
        var staging = (D3D12Buffer)CreateBuffer(totalBytes, GpuMemoryKind.HostMapped);
        uint rowPitch = footprint.Footprint.RowPitch;
        uint srcRowBytes = (uint)rowSizes[0];
        uint rows = numRows[0];
        byte* dst = (byte*)staging.MappedPointer;
        for (uint y = 0; y < rows; y++)
            data.Slice((int)(y * srcRowBytes), (int)srcRowBytes)
                .CopyTo(new Span<byte>(dst + y * rowPitch, (int)srcRowBytes));

        OneShotSubmit(list =>
        {
            list.ResourceBarrierTransition(tex, ResourceStates.Common, ResourceStates.CopyDest);
            var dstLoc = new TextureCopyLocation(tex, 0);
            var srcLoc = new TextureCopyLocation(staging.Resource, footprint);
            list.CopyTextureRegion(dstLoc, 0, 0, 0, srcLoc, null);
            list.ResourceBarrierTransition(tex, ResourceStates.CopyDest, ResourceStates.AllShaderResource);
        });
        staging.Dispose();

        uint index;
        try
        {
            index = _resourceSlots.Allocate();
        }
        catch
        {
            tex.Dispose();
            throw;
        }
        var cpu = new CpuDescriptorHandle(_heapCpuStart, (int)index, _descriptorSize);
        try
        {
            _device.CreateShaderResourceView(tex, null, cpu);
        }
        catch
        {
            _resourceSlots.Free(index);
            tex.Dispose();
            throw;
        }

        return new D3D12Texture(tex, width, height, format, dxgi, default, index, ResourceStates.AllShaderResource,
            () => _resourceSlots.Free(index));
    }

    public IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        Filter f = filter == GpuSamplerFilter.Linear ? Filter.MinMagMipLinear : Filter.MinMagMipPoint;
        TextureAddressMode addr = address == GpuSamplerAddress.Repeat
            ? TextureAddressMode.Wrap : TextureAddressMode.Clamp;
        var desc = new SamplerDescription(f, addr, addr, addr,
            0f, 1u, ComparisonFunction.Never, 0f, float.MaxValue);
        uint index = _samplerSlots.Allocate();
        var cpu = new CpuDescriptorHandle(_samplerStart, (int)index, _samplerSize);
        try
        {
            _device.CreateSampler(ref desc, cpu);
        }
        catch
        {
            _samplerSlots.Free(index);
            throw;
        }
        return new D3D12Sampler(index, () => _samplerSlots.Free(index));
    }

    private void OneShotSubmit(Action<ID3D12GraphicsCommandList> record)
    {
        using ID3D12CommandAllocator allocator = _device.CreateCommandAllocator(CommandListType.Direct);
        using ID3D12GraphicsCommandList list =
            _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, allocator, null);
        record(list);
        list.Close();
        using ID3D12Fence fence = _device.CreateFence(0, FenceFlags.None);
        lock (_queueLock)   // submit と Signal のみ直列化、GPU 完了待ちはロック外 (並列アップロード維持)
        {
            _queue.ExecuteCommandList(list);
            _queue.Signal(fence, 1);
        }
        while (fence.CompletedValue < 1) Thread.Yield();
    }

    private static Format ToDxgiFormat(GpuFormat format) => format switch
    {
        GpuFormat.R8Unorm => Format.R8_UNorm,
        GpuFormat.Rg8Unorm => Format.R8G8_UNorm,
        GpuFormat.Rgba8Unorm => Format.R8G8B8A8_UNorm,
        GpuFormat.Bgra8Unorm => Format.B8G8R8A8_UNorm,
        GpuFormat.Rgba8UnormSrgb => Format.R8G8B8A8_UNorm_SRgb,
        GpuFormat.Bgra8UnormSrgb => Format.B8G8R8A8_UNorm_SRgb,
        GpuFormat.R32Float => Format.R32_Float,
        GpuFormat.D32Float => Format.D32_Float,
        GpuFormat.Depth24PlusStencil8 => Format.D24_UNorm_S8_UInt,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static DepthStencilDescription CreateDepthStencil(GpuDepthStencilState state) => new()
    {
        DepthEnable = state.DepthTest || state.DepthWrite,
        DepthWriteMask = state.DepthWrite ? DepthWriteMask.All : DepthWriteMask.Zero,
        DepthFunc = state.DepthTest ? ToComparison(state.DepthCompare) : ComparisonFunction.Always,
        StencilEnable = state.StencilTest,
        StencilReadMask = (byte)state.StencilReadMask,
        StencilWriteMask = (byte)state.StencilWriteMask,
        FrontFace = ToStencilFace(state.StencilFront),
        BackFace = ToStencilFace(state.StencilBack),
    };
    private static DepthStencilOperationDescription ToStencilFace(GpuStencilFaceState state) => new(
        ToStencilOp(state.FailOp), ToStencilOp(state.DepthFailOp), ToStencilOp(state.PassOp), ToComparison(state.Compare));
    private static ComparisonFunction ToComparison(GpuCompareOp value) => value switch
    {
        GpuCompareOp.Never => ComparisonFunction.Never, GpuCompareOp.Less => ComparisonFunction.Less,
        GpuCompareOp.Equal => ComparisonFunction.Equal, GpuCompareOp.LessEqual => ComparisonFunction.LessEqual,
        GpuCompareOp.Greater => ComparisonFunction.Greater, GpuCompareOp.NotEqual => ComparisonFunction.NotEqual,
        GpuCompareOp.GreaterEqual => ComparisonFunction.GreaterEqual, _ => ComparisonFunction.Always,
    };
    private static StencilOperation ToStencilOp(GpuStencilOp value) => value switch
    {
        GpuStencilOp.Zero => StencilOperation.Zero, GpuStencilOp.Replace => StencilOperation.Replace,
        GpuStencilOp.IncrementClamp => StencilOperation.IncrementSaturate,
        GpuStencilOp.DecrementClamp => StencilOperation.DecrementSaturate,
        GpuStencilOp.Invert => StencilOperation.Invert,
        GpuStencilOp.IncrementWrap => StencilOperation.Increment,
        GpuStencilOp.DecrementWrap => StencilOperation.Decrement,
        _ => StencilOperation.Keep,
    };

    private void DumpDebugMessages()
    {
        var iq = _device.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
        if (iq is null) { Console.Error.WriteLine("[D3D12] InfoQueue 利用不可 (Graphics Tools 未導入?)"); return; }
        ulong n = iq.NumStoredMessages;
        Console.Error.WriteLine($"[D3D12] InfoQueue messages: {n}");
        for (ulong i = 0; i < n; i++)
        {
            var msg = iq.GetMessage(i);
            Console.Error.WriteLine($"  [{msg.Severity}] {msg.Description}");
        }
        iq.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lifecycle?.DeviceEvent(GpuDeviceLifecycleState.Disposing, GpuLifecycleReason.ExplicitDispose, isExpected: true);
        _disposed = true;
        _rootSignature?.Dispose();
        _rtvHeap?.Dispose();
        _samplerHeap?.Dispose();
        _dsvHeap?.Dispose();
        _resourceHeap?.Dispose();
        _queue?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
        _lifecycle?.DeviceEvent(GpuDeviceLifecycleState.Disposed, GpuLifecycleReason.ExplicitDispose, isExpected: true);
    }
}
