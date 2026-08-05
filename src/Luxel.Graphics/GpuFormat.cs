namespace Luxel.Graphics;

/// <summary>テクスチャ/レンダーターゲットのピクセル形式。</summary>
public enum GpuFormat
{
    Rgba8Unorm,
    Bgra8Unorm,
    R32Float,
    D32Float,
    Rgba8UnormSrgb,
    Bgra8UnormSrgb,
    R8Unorm,
    Rg8Unorm,
}

internal static class GpuFormatInfo
{
    internal static uint BytesPerPixel(GpuFormat format) => format switch
    {
        GpuFormat.R8Unorm => 1,
        GpuFormat.Rg8Unorm => 2,
        GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm
            or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb
            or GpuFormat.R32Float or GpuFormat.D32Float => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    internal static bool IsSampledUpload(GpuFormat format) => format != GpuFormat.D32Float && Enum.IsDefined(format);

    internal static bool IsPortableSampled(GpuFormat format) => format is
        GpuFormat.R8Unorm or GpuFormat.Rg8Unorm
        or GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm
        or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb;

    internal static bool IsColor(GpuFormat format) => format != GpuFormat.D32Float && Enum.IsDefined(format);
}
