using System.Runtime.InteropServices;
using System.Text;
using Luxel.Graphics.Abstraction;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;

namespace Luxel.Graphics.WebGPU;

/// <summary>wgpu-native based headless/offscreen WebGPU backend.</summary>
public sealed unsafe class WebGpuBackend : IGpuBackend
{
    internal const ulong ArenaSize = 64UL * 1024 * 1024;
    internal const uint ArenaAlignment = 256;
    internal const ulong RootBufferSize = 64UL * 1024;

    private static readonly RequestAdapterCallback AdapterCallback = OnAdapter;
    private static readonly RequestDeviceCallback DeviceCallback = OnDevice;
    private static readonly ErrorCallback ErrorCallback = OnError;
    private readonly object _sync = new();
    private readonly List<WebGpuBuffer> _buffers = [];
    private WebGpuApi _api = null!;
    private Instance* _instance;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private WgpuBuffer* _arena;
    private BindGroupLayout* _bindGroupLayout;
    private PipelineLayout* _pipelineLayout;
    private ulong _nextArenaOffset;
    private int _nextTextureIndex;
    private int _nextSamplerIndex;
    private bool _disposed;

    private WebGpuBackend() { }

    public string Name { get; private set; } = "WebGPU";
    public GpuBackendKind Kind => GpuBackendKind.WebGpu;
    public IGpuBackendQueue MainQueue { get; private set; } = null!;

    internal WebGpuApi Api => _api;
    internal Device* Device => _device;
    internal WgpuBuffer* Arena => _arena;
    internal BindGroupLayout* BindGroupLayout => _bindGroupLayout;
    internal PipelineLayout* PipelineLayout => _pipelineLayout;
    internal IReadOnlyList<WebGpuBuffer> Buffers => _buffers;

    public static WebGpuBackend Create()
    {
        var backend = new WebGpuBackend();
        try
        {
            backend.Initialize();
            return backend;
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        _api = WebGpuApi.GetApi();
        var instanceDescriptor = new InstanceDescriptor();
        _instance = _api.CreateInstance(in instanceDescriptor);
        if (_instance == null)
            throw new WebGpuUnavailableException("wgpuCreateInstance returned null. The wgpu-native runtime is unavailable.");

        var adapterState = new AdapterRequestState();
        var adapterHandle = GCHandle.Alloc(adapterState);
        try
        {
            var options = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
            _api.InstanceRequestAdapter(_instance, in options, new PfnRequestAdapterCallback(AdapterCallback), (void*)GCHandle.ToIntPtr(adapterHandle));
            PumpUntil(adapterState.Done, "adapter request");
            if (adapterState.Status != RequestAdapterStatus.Success || adapterState.Adapter == null)
                throw new WebGpuUnavailableException($"No WebGPU adapter is available: {adapterState.Message ?? adapterState.Status.ToString()}.");
            _adapter = adapterState.Adapter;
        }
        finally { adapterHandle.Free(); }

        var properties = new AdapterProperties();
        _api.AdapterGetProperties(_adapter, &properties);
        string adapterName = Marshal.PtrToStringUTF8((nint)properties.Name) ?? "unknown adapter";
        Name = $"WebGPU / {adapterName}";

        var deviceState = new DeviceRequestState();
        var deviceHandle = GCHandle.Alloc(deviceState);
        try
        {
            var descriptor = new DeviceDescriptor();
            _api.AdapterRequestDevice(_adapter, in descriptor, new PfnRequestDeviceCallback(DeviceCallback), (void*)GCHandle.ToIntPtr(deviceHandle));
            PumpUntil(deviceState.Done, "device request");
            if (deviceState.Status != RequestDeviceStatus.Success || deviceState.Device == null)
                throw new WebGpuUnavailableException($"WebGPU device creation failed: {deviceState.Message ?? deviceState.Status.ToString()}.");
            _device = deviceState.Device;
        }
        finally { deviceHandle.Free(); }

        _api.DeviceSetUncapturedErrorCallback(_device, new PfnErrorCallback(ErrorCallback), null);
        _queue = _api.DeviceGetQueue(_device);
        if (_queue == null) throw new WebGpuUnavailableException("The WebGPU device did not expose a default queue.");

        var arenaDescriptor = new BufferDescriptor
        {
            Size = ArenaSize,
            Usage = BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst,
        };
        _arena = _api.DeviceCreateBuffer(_device, in arenaDescriptor);
        if (_arena == null) throw new InvalidOperationException("Failed to create the WebGPU storage arena.");

        CreateFixedLayout();
        MainQueue = new WebGpuQueue(this, _queue, _sync);
    }

    private void CreateFixedLayout()
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment | ShaderStage.Compute,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage, MinBindingSize = 4 },
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = true, MinBindingSize = 256 },
        };
        var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = entries };
        _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, in layoutDescriptor);
        if (_bindGroupLayout == null) throw new InvalidOperationException("Failed to create the fixed WebGPU bind group layout.");

        var layout = _bindGroupLayout;
        var pipelineDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
        _pipelineLayout = _api.DeviceCreatePipelineLayout(_device, in pipelineDescriptor);
        if (_pipelineLayout == null) throw new InvalidOperationException("Failed to create the fixed WebGPU pipeline layout.");
    }

    public IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (size == 0) throw new ArgumentOutOfRangeException(nameof(size));
        lock (_sync)
        {
            ulong offset = AlignUp(_nextArenaOffset, ArenaAlignment);
            ulong allocationSize = AlignUp(size, 4);
            if (offset + allocationSize > ArenaSize)
                throw new OutOfMemoryException($"WebGPU storage arena exhausted ({ArenaSize} bytes).");
            _nextArenaOffset = offset + allocationSize;
            var buffer = new WebGpuBuffer(this, size, offset, checked((uint)(offset / ArenaAlignment)), kind);
            _buffers.Add(buffer);
            return buffer;
        }
    }

    public IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint)
    {
        var module = CreateShaderModule(shaderBlob);
        try
        {
            fixed (byte* entry = Utf8(entryPoint))
            {
                var descriptor = new ComputePipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = entry },
                };
                var pipeline = _api.DeviceCreateComputePipeline(_device, in descriptor);
                if (pipeline == null) throw new InvalidOperationException("Failed to create WebGPU compute pipeline. Check WGSL and the fixed ABI.");
                return new WebGpuPipeline(_api, pipeline);
            }
        }
        finally { _api.ShaderModuleRelease(module); }
    }

    public IGpuBackendPipeline CreateGraphicsPipeline(ReadOnlySpan<byte> vsBlob, string vsEntry,
        ReadOnlySpan<byte> psBlob, string psEntry, GpuRasterDesc raster)
    {
        var vs = CreateShaderModule(vsBlob);
        var ps = CreateShaderModule(psBlob);
        try
        {
            fixed (byte* vsName = Utf8(vsEntry))
            fixed (byte* psName = Utf8(psEntry))
            {
                var blend = CreateBlend(raster.Blend);
                var target = new ColorTargetState
                {
                    Format = MapFormat(raster.ColorFormat),
                    Blend = raster.Blend == GpuBlendMode.None ? null : &blend,
                    WriteMask = ColorWriteMask.All,
                };
                var fragment = new FragmentState { Module = ps, EntryPoint = psName, TargetCount = 1, Targets = &target };
                var depth = CreateDepthState(raster);
                var descriptor = new RenderPipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Vertex = new VertexState { Module = vs, EntryPoint = vsName },
                    Primitive = new PrimitiveState
                    {
                        Topology = raster.Topology == GpuPrimitiveTopology.TriangleStrip ? PrimitiveTopology.TriangleStrip : PrimitiveTopology.TriangleList,
                        FrontFace = raster.FrontFace == GpuFrontFace.Clockwise ? FrontFace.CW : FrontFace.Ccw,
                        CullMode = raster.CullMode switch { GpuCullMode.Front => CullMode.Front, GpuCullMode.Back => CullMode.Back, _ => CullMode.None },
                    },
                    DepthStencil = raster.DepthTest || raster.DepthWrite ? &depth : null,
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    Fragment = &fragment,
                };
                var pipeline = _api.DeviceCreateRenderPipeline(_device, in descriptor);
                if (pipeline == null) throw new InvalidOperationException("Failed to create WebGPU render pipeline. Check WGSL and the fixed ABI.");
                return new WebGpuPipeline(_api, pipeline);
            }
        }
        finally
        {
            _api.ShaderModuleRelease(vs);
            _api.ShaderModuleRelease(ps);
        }
    }

    public IGpuBackendTexture CreateRenderTarget(uint width, uint height, GpuFormat format)
        => CreateTexture(width, height, format, TextureUsage.RenderAttachment | TextureUsage.CopySrc);

    public IGpuBackendTexture CreateDepthTarget(uint width, uint height, GpuFormat format)
        => CreateTexture(width, height, format, TextureUsage.RenderAttachment);

    public IGpuBackendTexture CreateSampledTexture(uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data)
    {
        var texture = CreateTexture(width, height, format, TextureUsage.TextureBinding | TextureUsage.CopyDst, (uint)Interlocked.Increment(ref _nextTextureIndex));
        uint bytesPerPixel = 4;
        uint bytesPerRow = width * bytesPerPixel;
        if ((ulong)data.Length < (ulong)bytesPerRow * height) throw new ArgumentException("Texture data is too small.", nameof(data));
        var destination = new ImageCopyTexture { Texture = texture.Handle, Aspect = TextureAspect.All };
        var layout = new TextureDataLayout { BytesPerRow = bytesPerRow, RowsPerImage = height };
        var extent = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
        fixed (byte* source = data)
            _api.QueueWriteTexture(_queue, in destination, source, (nuint)data.Length, in layout, in extent);
        return texture;
    }

    public IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        var mode = address == GpuSamplerAddress.Repeat ? AddressMode.Repeat : AddressMode.ClampToEdge;
        var minMag = filter == GpuSamplerFilter.Point ? FilterMode.Nearest : FilterMode.Linear;
        var descriptor = new SamplerDescriptor
        {
            AddressModeU = mode, AddressModeV = mode, AddressModeW = mode,
            MinFilter = minMag, MagFilter = minMag,
            MipmapFilter = filter == GpuSamplerFilter.Point ? MipmapFilterMode.Nearest : MipmapFilterMode.Linear,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        };
        var sampler = _api.DeviceCreateSampler(_device, in descriptor);
        if (sampler == null) throw new InvalidOperationException("Failed to create WebGPU sampler.");
        return new WebGpuSampler(_api, sampler, (uint)Interlocked.Increment(ref _nextSamplerIndex));
    }

    public IGpuBackendSurface CreateSurface(nint windowHandle, uint width, uint height)
        => throw new PlatformNotSupportedException("Luxel WebGPU currently supports headless/offscreen operation only; window surfaces are not implemented.");

    private WebGpuTexture CreateTexture(uint width, uint height, GpuFormat format, TextureUsage usage, uint bindlessIndex = 0)
    {
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        var descriptor = new TextureDescriptor
        {
            Usage = usage,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
            Format = MapFormat(format),
            MipLevelCount = 1,
            SampleCount = 1,
        };
        var texture = _api.DeviceCreateTexture(_device, in descriptor);
        if (texture == null) throw new InvalidOperationException("Failed to create WebGPU texture.");
        var viewDescriptor = new TextureViewDescriptor
        {
            Format = descriptor.Format,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };
        var view = _api.TextureCreateView(texture, in viewDescriptor);
        if (view == null) { _api.TextureRelease(texture); throw new InvalidOperationException("Failed to create WebGPU texture view."); }
        return new WebGpuTexture(_api, texture, view, width, height, format, bindlessIndex);
    }

    private ShaderModule* CreateShaderModule(ReadOnlySpan<byte> shaderBlob)
    {
        byte[] code = new byte[shaderBlob.Length + 1];
        shaderBlob.CopyTo(code);
        fixed (byte* text = code)
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = text,
            };
            var descriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            var module = _api.DeviceCreateShaderModule(_device, in descriptor);
            if (module == null) throw new InvalidOperationException("Failed to create WebGPU WGSL shader module.");
            return module;
        }
    }

    internal BindGroup* CreateCommandBindGroup(WgpuBuffer* rootBuffer)
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = _arena, Size = ArenaSize };
        entries[1] = new BindGroupEntry { Binding = 1, Buffer = rootBuffer, Size = 256 };
        var descriptor = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 2, Entries = entries };
        var group = _api.DeviceCreateBindGroup(_device, in descriptor);
        if (group == null) throw new InvalidOperationException("Failed to create WebGPU command bind group.");
        return group;
    }

    internal void RemoveBuffer(WebGpuBuffer buffer)
    {
        lock (_sync) _buffers.Remove(buffer);
    }

    private void PumpUntil(Func<bool> done, string operation)
    {
        for (int i = 0; i < 10000 && !done(); i++)
        {
            _api.InstanceProcessEvents(_instance);
            Thread.Sleep(1);
        }
        if (!done()) throw new TimeoutException($"Timed out waiting for WebGPU {operation}.");
    }

    private static void OnAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userData)
    {
        var state = (AdapterRequestState)GCHandle.FromIntPtr((nint)userData).Target!;
        state.Status = status; state.Adapter = adapter; state.Message = Marshal.PtrToStringUTF8((nint)message); state.Completed = true;
    }

    private static void OnDevice(RequestDeviceStatus status, Device* device, byte* message, void* userData)
    {
        var state = (DeviceRequestState)GCHandle.FromIntPtr((nint)userData).Target!;
        state.Status = status; state.Device = device; state.Message = Marshal.PtrToStringUTF8((nint)message); state.Completed = true;
    }

    private static void OnError(ErrorType type, byte* message, void* userData)
        => Console.Error.WriteLine($"[WebGPU {type}] {Marshal.PtrToStringUTF8((nint)message)}");

    internal static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');
    internal static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);

    internal static TextureFormat MapFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => TextureFormat.Rgba8Unorm,
        GpuFormat.Bgra8Unorm => TextureFormat.Bgra8Unorm,
        GpuFormat.R32Float => TextureFormat.R32float,
        GpuFormat.D32Float => TextureFormat.Depth32float,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static BlendState CreateBlend(GpuBlendMode mode) => mode == GpuBlendMode.AlphaBlend
        ? new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha },
        }
        : default;

    private static DepthStencilState CreateDepthState(GpuRasterDesc raster) => new()
    {
        Format = MapFormat(raster.DepthFormat),
        DepthWriteEnabled = raster.DepthWrite,
        DepthCompare = raster.DepthTest ? CompareFunction.LessEqual : CompareFunction.Always,
        StencilFront = new StencilFaceState { Compare = CompareFunction.Always },
        StencilBack = new StencilFaceState { Compare = CompareFunction.Always },
        StencilReadMask = uint.MaxValue,
        StencilWriteMask = uint.MaxValue,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (MainQueue is WebGpuQueue queue) queue.WaitIdle();
        foreach (var buffer in _buffers.ToArray()) buffer.Dispose();
        _buffers.Clear();
        if (_pipelineLayout != null) _api.PipelineLayoutRelease(_pipelineLayout);
        if (_bindGroupLayout != null) _api.BindGroupLayoutRelease(_bindGroupLayout);
        if (_arena != null) { _api.BufferDestroy(_arena); _api.BufferRelease(_arena); }
        if (_queue != null) _api.QueueRelease(_queue);
        if (_device != null) _api.DeviceRelease(_device);
        if (_adapter != null) _api.AdapterRelease(_adapter);
        if (_instance != null) _api.InstanceRelease(_instance);
        _api?.Dispose();
        _instance = null; _adapter = null; _device = null; _queue = null;
    }

    private sealed class AdapterRequestState
    {
        public volatile bool Completed;
        public RequestAdapterStatus Status;
        public Adapter* Adapter;
        public string? Message;
        public bool Done() => Completed;
    }

    private sealed class DeviceRequestState
    {
        public volatile bool Completed;
        public RequestDeviceStatus Status;
        public Device* Device;
        public string? Message;
        public bool Done() => Completed;
    }
}

public sealed class WebGpuUnavailableException(string message) : InvalidOperationException(message);
