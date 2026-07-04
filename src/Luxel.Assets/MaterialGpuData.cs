using System.Numerics;
using System.Runtime.InteropServices;

namespace Luxel.Assets;

/// <summary>
/// GPU の material buffer 1 要素 (32B)。<c>scene_pbr_tex.slang</c> の Material struct と一致。
/// <c>RenderBuffer&lt;T&gt;</c> に SoA で並べ、shader 側は instance の
/// materialIndex から lookup する (RGRE-M2b)。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MaterialGpuData
{
    /// <summary>ベースカラー因子 (RGBA)。</summary>
    public Vector4 BaseColor;         // 16B
    /// <summary>bindless texture index (Flags の bit0 が立っているときのみ有効)。</summary>
    public uint    BaseColorTexIndex; //  4B (bindless、Flags の bit0 が立っているときのみ有効)
    /// <summary>bindless sampler index。</summary>
    public uint    SamplerIndex;      //  4B
    /// <summary>フラグビット群 (bit0 = テクスチャあり)。</summary>
    public uint    Flags;             //  4B
    /// <summary>32B 境界合わせ用パディング。</summary>
    public uint    _pad;              //  4B

    /// <summary>1 要素のバイト数 (32B)。shader 側の lookup stride。</summary>
    public const int Stride = 32;
    /// <summary><see cref="Flags"/> bit0: BaseColorTexIndex が有効。</summary>
    public const uint FlagHasTexture = 1u;
}
