namespace Luxel.Graphics;

/// <summary>
/// グラフィックスパイプラインのラスタ状態。ブログの GpuRasterDesc に相当。
/// </summary>
public struct GpuRasterDesc
{
    /// <summary>カラーターゲット形式。</summary>
    public GpuFormat ColorFormat;

    /// <summary>プリミティブトポロジ。</summary>
    public GpuPrimitiveTopology Topology;

    /// <summary>深度テストを行うか。</summary>
    public bool DepthTest;

    /// <summary>深度書き込みを行うか。</summary>
    public bool DepthWrite;

    /// <summary>深度ターゲット形式 (DepthTest 時に使用)。</summary>
    public GpuFormat DepthFormat;

    /// <summary>ブレンドモード。</summary>
    public GpuBlendMode Blend;

    /// <summary>背面/前面カリング。</summary>
    public GpuCullMode CullMode;

    /// <summary>表面として扱う頂点の回り順。</summary>
    public GpuFrontFace FrontFace;

    public static GpuRasterDesc Default(GpuFormat colorFormat) => new()
    {
        ColorFormat = colorFormat,
        Topology = GpuPrimitiveTopology.TriangleList,
        DepthTest = false,
        DepthWrite = false,
        DepthFormat = GpuFormat.D32Float,
        Blend = GpuBlendMode.None,
        CullMode = GpuCullMode.None,
        FrontFace = GpuFrontFace.CounterClockwise,
    };
}

/// <summary>ラスタライザで除外する面。</summary>
public enum GpuCullMode
{
    None,
    Front,
    Back,
}

/// <summary>表面と判定する頂点の回り順。</summary>
public enum GpuFrontFace
{
    CounterClockwise,
    Clockwise,
}

public enum GpuPrimitiveTopology
{
    TriangleList,
    TriangleStrip,
}

/// <summary>ブレンドモード。</summary>
public enum GpuBlendMode
{
    /// <summary>ブレンド無効 (上書き)。</summary>
    None,

    /// <summary>標準アルファブレンド (src.a, 1-src.a)。</summary>
    AlphaBlend,
}
