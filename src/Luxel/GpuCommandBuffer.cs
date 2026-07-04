using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Luxel.Abstraction;

namespace Luxel;

/// <summary>
/// 一過性 (使い捨て) コマンドバッファ。記録後 <see cref="Finish"/> し、
/// <see cref="GpuQueue.Submit"/> で投入する。
/// </summary>
public sealed class GpuCommandBuffer : IDisposable
{
    private readonly IGpuBackendCommandBuffer _cmd;
    private bool _disposed;

    internal GpuCommandBuffer(IGpuBackendCommandBuffer cmd) => _cmd = cmd;

    internal IGpuBackendCommandBuffer Backend => _cmd;

    /// <summary>compute パイプラインをバインドする。</summary>
    public GpuCommandBuffer SetComputePipeline(GpuPipeline pipeline)
    {
        _cmd.SetComputePipeline(pipeline.Backend);
        return this;
    }

    /// <summary>
    /// ルート引数構造体を設定する (ブログのルート引数に相当)。小さな値型を inline で渡す。
    /// バッファ参照は <see cref="GpuBuffer.BindlessIndex"/> をフィールドに入れて表現する。
    /// </summary>
    public GpuCommandBuffer SetRootArguments<T>(in T value) where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        _cmd.SetRootConstants(bytes);
        return this;
    }

    /// <summary>ルート引数を生のバイト列で設定する。</summary>
    public GpuCommandBuffer SetRootArguments(ReadOnlySpan<byte> data)
    {
        _cmd.SetRootConstants(data);
        return this;
    }

    /// <summary>compute ディスパッチ。</summary>
    public GpuCommandBuffer Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        _cmd.Dispatch(groupCountX, groupCountY, groupCountZ);
        return this;
    }

    /// <summary>graphics パイプラインをバインドする。</summary>
    public GpuCommandBuffer SetGraphicsPipeline(GpuPipeline pipeline)
    {
        _cmd.SetGraphicsPipeline(pipeline.Backend);
        return this;
    }

    /// <summary>カラー(+任意で深度)ターゲットへの描画を開始し、クリアする。</summary>
    public GpuCommandBuffer BeginRendering(GpuTexture color, GpuTexture? depth = null,
        float r = 0, float g = 0, float b = 0, float a = 1, float clearDepth = 1f)
    {
        _cmd.BeginRendering(color.Backend, depth?.Backend, r, g, b, a, clearDepth);
        return this;
    }

    /// <summary>描画を終了する。</summary>
    public GpuCommandBuffer EndRendering()
    {
        _cmd.EndRendering();
        return this;
    }

    /// <summary>描画 (頂点プル)。</summary>
    public GpuCommandBuffer Draw(uint vertexCount, uint instanceCount = 1)
    {
        _cmd.Draw(vertexCount, instanceCount);
        return this;
    }

    /// <summary>テクスチャをバッファへコピーする (読み戻し用)。</summary>
    public GpuCommandBuffer CopyTextureToBuffer(GpuTexture source, GpuBuffer destination)
    {
        _cmd.CopyTextureToBuffer(source.Backend, destination.Backend);
        return this;
    }

    /// <summary>バッファ間コピー (先頭から bytes 分)。HostMapped (write-combined) の framebuffer を
    /// HostCached (READBACK) へ GPU コピーしてから CPU で読む読み戻しの定石用。</summary>
    public GpuCommandBuffer CopyBuffer(GpuBuffer source, GpuBuffer destination, ulong bytes)
    {
        _cmd.CopyBufferToBuffer(source.Backend, destination.Backend, bytes);
        return this;
    }

    /// <summary>ステージベースのバリア (<c>gpuBarrier</c>)。</summary>
    public GpuCommandBuffer Barrier(GpuStage source, GpuStage destination, GpuHazard hazard = GpuHazard.None)
    {
        _cmd.Barrier(source, destination, hazard);
        return this;
    }

    /// <summary>記録を終了する。</summary>
    public void Finish() => _cmd.Finish();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cmd.Dispose();
    }
}
