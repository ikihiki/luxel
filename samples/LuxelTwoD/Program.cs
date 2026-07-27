using System.Security.Cryptography;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;

const uint width = 320, height = 200;
string? output = args.Length >= 2 && args[0] == "--output" ? args[1] : null;

// docs:begin two-d-scene
var scene = new Scene2D()
    .FillRoundedRect(Color2D.Rgba(32, 42, 64), 0, 0, width, height, 0)
    .FillRoundedRect(Color2D.Rgba(47, 111, 237), 28, 28, 150, 92, 18)
    .FillCircle(Color2D.Rgba(255, 196, 74), 228, 78, 38)
    .BeginStroke(Color2D.White, 6)
    .MoveTo(44, 158).CubicTo(105, 100, 190, 205, 278, 142).End();
// docs:end two-d-scene

// docs:begin two-d-render
using IRasterizer2D rasterizer = new SkiaRasterizer2D();
using IRasterScene2D encoded = rasterizer.CreateScene(scene);
var target = new SkiaRasterTarget2D(width, height);
encoded.Render(Camera2D.Pixels, target);
// docs:end two-d-render

byte[] pixels = target.ToArray();
string hash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
if (output is not null) WritePpm(output, pixels, width, height);
Console.WriteLine($"luxel-2d: {width}x{height}, backend={rasterizer.Name}, sha256={hash}");

static void WritePpm(string path, byte[] rgba, uint width, uint height)
{
    using FileStream stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
    for (int i = 0; i < rgba.Length; i += 4)
    {
        writer.Write(rgba[i]); writer.Write(rgba[i + 1]); writer.Write(rgba[i + 2]);
    }
}
