using Luxel;
using Luxel.Abstraction;
using Luxel.Graphics.Vulkan;
#if LUXEL_WINDOWS
using Luxel.Platform;
#else
using Luxel.Platform.Silk;
#endif

string backend = args.FirstOrDefault(a => a is "vk" or "vulkan" or "dx" or "d3d12")?.ToLowerInvariant() ?? "vk";
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
        NativeWindow window = windows.CreateWindow(new WindowDesc($"Luxel — 3D Tutorial ({stage})", initialWidth, initialHeight));
        using GpuDevice device = CreateDevice(backend, window);
        using GpuSurface surface = device.CreateSurface(window.Handle, (uint)Math.Max(1, window.Width), (uint)Math.Max(1, window.Height));
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

static GpuDevice CreateDevice(string backend, NativeWindow window)
{
#if LUXEL_WINDOWS
    return backend switch
    {
        "dx" or "d3d12" => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
        _ => new GpuDevice(VulkanBackend.Create()),
    };
#else
    IVulkanWindowSurface provider = window.GetFeature<IVulkanWindowSurface>()
        ?? throw new PlatformNotSupportedException("Linux/X11 window did not provide a Vulkan surface.");
    return new GpuDevice(VulkanBackend.Create(new VulkanBackendOptions
    {
        Presentation = VulkanPresentationMode.Window,
        WindowSurface = provider,
    }));
#endif
}

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
