namespace Luxel.Graphics;

/// <summary>
/// ステージベースのバリア用のパイプラインステージ。
/// Vulkan の <c>PipelineStageFlags2</c> / D3D12 の <c>D3D12_BARRIER_SYNC</c> にバックエンド側で remap する。
/// ブログの <c>STAGE_COMPUTE</c> / <c>STAGE_PIXEL_SHADER</c> / <c>STAGE_RASTER_COLOR_OUT</c> 等に相当。
/// </summary>
[Flags]
public enum GpuStage : uint
{
    None = 0,

    /// <summary>間接描画/ディスパッチ引数の読み出し。</summary>
    DrawIndirect = 1 << 0,

    /// <summary>頂点シェーダ (vertex pulling の load を含む)。</summary>
    VertexShader = 1 << 1,

    /// <summary>ピクセル/フラグメントシェーダ。</summary>
    PixelShader = 1 << 2,

    /// <summary>コンピュートシェーダ。</summary>
    ComputeShader = 1 << 3,

    /// <summary>カラー出力 (ラスタライザのカラーアタッチメント書き込み)。</summary>
    ColorOutput = 1 << 4,

    /// <summary>深度/ステンシルテストと書き込み。</summary>
    DepthStencil = 1 << 5,

    /// <summary>コピー/転送。</summary>
    Copy = 1 << 6,

    /// <summary>すべてのグラフィックステージ。</summary>
    AllGraphics = 1 << 7,

    /// <summary>すべてのコマンド (最も粗いバリア)。</summary>
    All = 1 << 8,
}
