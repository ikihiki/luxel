using System.Numerics;
using System.Runtime.InteropServices;

namespace Luxel.Assets;

/// <summary>
/// GPU の material buffer 1 要素 (32B)。<c>scene_pbr_tex.slang</c> の Material struct と一致。
/// <see cref="Luxel.Resources.RenderBuffer{T}"/> に SoA で並べ、shader 側は instance の
/// materialIndex から lookup する (RGRE-M2b)。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MaterialGpuData
{
    public Vector4 BaseColor;         // 16B
    public uint    BaseColorTexIndex; //  4B (bindless、Flags の bit0 が立っているときのみ有効)
    public uint    SamplerIndex;      //  4B
    public uint    Flags;             //  4B
    public uint    _pad;              //  4B

    public const int Stride = 32;
    public const uint FlagHasTexture = 1u;
}
