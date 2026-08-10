using System.Reflection;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>Resource GPU stories が native/browser 共通で使用する埋め込み shader blobs。</summary>
internal static class ResourceStoryShaders
{
    public static GpuShaderCode Load(string name) => new()
    {
        SpirV = ReadOptional($"{name}.spv"),
        DxilVertex = ReadOptional($"{name}.vs.dxil"),
        DxilPixel = ReadOptional($"{name}.ps.dxil"),
        Wgsl = ReadOptional($"{name}.wgsl"),
    };

    private static byte[]? ReadOptional(string file)
    {
        Assembly assembly = typeof(ResourceStoryShaders).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream($"Luxel.Gallery.Resources.Shaders.{file}");
        if (stream is null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
