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
const string outputPath = "clear-color.png";

using GpuDevice device = new(VulkanBackend.Create(enableValidation: false));
using GpuTexture target = device.CreateRenderTarget((uint)width, (uint)height, GpuFormat.Rgba8Unorm);
using GpuBuffer readback = device.Malloc(checked((ulong)width * height * 4), GpuMemoryKind.HostMapped);

using (GpuCommandBuffer command = device.MainQueue.StartCommandRecording())
{
    command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
        .EndRendering()
        .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
        .CopyTextureToBuffer(target, readback, (uint)width);
    command.Finish();
    device.MainQueue.SubmitAndWait(command);
}

ReadOnlySpan<byte> rgba = readback.Span<byte>(width * height * 4);
using (Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rgba, width, height))
    image.SaveAsPng(outputPath);
string sha256 = Convert.ToHexStringLower(SHA256.HashData(rgba));
Console.WriteLine($"clear-color: offline, backend=vulkan, device={device.Name}, size={width}x{height}, output={outputPath}, sha256={sha256}");
