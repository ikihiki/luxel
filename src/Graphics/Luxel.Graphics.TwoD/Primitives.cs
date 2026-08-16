using System.Numerics;
using System.Runtime.InteropServices;

namespace Luxel.Graphics.TwoD;

/// <summary>塗り規則。</summary>
public enum FillRule : uint
{
    NonZero = 0,
    EvenOdd = 1,
}

/// <summary>RGBA8 色のヘルパ (R が下位バイト)。</summary>
public static class Color2D
{
    public static uint Rgba(byte r, byte g, byte b, byte a = 255)
        => (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    public static uint FromFloat(float r, float g, float b, float a = 1f)
        => Rgba((byte)(Math.Clamp(r, 0, 1) * 255), (byte)(Math.Clamp(g, 0, 1) * 255),
                (byte)(Math.Clamp(b, 0, 1) * 255), (byte)(Math.Clamp(a, 0, 1) * 255));

    public static readonly uint Black = Rgba(0, 0, 0);
    public static readonly uint White = Rgba(255, 255, 255);
    public static readonly uint Red = Rgba(220, 50, 50);
    public static readonly uint Green = Rgba(50, 180, 80);
    public static readonly uint Blue = Rgba(60, 110, 220);
}

// ---- GPU 構造体 (raster2d_fine.slang と一致, scalar layout) ----

[StructLayout(LayoutKind.Sequential)]
internal struct GpuSegment   // 32 バイト
{
    public float X0, Y0, X1, Y1;  // ワールド座標の線分
    public uint PathId, Pad0, Pad1, Pad2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPath       // 72 バイト
{
    public uint SegStart, SegCount, TransformSlot, StyleSlot;
    public uint FillRule, Kind, ClipSlot, SrcIndex;   // ClipSlot=0xFFFFFFFF で無し。Kind: 0=fill 1=stroke 2=image
    public float BMinX, BMinY, BMaxX, BMaxY;           // ローカル bbox (変換前)。image は UV 矩形を兼ねる
    public float StrokeHalfWidth;
    public uint SrcStride, SrcW, SrcH;                 // image 用: ソース bindless バッファの行ピッチ(px)/サンプリング寸法
    public uint SrcX, SrcY;                            // image 用: アトラス原点オフセット(px)。0 = バッファ先頭 (全画像)

    public const uint NoClip = 0xFFFFFFFF;
    public const int SizeBytes = 72;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuTransform  // 32 バイト (2x3 affine)
{
    public float A, B, C, D, E, F;  // screen = (A*x+C*y+E, B*x+D*y+F)
    public uint Pad0, Pad1;

    public static GpuTransform Identity => new() { A = 1, D = 1 };
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuStyle      // 16 バイト
{
    public uint ColorRgba;
    public float Opacity;
    public uint Pad0, Pad1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuClip       // 32 バイト (ワールド空間の角丸 AABB + 親クリップ)
{
    public float MinX, MinY, MaxX, MaxY;
    public float RadiusX, RadiusY;
    public uint Corners, ParentSlot;

    public const int SizeBytes = 32;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RasterArgs    // 88 バイト
{
    public float A, B, C, D, E, F;  // カメラ (ワールド→画面 affine 2x3)
    public uint Width, Height;
    public uint SegIndex, PathIndex, TransformIndex, StyleIndex, ClipIndex, OrderIndex;
    public uint OrderCount, FbIndex;
    public uint BgMode;             // 0 = 白背景 (RGB 出力, alpha=255), 1 = 透過 (premultiplied RGBA)
    // ---- タイルビニング (RA-M1): 16px タイル毎のパスリストで fine の per-pixel 全 order 走査を置換 ----
    public uint TilesX, TileCap;    // 横タイル数 / タイルリスト容量 (超過タイルは fine が全走査へフォールバック)
    public uint BoundsIndex, TileListIndex, TileCountIndex;   // 前計算 AABB / タイルリスト / タイル毎件数
}

/// <summary>ワールド→画面の 2D アフィン変換 (ズーム/パン)。</summary>
public struct Camera2D
{
    public float A, B, C, D, E, F;  // screen = (A*x+C*y+E, B*x+D*y+F)

    /// <summary>ワールド=画面ピクセルそのまま (恒等)。</summary>
    public static Camera2D Pixels => new() { A = 1, B = 0, C = 0, D = 1, E = 0, F = 0 };

    /// <summary>スケール + 中心指定。worldCenter が画面中心に来る。</summary>
    public static Camera2D Create(float scale, Vector2 worldCenter, uint screenW, uint screenH) => new()
    {
        A = scale,
        B = 0,
        C = 0,
        D = scale,
        E = screenW * 0.5f - worldCenter.X * scale,
        F = screenH * 0.5f - worldCenter.Y * scale,
    };
}
