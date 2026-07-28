#:project ../src/Luxel.Graphics/Luxel.Graphics.csproj
#:project ../src/Luxel.Graphics.Vulkan/Luxel.Graphics.Vulkan.csproj
#:property TargetFramework=net10.0

using System.Security.Cryptography;
using System.Text;
using Luxel.Graphics;
using Luxel.Graphics.Vulkan;

(int width, int height) = ParseSize(args);
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
    WritePpm(outputPath, rgba, width, height);
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

static void WritePpm(string path, ReadOnlySpan<byte> rgba, int width, int height)
{
    string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    using FileStream stream = File.Create(path);
    stream.Write(Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
    byte[] rgbRow = new byte[checked(width * 3)];
    for (int y = 0; y < height; y++)
    {
        ReadOnlySpan<byte> source = rgba.Slice(checked(y * width * 4), width * 4);
        for (int x = 0; x < width; x++)
        {
            rgbRow[x * 3] = source[x * 4];
            rgbRow[x * 3 + 1] = source[x * 4 + 1];
            rgbRow[x * 3 + 2] = source[x * 4 + 2];
        }
        stream.Write(rgbRow);
    }
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

static string ParseOutput(string[] arguments)
{
    for (int index = 0; index < arguments.Length; index++)
    {
        if (arguments[index] == "--output" && index + 1 < arguments.Length) return arguments[index + 1];
        if (arguments[index].StartsWith("--output=", StringComparison.Ordinal)) return arguments[index]["--output=".Length..];
    }
    return "clear-color.ppm";
}
