#:project ../src/Luxel.Graphics/Luxel.Graphics.csproj
#:project ../src/Luxel.Graphics.Vulkan/Luxel.Graphics.Vulkan.csproj
#:project ../src/Luxel.Graphics.DirectX12/Luxel.Graphics.DirectX12.csproj
#:project ../src/Luxel.Platform/Luxel.Platform.csproj
#:project ../src/Luxel.Platform.Silk/Luxel.Platform.Silk.csproj
#:project ../src/Luxel.Platform.Windows/Luxel.Platform.Windows.csproj
#:property TargetFramework=net10.0

using Luxel.Graphics;
using Luxel.Graphics.Vulkan;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Silk;
using Luxel.Platform.Windows;

string backend = args.FirstOrDefault(argument => argument is "vk" or "vulkan" or "dx" or "d3d12")?.ToLowerInvariant() ?? "vk";
int? frameLimit = ParseFrameLimit(args);
(int initialWidth, int initialHeight) = ParseSize(args);

if (OperatingSystem.IsWindows())
{
    int exitCode = 1;
    var thread = new Thread(() => exitCode = Run(backend, frameLimit, initialWidth, initialHeight))
    {
        Name = "LuxelClearColor-Main",
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    return exitCode;
}

return Run(backend, frameLimit, initialWidth, initialHeight);

static int Run(string backend, int? frameLimit, int initialWidth, int initialHeight)
{
    try
    {
        using WindowSystem windows = CreateWindowSystem();
        Window window = windows.CreateWindow(new WindowDesc("Luxel — Clear Color", initialWidth, initialHeight));
        using GpuDevice device = CreateDevice(backend, window);
        using GpuSurface surface = device.CreateSurface(
            window.Handle, (uint)Math.Max(1, window.Width), (uint)Math.Max(1, window.Height));
        using var frame = new ClearColorFrame(device);

        int width = Math.Max(0, window.Width);
        int height = Math.Max(0, window.Height);
        bool resizePending = true;
        window.Resized += (newWidth, newHeight) =>
        {
            width = Math.Max(0, newWidth);
            height = Math.Max(0, newHeight);
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
                frame.Resize(width, height);
                resizePending = false;
            }

            if (width == 0 || height == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            frame.Render();
            surface.Present(frame.Framebuffer, frame.StridePixels, (uint)width, (uint)height);
            renderedFrames++;

            if (frameLimit is int limit && renderedFrames >= limit)
            {
                window.Close();
                windows.Pump();
                break;
            }
        }

        device.MainQueue.WaitIdle();
        Console.WriteLine($"clear-color: {renderedFrames} frame(s), backend={backend}, device={device.Name}, size={width}x{height}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

static WindowSystem CreateWindowSystem()
{
    if (OperatingSystem.IsWindows())
        return new WindowSystem(Win32WindowBackend.Create());
    return new WindowSystem(SilkWindowBackend.Create());
}

static GpuDevice CreateDevice(string backend, Window window)
{
    if (OperatingSystem.IsWindows())
    {
        return backend switch
        {
            "dx" or "d3d12" => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
            _ => new GpuDevice(VulkanBackend.Create()),
        };
    }

    if (backend is "dx" or "d3d12")
        throw new PlatformNotSupportedException("DirectX 12 is available only on Windows. Use 'vk' on Linux.");
    IVulkanWindowSurface provider = window.GetFeature<IVulkanWindowSurface>()
        ?? throw new PlatformNotSupportedException("The Linux/X11 window did not provide a Vulkan surface.");
    return new GpuDevice(VulkanBackend.Create(new VulkanBackendOptions
    {
        Presentation = VulkanPresentationMode.Window,
        WindowSurface = provider,
    }));
}

static int? ParseFrameLimit(string[] arguments)
{
    for (int index = 0; index < arguments.Length; index++)
    {
        if (arguments[index] == "--frames" && index + 1 < arguments.Length
            && int.TryParse(arguments[index + 1], out int frames))
            return Math.Max(1, frames);
        if (arguments[index].StartsWith("--frames=", StringComparison.Ordinal)
            && int.TryParse(arguments[index]["--frames=".Length..], out frames))
            return Math.Max(1, frames);
    }
    return null;
}

static (int Width, int Height) ParseSize(string[] arguments)
{
    for (int index = 0; index < arguments.Length; index++)
    {
        string? value = null;
        if (arguments[index] == "--size" && index + 1 < arguments.Length) value = arguments[index + 1];
        else if (arguments[index].StartsWith("--size=", StringComparison.Ordinal)) value = arguments[index]["--size=".Length..];
        if (value is null) continue;

        string[] parts = value.Split('x', 'X');
        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height)
            && width > 0 && height > 0)
            return (width, height);
        throw new ArgumentException("--size must be a positive WIDTHxHEIGHT value, for example 801x603.");
    }
    return (800, 600);
}

sealed class ClearColorFrame(GpuDevice device) : IDisposable
{
    private GpuTexture? _target;
    private GpuBuffer? _framebuffer;

    public GpuBuffer Framebuffer
        => _framebuffer ?? throw new InvalidOperationException("Resize must be called with a positive size before rendering.");
    public uint StridePixels { get; private set; }

    public void Resize(int width, int height)
    {
        _framebuffer?.Dispose();
        _framebuffer = null;
        _target?.Dispose();
        _target = null;
        StridePixels = 0;

        if (width <= 0 || height <= 0) return;
        StridePixels = (uint)Align(width, 64); // D3D12 RGBA8 readback rows must be aligned to 256 bytes.
        _target = device.CreateRenderTarget((uint)width, (uint)height, GpuFormat.Rgba8Unorm);
        _framebuffer = device.Malloc(checked((ulong)StridePixels * (uint)height * 4), GpuMemoryKind.HostMapped);
    }

    public void Render()
    {
        if (_target is null || _framebuffer is null) return;
        using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
        command.BeginRendering(_target, null, 0.055f, 0.07f, 0.11f, 1)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(_target, _framebuffer, StridePixels);
        command.Finish();
        device.MainQueue.SubmitAndWait(command);
    }

    public void Dispose()
    {
        _framebuffer?.Dispose();
        _target?.Dispose();
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
