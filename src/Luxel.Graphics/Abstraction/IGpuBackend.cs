namespace Luxel.Graphics.Abstraction;

/// <summary>
/// バックエンド (Vulkan / D3D12) が実装する低レベル抽象。
/// 公開 API (<see cref="GpuDevice"/> 等) はこのインターフェイスのみに依存し、
/// 各バックエンドの具象型 (Silk.NET / Vortice) はバックエンドプロジェクト内に隔離する。
/// </summary>
public interface IGpuBackend : IDisposable
{
    /// <summary>バックエンドとデバイスの人間可読な名前 (例: "Vulkan / NVIDIA RTX ...")。</summary>
    string Name { get; }

    /// <summary>このバックエンドの種別。シェーダの形式選択 (SPIR-V / DXIL) に使う。</summary>
    GpuBackendKind Kind { get; }

    /// <summary>主キュー (汎用 graphics+compute+copy)。</summary>
    IGpuBackendQueue MainQueue { get; }

    /// <summary><c>gpuMalloc</c>。指定種別のメモリからバッファをサブアロケートする。</summary>
    IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind);

    /// <summary>compute パイプラインを生成する。</summary>
    IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint);

    /// <summary>graphics パイプラインを生成する (頂点 + ピクセル)。Vulkan は同一 blob を vs/ps に使える。</summary>
    IGpuBackendPipeline CreateGraphicsPipeline(
        ReadOnlySpan<byte> vsBlob, string vsEntry,
        ReadOnlySpan<byte> psBlob, string psEntry,
        GpuRasterDesc raster);

    /// <summary>レンダーターゲット (カラー) テクスチャを生成する。</summary>
    IGpuBackendTexture CreateRenderTarget(uint width, uint height, GpuFormat format);

    /// <summary>深度ターゲットを生成する。</summary>
    IGpuBackendTexture CreateDepthTarget(uint width, uint height, GpuFormat format);

    /// <summary>サンプリング可能なテクスチャを生成し、ピクセルデータをアップロードする。</summary>
    IGpuBackendTexture CreateSampledTexture(uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data);

    /// <summary>サンプラを生成する。</summary>
    IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp);

    /// <summary>ネイティブウィンドウへのスワップチェーン提示面を生成する。</summary>
    IGpuBackendSurface CreateSurface(in NativeSurfaceDescriptor descriptor, uint width, uint height);
}

/// <summary>スワップチェーン提示面。RGBA8 バッファをバックバッファへコピーして present する。</summary>
public interface IGpuBackendSurface : IDisposable
{
    /// <summary>RGBA8 バッファの (0,0)-(width,height) をバックバッファへコピーして present する。
    /// <paramref name="srcStridePixels"/> はソースの 1 行のピクセル数 (行パディング込み)。</summary>
    void Present(IGpuBackendBuffer source, uint srcStridePixels, uint width, uint height);

    /// <summary>サイズ変更時にスワップチェーンを再生成する。</summary>
    void Resize(uint width, uint height);
}

/// <summary>バックエンドのテクスチャハンドル。</summary>
public interface IGpuBackendTexture : IDisposable
{
    uint Width { get; }
    uint Height { get; }
    GpuFormat Format { get; }

    /// <summary>サンプリング用 bindless インデックス (sampled image ヒープ)。RT は未使用。</summary>
    uint BindlessIndex { get; }
}

/// <summary>バックエンドのサンプラハンドル。</summary>
public interface IGpuBackendSampler : IDisposable
{
    uint BindlessIndex { get; }
}

/// <summary>バックエンドのバッファハンドル。ブログの「64bit ポインタ」と CPU マップを表す。</summary>
public interface IGpuBackendBuffer : IDisposable
{
    /// <summary>バイト数。</summary>
    ulong Size { get; }

    /// <summary>シェーダから参照できる 64bit GPU アドレス (BDA / GPUVA)。</summary>
    ulong DeviceAddress { get; }

    /// <summary>
    /// bindless ヒープ内のインデックス。D3D12 では <c>ResourceDescriptorHeap[index]</c> 用。
    /// Vulkan の bindless 対応は M6 で導入予定 (現状は未使用)。
    /// </summary>
    uint BindlessIndex { get; }

    /// <summary>CPU からマップされた先頭ポインタ。マップ不可なら null。</summary>
    unsafe void* MappedPointer { get; }
}

/// <summary>バックエンドのパイプラインハンドル (compute / graphics / mesh 共通)。</summary>
public interface IGpuBackendPipeline : IDisposable
{
    bool IsCompute { get; }
}

/// <summary>バックエンドのキュー。一過性コマンドバッファの記録開始と投入を行う。</summary>
public interface IGpuBackendQueue
{
    /// <summary>新しい使い捨てコマンドバッファの記録を開始する (<c>gpuStartCommandRecording</c>)。</summary>
    IGpuBackendCommandBuffer StartCommandRecording();

    /// <summary>記録済みコマンドバッファを投入する。</summary>
    void Submit(IGpuBackendCommandBuffer commandBuffer);

    /// <summary>キューが空になるまで待つ。</summary>
    void WaitIdle();
}

/// <summary>バックエンドのコマンドバッファ。記録 API を提供する。</summary>
public interface IGpuBackendCommandBuffer : IDisposable
{
    /// <summary>compute パイプラインをバインドする。</summary>
    void SetComputePipeline(IGpuBackendPipeline pipeline);

    /// <summary>
    /// ルート引数 (小さな定数バイト列) を設定する。Vulkan は push 定数、D3D12 は root 32bit 定数へ書く。
    /// </summary>
    void SetRootConstants(ReadOnlySpan<byte> data);

    /// <summary>compute ディスパッチ。</summary>
    void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ);

    /// <summary>graphics パイプラインをバインドする。</summary>
    void SetGraphicsPipeline(IGpuBackendPipeline pipeline);

    /// <summary>カラー(+任意で深度)ターゲットへの dynamic rendering を開始し、クリアする。</summary>
    void BeginRendering(IGpuBackendTexture color, IGpuBackendTexture? depth,
        float r, float g, float b, float a, float clearDepth);

    /// <summary>dynamic rendering を終了する。</summary>
    void EndRendering();

    /// <summary>描画 (頂点プル)。</summary>
    void Draw(uint vertexCount, uint instanceCount);

    /// <summary>テクスチャ全体をバッファへコピーする (読み戻し用)。rowLengthPixels=0 は密な行。</summary>
    void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination, uint rowLengthPixels);

    /// <summary>バッファ間コピー (先頭から bytes 分)。WC メモリの読み戻しを避けて
    /// HostCached (READBACK) バッファへ落とす用途など。</summary>
    void CopyBufferToBuffer(IGpuBackendBuffer source, IGpuBackendBuffer destination, ulong bytes);

    /// <summary>ステージベースのバリアを発行する。</summary>
    void Barrier(GpuStage source, GpuStage destination, GpuHazard hazard);

    /// <summary>記録を終了する (投入可能な状態にする)。</summary>
    void Finish();
}
