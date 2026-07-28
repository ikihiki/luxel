#:project ../src/Luxel.Graphics/Luxel.Graphics.csproj
#:project ../src/Luxel.Graphics.Vulkan/Luxel.Graphics.Vulkan.csproj
#:package SixLabors.ImageSharp@3.1.12
#:property TargetFramework=net10.0

using System.Security.Cryptography;
using Luxel.Graphics;
using Luxel.Graphics.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

const int width = 800;
const int height = 600;
string outputPath = ParseOutput(args);

try
{
    using GpuDevice device = new(VulkanBackend.Create(enableValidation: false));
    uint stridePixels = (uint)width;
    using GpuTexture target = device.CreateRenderTarget((uint)width, (uint)height, GpuFormat.Rgba8Unorm);
    using GpuBuffer readback = device.Malloc(checked((ulong)stridePixels * (uint)height * 4), GpuMemoryKind.HostMapped);

    using (GpuCommandBuffer command = device.MainQueue.StartCommandRecording())
    {
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, readback, stridePixels);
        command.Finish();
        device.MainQueue.SubmitAndWait(command);
    }

    byte[] rgba = CopyTightlyPacked(readback, stridePixels, width, height);
    using (Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rgba, width, height))
        image.SaveAsPng(outputPath);
    string sha256 = Convert.ToHexStringLower(SHA256.HashData(rgba));
    Console.WriteLine($"clear-color: offline, backend=vulkan, device={device.Name}, size={width}x{height}, output={outputPath}, sha256={sha256}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static byte[] CopyTightlyPacked(GpuBuffer readback, uint stridePixels, int width, int height)
{
    ReadOnlySpan<byte> mapped = readback.Span<byte>();
    int sourceStride = checked((int)stridePixels * 4);
    int destinationStride = checked(width * 4);
    byte[] pixels = new byte[checked(destinationStride * height)];
    for (int y = 0; y < height; y++)
        mapped.Slice(y * sourceStride, destinationStride).CopyTo(pixels.AsSpan(y * destinationStride, destinationStride));
    return pixels;
}

static string ParseOutput(string[] arguments)
{
    for (int index = 0; index < arguments.Length; index++)
    {
        if (arguments[index] == "--output" && index + 1 < arguments.Length) return arguments[index + 1];
        if (arguments[index].StartsWith("--output=", StringComparison.Ordinal)) return arguments[index]["--output=".Length..];
    }
    return "clear-color.png";
}
