namespace Luxel.RenderGraph;

/// <summary>
/// シーン側 (2D/UI/3D ECS など) を毎フレーム GPU バッファへ抽出する抽象。
/// レンダーグラフはこの抽象を介してシーンを取り込むことで、UI/ECS/独自シーンを問わず同じ pass builder で
/// パイプラインを組めるようになる (scene-agnostic 設計)。
///
/// 強制ではない: 既存の <see cref="GpuBuffer"/> をそのまま <see cref="RenderGraph.ImportBuffer"/> しても良い。
/// IRenderExtractor は「シーン更新 → GPU バッファ書込」の操作を一段抽象化する規約であり、
/// RG-M4 (3D + ECS) では <c>Render3DExtractSystem</c> 等が実装する想定。
/// </summary>
public interface IRenderExtractor
{
    /// <summary>
    /// シーンを GPU バッファへ抽出する。レンダーグラフ構築の前段で呼び出されることを想定。
    /// 必要な <see cref="GpuBuffer"/> は extractor 自身が確保 / 保持し、
    /// それを <see cref="RenderGraph.ImportBuffer"/> でグラフへ取り込む流儀。
    /// </summary>
    void Extract(ExtractContext ctx);
}

/// <summary>extractor が参照するフレームレベル情報。</summary>
public readonly struct ExtractContext
{
    /// <summary>GPU デバイス (リソース確保用)。</summary>
    public GpuDevice Device { get; }

    /// <summary>フレーム連番 (0 から開始、ダブルバッファリング等の計算に使う)。</summary>
    public ulong FrameIndex { get; }

    public ExtractContext(GpuDevice device, ulong frameIndex)
    {
        Device = device;
        FrameIndex = frameIndex;
    }
}
