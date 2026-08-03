using Luxel;
using Luxel.Platform.Abstraction;
using Luxel.Graphics.Vulkan;
#if LUXEL_WINDOWS
using Luxel.Platform.Windows;
#else
using Luxel.Platform.Silk;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
#endif

string backend = args.FirstOrDefault(a => a is "vk" or "vulkan" or "dx" or "d3d12" or "webgpu" or "wgpu")?.ToLowerInvariant() ?? "vk";
int? frameLimit = ParseFrameLimit(args);
TutorialStage stage = ParseStage(args);
(int initialWidth, int initialHeight) = ParseSize(args);

#if LUXEL_WINDOWS
int exitCode = 1;
var thread = new Thread(() => exitCode = Run(backend, frameLimit, stage, initialWidth, initialHeight)) { Name = "LuxelTriangle-Main" };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exitCode;
#else
return Run(backend, frameLimit, stage, initialWidth, initialHeight);
#endif

// docs:begin standalone-frame-loop
static int Run(string backend, int? frameLimit, TutorialStage stage, int initialWidth, int initialHeight)
{
    try
    {
#if LUXEL_WINDOWS
        using var windows = new WindowSystem(Win32WindowBackend.Create());
#else
        if (backend is "dx" or "d3d12")
            throw new PlatformNotSupportedException("DirectX 12 は Windows でのみ利用できます。Linux では 'vk' を指定してください。");
        using var windows = new WindowSystem(SilkWindowBackend.Create());
#endif
        Window window = windows.CreateWindow(new WindowDesc($"Luxel — 3D Tutorial ({stage})", initialWidth, initialHeight));
        using GpuDevice device = CreateDevice(backend, window);
        using GpuSurface surface = CreateSurface(device, window);
        using var renderer = new TriangleRenderer(device, stage);

        int width = Math.Max(0, window.Width);
        int height = Math.Max(0, window.Height);
        bool resizePending = true;
        window.Resized += (w, h) =>
        {
            width = Math.Max(0, w);
            height = Math.Max(0, h);
            resizePending = true;
        };

        int renderedFrames = 0;
        while (windows.Pump())
        {
            if (resizePending)
            {
                device.MainQueue.WaitIdle();
                if (width > 0 && height > 0)
                    surface.Resize((uint)width, (uint)height);
                renderer.Resize(width, height);
                resizePending = false;
            }

            if (width == 0 || height == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            renderer.Render();
            surface.Present(renderer.Framebuffer, renderer.StridePixels, (uint)width, (uint)height);
            renderedFrames++;

            if (frameLimit is int limit && renderedFrames >= limit)
            {
                window.Close();
                windows.Pump();
                break;
            }
        }

        device.MainQueue.WaitIdle();
        Console.WriteLine($"tutorial-3d: {renderedFrames} frame(s), stage={stage}, backend={backend}, device={device.Name}, aspect={renderer.AspectRatio:F4}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 1;
    }
}

// docs:end standalone-frame-loop

// docs:begin device-and-surface-backend
static GpuDevice CreateDevice(string backend, Window window)
{
#if LUXEL_WINDOWS
    return backend switch
    {
        "dx" or "d3d12" => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
        "webgpu" or "wgpu" => new GpuDevice(Luxel.Graphics.WebGPU.WebGpuBackend.Create()),
        _ => new GpuDevice(VulkanBackend.Create()),
    };
#else
    if (backend is "webgpu" or "wgpu")
        return new GpuDevice(Luxel.Graphics.WebGPU.WebGpuBackend.Create());
    return new GpuDevice(VulkanBackend.Create(new VulkanBackendOptions
    {
        Presentation = VulkanPresentationMode.Window,
        PresentationSource = CreateVulkanPresentationSource(window.RequireBackendWindow<SilkWindow>()),
    }));
#endif
}

static GpuSurface CreateSurface(GpuDevice device, Window window)
{
    uint width = (uint)Math.Max(1, window.Width);
    uint height = (uint)Math.Max(1, window.Height);
#if LUXEL_WINDOWS
    Win32Window native = window.RequireBackendWindow<Win32Window>();
    return device.Backend switch
    {
        Luxel.Graphics.DirectX12.D3D12Backend d3d12 => d3d12.CreateSurface(native.Handle, width, height),
        Luxel.Graphics.WebGPU.WebGpuBackend webGpu => webGpu.CreateWin32Surface(native.HInstance, native.Handle, width, height),
        VulkanBackend vulkan => vulkan.CreateWin32Surface(native.Handle, width, height),
        _ => throw new PlatformNotSupportedException($"Unsupported backend: {device.Backend.GetType().FullName}"),
    };
#else
    SilkWindow native = window.RequireBackendWindow<SilkWindow>();
    return device.Backend switch
    {
        Luxel.Graphics.WebGPU.WebGpuBackend webGpu => webGpu.CreateXlibSurface(native.X11Display, native.X11Window, width, height),
        VulkanBackend vulkan => vulkan.CreateSurface(width, height),
        _ => throw new PlatformNotSupportedException($"Unsupported backend: {device.Backend.GetType().FullName}"),
    };
#endif
}

#if !LUXEL_WINDOWS
static unsafe VulkanPresentationSource CreateVulkanPresentationSource(SilkWindow window)
{
    IVkSurface vkSurface = window.NativeWindow.VkSurface
        ?? throw new PlatformNotSupportedException("Silk.NET did not expose Vulkan surface integration.");
    byte** pointers = vkSurface.GetRequiredExtensions(out uint count);
    if (pointers is null || count == 0)
        throw new PlatformNotSupportedException("Silk.NET did not report Vulkan instance extensions.");
    var extensions = new string[count];
    for (uint i = 0; i < count; i++)
        extensions[i] = SilkMarshal.PtrToString((nint)pointers[i])
            ?? throw new PlatformNotSupportedException("Silk.NET returned an invalid Vulkan extension name.");
    return new VulkanPresentationSource(extensions, instance =>
        vkSurface.Create<byte>(new VkHandle(instance), null).Handle);
}
#endif
// docs:end device-and-surface-backend

static int? ParseFrameLimit(string[] args)
{
    for (int i = 0; i + 1 < args.Length; i++)
        if (args[i] == "--frames" && int.TryParse(args[i + 1], out int frames))
            return Math.Max(1, frames);
    return null;
}

static (int Width, int Height) ParseSize(string[] args)
{
    for (int i = 0; i + 1 < args.Length; i++)
    {
        if (args[i] != "--size") continue;
        string[] parts = args[i + 1].Split('x', 'X');
        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height)
            && width > 0 && height > 0)
            return (width, height);
        throw new ArgumentException("--size must be a positive WIDTHxHEIGHT value, for example 801x603.");
    }
    return (800, 600);
}

static TutorialStage ParseStage(string[] args)
{
    for (int i = 0; i + 1 < args.Length; i++)
    {
        if (args[i] != "--stage") continue;
        return args[i + 1].ToLowerInvariant() switch
        {
            "triangle" => TutorialStage.Triangle,
            "texture" or "quad" => TutorialStage.Texture,
            "transform" or "cube" => TutorialStage.Transform,
            "lighting" or "light" => TutorialStage.Lighting,
            "graph" or "rendergraph" => TutorialStage.Graph,
            "post" or "postprocess" => TutorialStage.PostProcess,
            _ => throw new ArgumentException("--stage must be triangle, texture, transform, lighting, graph, or post."),
        };
    }
    return TutorialStage.Triangle;
}
