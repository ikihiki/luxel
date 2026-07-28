using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Luxel.Graphics.Abstraction;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
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
    private readonly List<ArenaRange> _freeArenaRanges = [new(0, ArenaSize)];
    private readonly ConcurrentQueue<string> _validationErrors = new();
    private WebGpuApi _api = null!;
    private Instance* _instance;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private WgpuBuffer* _arena;
    private BindGroupLayout* _computeBindGroupLayout;
    private BindGroupLayout* _graphicsBindGroupLayout;
    private PipelineLayout* _computePipelineLayout;
    private PipelineLayout* _graphicsPipelineLayout;
    private Wgpu _native = null!;
    private GCHandle _errorHandle;
    private int _activeCommands;
    private bool _disposed;

    private WebGpuBackend() { }

    public string Name { get; private set; } = "WebGPU";
    public GpuBackendKind Kind => GpuBackendKind.WebGpu;
    public IGpuBackendQueue MainQueue { get; private set; } = null!;

    internal WebGpuApi Api => _api;
    internal Device* Device => _device;
    internal WgpuBuffer* Arena => _arena;
    internal IReadOnlyList<WebGpuBuffer> Buffers { get { ThrowIfDisposed(); return _buffers; } }
    internal bool IsDisposed => _disposed;

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
        {
            var options = new RequestAdapterOptions
            {
                PowerPreference = PowerPreference.HighPerformance,
                ForceFallbackAdapter = Environment.GetEnvironmentVariable("LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER") is "1" or "true" or "True",
            };
            _api.InstanceRequestAdapter(_instance, in options, new PfnRequestAdapterCallback(AdapterCallback), (void*)GCHandle.ToIntPtr(adapterHandle));
            PumpUntil(adapterState.Done, "adapter request");
            if (adapterState.Status != RequestAdapterStatus.Success || adapterState.Adapter == null)
                throw new WebGpuUnavailableException($"No WebGPU adapter is available: {adapterState.Message ?? adapterState.Status.ToString()}.");
            _adapter = adapterState.Adapter;
        }

        var properties = new AdapterProperties();
        _api.AdapterGetProperties(_adapter, &properties);
        string adapterName = Marshal.PtrToStringUTF8((nint)properties.Name) ?? "unknown adapter";
        Name = $"WebGPU / {adapterName}";

        var deviceState = new DeviceRequestState();
        var deviceHandle = GCHandle.Alloc(deviceState);
        {
            var descriptor = new DeviceDescriptor();
            _api.AdapterRequestDevice(_adapter, in descriptor, new PfnRequestDeviceCallback(DeviceCallback), (void*)GCHandle.ToIntPtr(deviceHandle));
            PumpUntil(deviceState.Done, "device request");
            if (deviceState.Status != RequestDeviceStatus.Success || deviceState.Device == null)
                throw new WebGpuUnavailableException($"WebGPU device creation failed: {deviceState.Message ?? deviceState.Status.ToString()}.");
            _device = deviceState.Device;
        }

        if (!_api.TryGetDeviceExtension(_device, out _native!))
            throw new WebGpuUnavailableException("The wgpu-native DevicePoll extension is unavailable.");
        _errorHandle = GCHandle.Alloc(this);
        _api.DeviceSetUncapturedErrorCallback(_device, new PfnErrorCallback(ErrorCallback), (void*)GCHandle.ToIntPtr(_errorHandle));
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
        CreateFixedLayout(BufferBindingType.Storage, ShaderStage.Compute, out _computeBindGroupLayout, out _computePipelineLayout);
        CreateFixedLayout(BufferBindingType.ReadOnlyStorage, ShaderStage.Vertex | ShaderStage.Fragment,
            out _graphicsBindGroupLayout, out _graphicsPipelineLayout);
    }

    private void CreateFixedLayout(BufferBindingType arenaType, ShaderStage arenaVisibility,
        out BindGroupLayout* bindGroupLayout, out PipelineLayout* pipelineLayout)
    {
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = arenaVisibility,
            Buffer = new BufferBindingLayout { Type = arenaType, MinBindingSize = 4 },
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = true, MinBindingSize = 256 },
        };
        var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = entries };
        bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, in layoutDescriptor);
        if (bindGroupLayout == null) throw new InvalidOperationException("Failed to create a fixed WebGPU bind group layout.");

        var layout = bindGroupLayout;
        var pipelineDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
        pipelineLayout = _api.DeviceCreatePipelineLayout(_device, in pipelineDescriptor);
        if (pipelineLayout == null) throw new InvalidOperationException("Failed to create a fixed WebGPU pipeline layout.");
    }

    public IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (size == 0) throw new ArgumentOutOfRangeException(nameof(size));
        lock (_sync)
        {
            ulong physicalSize = CheckedAlignUp(size, 4);
            ulong allocationSize = CheckedAlignUp(physicalSize, ArenaAlignment);
            for (int i = 0; i < _freeArenaRanges.Count; i++)
            {
                ArenaRange range = _freeArenaRanges[i];
                if (range.Size < allocationSize) continue;
                ulong offset = range.Offset;
                if (range.Size == allocationSize) _freeArenaRanges.RemoveAt(i);
                else _freeArenaRanges[i] = new ArenaRange(checked(offset + allocationSize), range.Size - allocationSize);
                var buffer = new WebGpuBuffer(this, size, physicalSize, allocationSize, offset,
                    checked((uint)(offset / ArenaAlignment)), kind);
                _buffers.Add(buffer);
                return buffer;
            }
            throw new OutOfMemoryException($"WebGPU storage arena exhausted ({ArenaSize} bytes).");
        }
    }

    public IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint)
    {
        ThrowIfDisposed();
        DrainValidationErrors();
        var module = CreateShaderModule(shaderBlob);
        try
        {
            fixed (byte* entry = Utf8(entryPoint))
            {
                var descriptor = new ComputePipelineDescriptor
                {
                    Layout = _computePipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = entry },
                };
                var pipeline = _api.DeviceCreateComputePipeline(_device, in descriptor);
                try { ProcessEventsAndThrowValidationErrors("compute pipeline creation"); }
                catch { if (pipeline != null) _api.ComputePipelineRelease(pipeline); throw; }
                if (pipeline == null) throw new InvalidOperationException("Failed to create WebGPU compute pipeline. Check WGSL and the fixed ABI.");
                return new WebGpuPipeline(this, pipeline);
            }
        }
        finally { _api.ShaderModuleRelease(module); }
    }

    public IGpuBackendPipeline CreateGraphicsPipeline(ReadOnlySpan<byte> vsBlob, string vsEntry,
        ReadOnlySpan<byte> psBlob, string psEntry, GpuRasterDesc raster)
    {
        ThrowIfDisposed();
        DrainValidationErrors();
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
                    Layout = _graphicsPipelineLayout,
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
                try { ProcessEventsAndThrowValidationErrors("graphics pipeline creation"); }
                catch { if (pipeline != null) _api.RenderPipelineRelease(pipeline); throw; }
                if (pipeline == null) throw new InvalidOperationException("Failed to create WebGPU render pipeline. Check WGSL and the fixed ABI.");
                return new WebGpuPipeline(this, pipeline);
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
        ThrowIfDisposed();
        throw new NotSupportedException("The current WebGPU fixed ABI does not expose sampled textures. Use render targets/readback only.");
    }

    public IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
    {
        ThrowIfDisposed();
        throw new NotSupportedException("The current WebGPU fixed ABI does not expose samplers.");
    }

    public IGpuBackendSurface CreateSurface(nint windowHandle, uint width, uint height)
    {
        ThrowIfDisposed();
        throw new PlatformNotSupportedException("Luxel WebGPU currently supports headless/offscreen operation only; window surfaces are not implemented.");
    }

    private WebGpuTexture CreateTexture(uint width, uint height, GpuFormat format, TextureUsage usage, uint bindlessIndex = 0)
    {
        ThrowIfDisposed();
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
        return new WebGpuTexture(this, texture, view, width, height, format, bindlessIndex);
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

    internal BindGroup* CreateCommandBindGroup(WgpuBuffer* rootBuffer, bool compute)
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, Buffer = _arena, Size = ArenaSize };
        entries[1] = new BindGroupEntry { Binding = 1, Buffer = rootBuffer, Size = 256 };
        var descriptor = new BindGroupDescriptor
        {
            Layout = compute ? _computeBindGroupLayout : _graphicsBindGroupLayout,
            EntryCount = 2,
            Entries = entries,
        };
        var group = _api.DeviceCreateBindGroup(_device, in descriptor);
        if (group == null) throw new InvalidOperationException("Failed to create WebGPU command bind group.");
        return group;
    }

    internal bool TryRetireBuffer(WebGpuBuffer buffer, ulong offset, ulong size)
    {
        lock (_sync)
        {
            if (_activeCommands != 0) return false;
            _buffers.Remove(buffer);
            int index = 0;
            while (index < _freeArenaRanges.Count && _freeArenaRanges[index].Offset < offset) index++;
            _freeArenaRanges.Insert(index, new ArenaRange(offset, size));
            if (index > 0 && checked(_freeArenaRanges[index - 1].Offset + _freeArenaRanges[index - 1].Size) == offset)
            {
                ArenaRange previous = _freeArenaRanges[index - 1];
                _freeArenaRanges[index - 1] = new ArenaRange(previous.Offset, checked(previous.Size + size));
                _freeArenaRanges.RemoveAt(index--);
            }
            if (index + 1 < _freeArenaRanges.Count && checked(_freeArenaRanges[index].Offset + _freeArenaRanges[index].Size) == _freeArenaRanges[index + 1].Offset)
            {
                ArenaRange current = _freeArenaRanges[index];
                _freeArenaRanges[index] = new ArenaRange(current.Offset, checked(current.Size + _freeArenaRanges[index + 1].Size));
                _freeArenaRanges.RemoveAt(index + 1);
            }
            return true;
        }
    }

    internal void RegisterCommand()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            checked { _activeCommands++; }
        }
    }

    internal void CompleteCommand()
    {
        WebGpuBuffer[] buffers;
        lock (_sync)
        {
            if (_activeCommands <= 0) return;
            _activeCommands--;
            if (_activeCommands != 0) return;
            buffers = _buffers.ToArray();
        }
        foreach (WebGpuBuffer buffer in buffers) buffer.TryRetire();
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
        => CompleteRequest<AdapterRequestState>(userData, state => { state.Status = status; state.Adapter = adapter; state.Message = Marshal.PtrToStringUTF8((nint)message); state.Completed = true; });

    private static void OnDevice(RequestDeviceStatus status, Device* device, byte* message, void* userData)
        => CompleteRequest<DeviceRequestState>(userData, state => { state.Status = status; state.Device = device; state.Message = Marshal.PtrToStringUTF8((nint)message); state.Completed = true; });

    private static void CompleteRequest<T>(void* userData, Action<T> complete) where T : class
    {
        GCHandle handle = GCHandle.FromIntPtr((nint)userData);
        try { if (handle.Target is T state) complete(state); }
        catch (Exception exception) { Console.Error.WriteLine($"WebGPU callback failed: {exception}"); }
        finally { handle.Free(); }
    }

    private static void OnError(ErrorType type, byte* message, void* userData)
    {
        if (userData == null) return;
        try
        {
            if (GCHandle.FromIntPtr((nint)userData).Target is WebGpuBackend backend)
                backend._validationErrors.Enqueue($"{type}: {Marshal.PtrToStringUTF8((nint)message)}");
        }
        catch (Exception exception) { Console.Error.WriteLine($"WebGPU error callback failed: {exception}"); }
    }

    internal void ProcessEventsAndThrowValidationErrors(string operation)
    {
        ThrowIfDisposed();
        _native.DevicePoll(_device, false, null);
        if (_validationErrors.IsEmpty) return;
        var messages = new List<string>();
        while (_validationErrors.TryDequeue(out string? message)) messages.Add(message);
        throw new InvalidOperationException($"WebGPU {operation} failed: {string.Join(" | ", messages)}");
    }

    private void DrainValidationErrors() { while (_validationErrors.TryDequeue(out _)) { } }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    internal static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');
    internal static ulong AlignUp(ulong value, ulong alignment) => CheckedAlignUp(value, alignment);
    internal static ulong CheckedAlignUp(ulong value, ulong alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(alignment));
        return checked(value + alignment - 1) & ~(alignment - 1);
    }

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
        if (_device != null && MainQueue is WebGpuQueue queue) queue.WaitIdleForDispose();
        lock (_sync) _activeCommands = 0;
        foreach (var buffer in _buffers.ToArray()) buffer.Dispose();
        _buffers.Clear();
        if (_computePipelineLayout != null) _api.PipelineLayoutRelease(_computePipelineLayout);
        if (_graphicsPipelineLayout != null) _api.PipelineLayoutRelease(_graphicsPipelineLayout);
        if (_computeBindGroupLayout != null) _api.BindGroupLayoutRelease(_computeBindGroupLayout);
        if (_graphicsBindGroupLayout != null) _api.BindGroupLayoutRelease(_graphicsBindGroupLayout);
        if (_arena != null) { _api.BufferDestroy(_arena); _api.BufferRelease(_arena); }
        if (_queue != null) _api.QueueRelease(_queue);
        if (_device != null) { _api.DeviceSetUncapturedErrorCallback(_device, default, null); _api.DeviceRelease(_device); }
        if (_errorHandle.IsAllocated) _errorHandle.Free();
        if (_adapter != null) _api.AdapterRelease(_adapter);
        if (_instance != null) _api.InstanceRelease(_instance);
        _api?.Dispose();
        _instance = null; _adapter = null; _device = null; _queue = null;
    }

    private readonly record struct ArenaRange(ulong Offset, ulong Size);

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
