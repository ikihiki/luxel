using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

/// <summary>
/// 一過性 (使い捨て) コマンドバッファ。記録後 <see cref="Finish"/> し、
/// <see cref="GpuQueue.Submit"/> で投入する。
/// </summary>
public sealed class GpuCommandBuffer : IDisposable
{
    /// <summary>Maximum byte size shared by the Vulkan push-constant range and D3D12 root constants.</summary>
    public const int MaxRootArgumentBytes = 192;

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
        return SetRootArguments(bytes);
    }

    /// <summary>ルート引数を生のバイト列で設定する。</summary>
    public GpuCommandBuffer SetRootArguments(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxRootArgumentBytes)
            throw new ArgumentOutOfRangeException(nameof(data), data.Length,
                $"Root arguments are limited to {MaxRootArgumentBytes} bytes.");
        if ((data.Length & 3) != 0)
            throw new ArgumentException("Root arguments must have a 4-byte-compatible size.", nameof(data));
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

    public GpuCommandBuffer SetRasterizerState(GpuRasterizerState state) { _cmd.SetRasterizerState(state); return this; }
    public GpuCommandBuffer SetDepthStencilState(GpuDepthStencilState state) { _cmd.SetDepthStencilState(state.Normalize()); return this; }
    public GpuCommandBuffer SetStencilReference(uint reference)
    {
        GpuDepthStencilState.ValidateByte(reference, nameof(reference));
        _cmd.SetStencilReference(reference);
        return this;
    }
    public GpuCommandBuffer SetBlendState(GpuBlendState state) { _cmd.SetBlendState(state); return this; }
    public GpuCommandBuffer SetViewport(GpuViewport viewport) { _cmd.SetViewport(viewport); return this; }
    public GpuCommandBuffer SetScissor(GpuScissorRect scissor) { _cmd.SetScissor(scissor); return this; }

    /// <summary>カラー(+任意で深度-stencil)ターゲットへの描画を開始し、クリアする。</summary>
    public GpuCommandBuffer BeginRendering(GpuTexture color, GpuTexture? depth = null,
        float r = 0, float g = 0, float b = 0, float a = 1, float clearDepth = 1f, uint clearStencil = 0)
    {
        if (!float.IsFinite(clearDepth) || clearDepth < 0 || clearDepth > 1) throw new ArgumentOutOfRangeException(nameof(clearDepth));
        GpuDepthStencilState.ValidateByte(clearStencil, nameof(clearStencil));
        _cmd.BeginRendering(color.Backend, depth?.Backend, r, g, b, a, clearDepth, clearStencil);
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

    /// <summary>テクスチャをバッファへコピーする (読み戻し用)。destinationRowLengthPixels=0 は密な行。</summary>
    public GpuCommandBuffer CopyTextureToBuffer(GpuTexture source, GpuBuffer destination,
        uint destinationRowLengthPixels = 0)
    {
        _cmd.CopyTextureToBuffer(source.Backend, destination.Backend, destinationRowLengthPixels);
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
