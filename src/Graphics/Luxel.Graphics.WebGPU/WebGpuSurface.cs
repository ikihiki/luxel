using System.Runtime.InteropServices;
using Luxel.Graphics.Abstraction;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;

namespace Luxel.Graphics.WebGPU;

internal sealed unsafe class WebGpuSurface : IGpuBackendSurface
{
    private const string BlitWgsl = """
        struct PresentInfo { stride: u32, width: u32, height: u32, unused: u32 }
        @group(0) @binding(0) var<storage, read> pixels: array<u32>;
        @group(0) @binding(1) var<uniform> info: PresentInfo;

        struct VertexOutput {
            @builtin(position) position: vec4f,
        }

        @vertex fn vsMain(@builtin(vertex_index) vertexIndex: u32) -> VertexOutput {
            var positions = array<vec2f, 3>(
                vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            var output: VertexOutput;
            output.position = vec4f(positions[vertexIndex], 0.0, 1.0);
            return output;
        }

        @fragment fn fsMain(@builtin(position) position: vec4f) -> @location(0) vec4f {
            let x = min(u32(position.x), info.width - 1u);
            let y = min(u32(position.y), info.height - 1u);
            let packed = pixels[y * info.stride + x];
            return vec4f(
                f32(packed & 255u),
                f32((packed >> 8u) & 255u),
                f32((packed >> 16u) & 255u),
                f32((packed >> 24u) & 255u)) / 255.0;
        }
        """;

    private readonly WebGpuBackend _backend;
    private readonly WebGpuApi _api;
    private Surface* _surface;
    private TextureFormat _format;
    private BindGroupLayout* _bindGroupLayout;
    private PipelineLayout* _pipelineLayout;
    private RenderPipeline* _pipeline;
    private WgpuBuffer* _presentInfo;
    private uint _width;
    private uint _height;
    private bool _configured;
    private bool _disposed;

    private WebGpuSurface(WebGpuBackend backend, Surface* surface, uint width, uint height)
    {
        _backend = backend;
        _api = backend.Api;
        _surface = surface;
        try
        {
            SelectFormat();
            CreatePipeline();
            var infoDescriptor = new BufferDescriptor { Size = 16, Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
            _presentInfo = _api.DeviceCreateBuffer(_backend.Device, in infoDescriptor);
            if (_presentInfo == null) throw new InvalidOperationException("Failed to create the WebGPU presentation uniform buffer.");
            Resize(width, height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Present(IGpuBackendBuffer source, uint srcStridePixels, uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source is not WebGpuBuffer buffer || !ReferenceEquals(buffer.Owner, _backend))
            throw new ArgumentException("The presentation buffer belongs to another backend.", nameof(source));
        if (srcStridePixels < width) throw new ArgumentOutOfRangeException(nameof(srcStridePixels));
        if (width == 0 || height == 0) return;
        ulong required = checked(((ulong)(height - 1) * srcStridePixels + width) * 4);
        if (required > buffer.Size) throw new ArgumentException("The presentation buffer is too small.", nameof(source));

        lock (_backend.Sync)
        {
            if (!_configured || _width != width || _height != height) Configure(width, height);
            PresentInfo info = new(srcStridePixels, width, height, 0);
            _api.QueueWriteBuffer(_backend.Queue, _presentInfo, 0, &info, 16);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var current = new SurfaceTexture();
                _api.SurfaceGetCurrentTexture(_surface, &current);
                if (current.Status == SurfaceGetCurrentTextureStatus.Success && current.Texture != null)
                {
                    try { EncodeAndPresent(current.Texture, buffer); }
                    finally { _api.TextureRelease(current.Texture); }
                    return;
                }
                if (current.Texture != null) _api.TextureRelease(current.Texture);

                if (current.Status is SurfaceGetCurrentTextureStatus.Outdated or SurfaceGetCurrentTextureStatus.Lost)
                {
                    Configure(_width, _height);
                    continue;
                }
                if (current.Status == SurfaceGetCurrentTextureStatus.Timeout) return;
                throw new InvalidOperationException($"WebGPU surface acquisition failed: {current.Status}.");
            }
            throw new InvalidOperationException("WebGPU surface acquisition remained outdated after reconfiguration.");
        }
    }

    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_backend.Sync)
        {
            _width = width;
            _height = height;
            if (width == 0 || height == 0)
            {
                if (_configured) _api.SurfaceUnconfigure(_surface);
                _configured = false;
                return;
            }
            Configure(width, height);
        }
    }

    internal static WebGpuSurface CreateXlib(WebGpuBackend backend, nint display, ulong window, uint width, uint height)
    {
        if (display == 0) throw new ArgumentException("A non-zero Xlib Display is required.", nameof(display));
        if (window == 0) throw new ArgumentException("A non-zero Xlib Window is required.", nameof(window));
        var xlib = new SurfaceDescriptorFromXlibWindow
        {
            Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromXlibWindow },
            Display = (void*)display,
            Window = window,
        };
        var descriptor = new SurfaceDescriptor { NextInChain = &xlib.Chain };
        Surface* surface = backend.Api.InstanceCreateSurface(backend.Instance, in descriptor);
        if (surface == null) throw new InvalidOperationException("wgpuInstanceCreateSurface returned null for Xlib.");
        return new WebGpuSurface(backend, surface, width, height);
    }

    internal static WebGpuSurface CreateWin32(WebGpuBackend backend, nint hinstance, nint hwnd, uint width, uint height)
    {
        if (hwnd == 0) throw new ArgumentException("A non-zero Win32 HWND is required.", nameof(hwnd));
        var win32 = new SurfaceDescriptorFromWindowsHWND
        {
            Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromWindowsHwnd },
            Hinstance = (void*)hinstance,
            Hwnd = (void*)hwnd,
        };
        var descriptor = new SurfaceDescriptor { NextInChain = &win32.Chain };
        Surface* surface = backend.Api.InstanceCreateSurface(backend.Instance, in descriptor);
        if (surface == null) throw new InvalidOperationException("wgpuInstanceCreateSurface returned null for Win32.");
        return new WebGpuSurface(backend, surface, width, height);
    }

    private void SelectFormat()
    {
        var capabilities = new SurfaceCapabilities();
        _api.SurfaceGetCapabilities(_surface, _backend.Adapter, &capabilities);
        try
        {
            if (capabilities.FormatCount == 0 || capabilities.Formats == null)
                throw new PlatformNotSupportedException("The selected WebGPU adapter cannot present to this surface.");
            _format = capabilities.Formats[0];
            for (nuint i = 0; i < capabilities.FormatCount; i++)
            {
                if (capabilities.Formats[i] is TextureFormat.Bgra8Unorm or TextureFormat.Rgba8Unorm)
                {
                    _format = capabilities.Formats[i];
                    break;
                }
            }
        }
        finally { _api.SurfaceCapabilitiesFreeMembers(capabilities); }
    }

    private void Configure(uint width, uint height)
    {
        if (width == 0 || height == 0) return;
        var configuration = new SurfaceConfiguration
        {
            Device = _backend.Device,
            Format = _format,
            Usage = TextureUsage.RenderAttachment,
            Width = width,
            Height = height,
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto,
        };
        _api.SurfaceConfigure(_surface, in configuration);
        _width = width;
        _height = height;
        _configured = true;
        _backend.ProcessEventsAndThrowValidationErrors("surface configuration");
    }

    private void CreatePipeline()
    {
        var layoutEntries = stackalloc BindGroupLayoutEntry[2];
        layoutEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 4 },
        };
        layoutEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 16 },
        };
        var bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = layoutEntries };
        _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_backend.Device, in bindGroupLayoutDescriptor);
        if (_bindGroupLayout == null) throw new InvalidOperationException("Failed to create the WebGPU presentation bind group layout.");
        BindGroupLayout** layouts = stackalloc BindGroupLayout*[1] { _bindGroupLayout };
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = layouts };
        _pipelineLayout = _api.DeviceCreatePipelineLayout(_backend.Device, in pipelineLayoutDescriptor);
        if (_pipelineLayout == null) throw new InvalidOperationException("Failed to create the WebGPU presentation pipeline layout.");

        byte[] code = System.Text.Encoding.UTF8.GetBytes(BlitWgsl + "\0");
        fixed (byte* text = code)
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = text,
            };
            var moduleDescriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            ShaderModule* module = _api.DeviceCreateShaderModule(_backend.Device, in moduleDescriptor);
            if (module == null) throw new InvalidOperationException("Failed to create the WebGPU presentation shader.");
            try
            {
                fixed (byte* vs = "vsMain\0"u8)
                fixed (byte* fs = "fsMain\0"u8)
                {
                    var target = new ColorTargetState { Format = _format, WriteMask = ColorWriteMask.All };
                    var fragment = new FragmentState { Module = module, EntryPoint = fs, TargetCount = 1, Targets = &target };
                    var pipelineDescriptor = new RenderPipelineDescriptor
                    {
                        Layout = _pipelineLayout,
                        Vertex = new VertexState { Module = module, EntryPoint = vs },
                        Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                        Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                        Fragment = &fragment,
                    };
                    _pipeline = _api.DeviceCreateRenderPipeline(_backend.Device, in pipelineDescriptor);
                }
            }
            finally { _api.ShaderModuleRelease(module); }
        }
        _backend.ProcessEventsAndThrowValidationErrors("surface presentation pipeline creation");
        if (_pipeline == null) throw new InvalidOperationException("Failed to create the WebGPU presentation pipeline.");
    }

    private void EncodeAndPresent(Texture* texture, WebGpuBuffer buffer)
    {
        TextureView* view = _api.TextureCreateView(texture, null);
        if (view == null) throw new InvalidOperationException("Failed to create a view for the current WebGPU surface texture.");
        BindGroup* bindGroup = null;
        CommandEncoder* encoder = null;
        CommandBuffer* command = null;
        try
        {
            var entries = stackalloc BindGroupEntry[2];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = _backend.Arena, Offset = buffer.Offset, Size = buffer.PhysicalSize };
            entries[1] = new BindGroupEntry { Binding = 1, Buffer = _presentInfo, Offset = 0, Size = 16 };
            var bindGroupDescriptor = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 2, Entries = entries };
            bindGroup = _api.DeviceCreateBindGroup(_backend.Device, in bindGroupDescriptor);
            if (bindGroup == null) throw new InvalidOperationException("Failed to bind the WebGPU presentation buffer.");
            var encoderDescriptor = new CommandEncoderDescriptor();
            encoder = _api.DeviceCreateCommandEncoder(_backend.Device, in encoderDescriptor);
            if (encoder == null) throw new InvalidOperationException("Failed to create the WebGPU presentation encoder.");
            var attachment = new RenderPassColorAttachment
            {
                View = view,
                DepthSlice = WebGpuApi.DepthSliceUndefined,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { A = 1 },
            };
            var passDescriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
            RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(encoder, in passDescriptor);
            _api.RenderPassEncoderSetPipeline(pass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
            _api.RenderPassEncoderEnd(pass);
            _api.RenderPassEncoderRelease(pass);
            var commandDescriptor = new CommandBufferDescriptor();
            command = _api.CommandEncoderFinish(encoder, in commandDescriptor);
            if (command == null) throw new InvalidOperationException("Failed to finish WebGPU presentation commands.");
            _api.QueueSubmit(_backend.Queue, 1, &command);
            _api.SurfacePresent(_surface);
            _backend.ProcessEventsAndThrowValidationErrors("surface presentation");
        }
        finally
        {
            if (command != null) _api.CommandBufferRelease(command);
            if (encoder != null) _api.CommandEncoderRelease(encoder);
            if (bindGroup != null) _api.BindGroupRelease(bindGroup);
            _api.TextureViewRelease(view);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_configured && _surface != null) _api.SurfaceUnconfigure(_surface);
        if (_presentInfo != null) { _api.BufferDestroy(_presentInfo); _api.BufferRelease(_presentInfo); }
        if (_pipeline != null) _api.RenderPipelineRelease(_pipeline);
        if (_pipelineLayout != null) _api.PipelineLayoutRelease(_pipelineLayout);
        if (_bindGroupLayout != null) _api.BindGroupLayoutRelease(_bindGroupLayout);
        if (_surface != null) _api.SurfaceRelease(_surface);
        _presentInfo = null;
        _pipeline = null;
        _pipelineLayout = null;
        _bindGroupLayout = null;
        _surface = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PresentInfo(uint Stride, uint Width, uint Height, uint Unused);
}
