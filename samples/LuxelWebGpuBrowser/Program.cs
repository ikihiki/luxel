using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Luxel.Graphics;
using Luxel.Graphics.Abstraction;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;

namespace LuxelWebGpuBrowser;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private const uint ExpectedCompute = 0xc0ffee42;
    private const uint TargetWidth = CanonicalTriangleRecipe.Width;
    private const uint TargetHeight = CanonicalTriangleRecipe.Height;

    public static async Task Main()
    {
        try
        {
            using WebWindowBackend web = await WebWindowBackend.CreateAsync(new WebWindowBackendOptions
            {
                // JSHost.ImportAsync resolves relative URLs from the _framework runtime module.
                ModuleUrl = "../luxel-platform-web.js",
                Canvases = [new WebCanvasOptions("#luxel-canvas") { SurfaceToken = "#luxel-canvas" }],
            });
            using var windows = new WindowSystem(web);
            Window window = windows.CreateWindow(new WindowDesc("Luxel browser WebGPU", (int)TargetWidth, (int)TargetHeight));
            int pointerEvents = 0, keyEvents = 0, resizeEvents = 0;
            window.PointerMoved += _ => pointerEvents++;
            window.PointerDown += _ => pointerEvents++;
            window.PointerUp += _ => pointerEvents++;
            window.KeyDown += _ => keyEvents++;
            window.KeyUp += _ => keyEvents++;
            window.Resized += (_, _) => resizeEvents++;

            using BrowserWebGpuBackend gpu = await BrowserWebGpuBackend.CreateAsync();
            using BrowserWebGpuSurface surface = gpu.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
            using IGpuBackendBuffer compute = gpu.CreateBuffer(256, GpuMemoryKind.HostCached);
            using IGpuBackendPipeline computePipeline = gpu.CreateComputePipeline(Shader("compute.wgsl"), "main");
            using (IGpuBackendCommandBuffer command = gpu.MainQueue.StartCommandRecording())
            {
                command.SetComputePipeline(computePipeline);
                command.SetRootConstants(Bytes(new ComputeRoot(compute.BindlessIndex, ExpectedCompute)));
                command.Dispatch(1, 1, 1);
                command.Finish();
                await gpu.AsyncQueue.SubmitAsync(command);
            }
            uint computeValue = Read<uint>(compute);
            if (computeValue != ExpectedCompute) throw new InvalidOperationException($"Compute result was 0x{computeValue:x8}.");

            CanonicalTriangleRecipe.Vertex[] triangleVertices = CanonicalTriangleRecipe.CreateVertices();
            using IGpuBackendBuffer vertices = gpu.CreateBuffer((ulong)(triangleVertices.Length * CanonicalTriangleRecipe.VertexSize), GpuMemoryKind.HostMapped);
            Write(vertices, triangleVertices);
            using IGpuBackendTexture target = gpu.CreateRenderTarget(TargetWidth, TargetHeight, GpuFormat.Rgba8Unorm);
            using IGpuBackendBuffer pixels = gpu.CreateBuffer(TargetWidth * TargetHeight * 4, GpuMemoryKind.HostCached);
            using IGpuBackendPipeline graphics = gpu.CreateGraphicsPipeline(Shader("tutorial_triangle.wgsl"), "vsMain", Shader("tutorial_triangle.wgsl"), "psMain", GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
            using (IGpuBackendCommandBuffer command = gpu.MainQueue.StartCommandRecording())
            {
                command.SetGraphicsPipeline(graphics);
                command.SetRootConstants(Bytes(new CanonicalTriangleRecipe.DrawArgs { VertexBufferIndex = vertices.BindlessIndex }));
                command.BeginRendering(target, null, 0.04f, 0.07f, 0.12f, 1f, 1f);
                command.Draw(3, 1);
                command.EndRendering();
                command.CopyTextureToBuffer(target, pixels, TargetWidth);
                command.Finish();
                await gpu.AsyncQueue.SubmitAsync(command);
            }
            int center = checked((int)((TargetHeight / 2 * TargetWidth + TargetWidth / 2) * 4));
            byte red, green, blue, alpha;
            {
                Span<byte> data = Mapped(pixels);
                red = data[center]; green = data[center + 1]; blue = data[center + 2]; alpha = data[center + 3];
            }
            if (alpha < 240 || red + green + blue < 180)
                throw new InvalidOperationException($"Center pixel was rgba({red},{green},{blue},{alpha}).");
            surface.Present(pixels, TargetWidth, TargetWidth, TargetHeight);

            while (windows.Pump())
            {
                surface.Resize((uint)window.Width, (uint)window.Height);
                surface.Present(pixels, TargetWidth, TargetWidth, TargetHeight);
                SetStatus("pass", $"browser-webgpu: status=pass\nstory={CanonicalTriangleRecipe.Story}\nshader={CanonicalTriangleRecipe.Shader}\nvertexSize={CanonicalTriangleRecipe.VertexSize}; rootSize={CanonicalTriangleRecipe.DrawArgsSize}\ncanvas={CanonicalTriangleRecipe.Width}x{CanonicalTriangleRecipe.Height}\nrecipe={CanonicalTriangleRecipe.Recipe}\nhash={CanonicalTriangleRecipe.ShaderSha256}\ndevice={gpu.Name}\ncompute=0x{computeValue:x8}; center=rgba({red},{green},{blue},{alpha})\nframes=1+; resize={resizeEvents}; pointer={pointerEvents}; key={keyEvents}");
                await NextFrame();
            }
        }
        catch (Exception ex)
        {
            SetStatus("fail", $"browser-webgpu: status=fail, error={ex.Message}");
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    private static byte[] Shader(string name)
    {
        string suffix = ".Shaders." + name;
        string resource = Assembly.GetExecutingAssembly().GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
    private static byte[] Bytes<T>(T value) where T : unmanaged { T[] one = [value]; return MemoryMarshal.AsBytes(one.AsSpan()).ToArray(); }
    private static unsafe Span<byte> Mapped(IGpuBackendBuffer buffer) => new(buffer.MappedPointer, checked((int)buffer.Size));
    private static T Read<T>(IGpuBackendBuffer buffer) where T : unmanaged => MemoryMarshal.Read<T>(Mapped(buffer));
    private static void Write<T>(IGpuBackendBuffer buffer, ReadOnlySpan<T> values) where T : unmanaged => MemoryMarshal.AsBytes(values).CopyTo(Mapped(buffer));
    private readonly record struct ComputeRoot(uint BufferIndex, uint Value, uint Pad0 = 0, uint Pad1 = 0);

    [JSImport("nextFrame", "luxel-browser-host")] private static partial Task<double> NextFrame();
    [JSImport("setStatus", "luxel-browser-host")] private static partial void SetStatus(string state, string summary);
}
