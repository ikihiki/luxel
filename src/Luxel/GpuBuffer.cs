using System.Runtime.InteropServices;
using Luxel.Abstraction;

namespace Luxel;

/// <summary>
/// <c>gpuMalloc</c> が返す GPU メモリ確保。シェーダから参照する 64bit アドレス
/// (<see cref="DeviceAddress"/>) と、可能なら CPU マップ領域 (<see cref="Span{T}()"/>) を持つ。
/// </summary>
public sealed class GpuBuffer : IDisposable
{
    private readonly IGpuBackendBuffer _buffer;
    private bool _disposed;

    internal GpuBuffer(IGpuBackendBuffer buffer) => _buffer = buffer;

    /// <summary>バイト数。</summary>
    public ulong Size => _buffer.Size;

    /// <summary>シェーダのルート引数等に渡す 64bit GPU アドレス。</summary>
    public ulong DeviceAddress => _buffer.DeviceAddress;

    /// <summary>bindless ヒープ内インデックス (D3D12 の <c>ResourceDescriptorHeap</c> 用)。</summary>
    public uint BindlessIndex => _buffer.BindlessIndex;

    /// <summary>CPU マップされているか。</summary>
    public unsafe bool IsMapped => _buffer.MappedPointer != null;

    /// <summary>CPU マップ先頭ポインタ (マップ不可なら null)。</summary>
    public unsafe void* Pointer => _buffer.MappedPointer;

    /// <summary>マップ領域全体を <typeparamref name="T"/> の <see cref="System.Span{T}"/> として見る。</summary>
    public unsafe Span<T> Span<T>() where T : unmanaged
    {
        void* p = _buffer.MappedPointer;
        if (p == null)
            throw new InvalidOperationException("このバッファは CPU マップされていません (GpuMemoryKind を確認してください)。");
        int count = checked((int)(Size / (ulong)Marshal.SizeOf<T>()));
        return new Span<T>(p, count);
    }

    /// <summary>マップ領域を指定要素数の <see cref="System.Span{T}"/> として見る。</summary>
    public unsafe Span<T> Span<T>(int count) where T : unmanaged
    {
        void* p = _buffer.MappedPointer;
        if (p == null)
            throw new InvalidOperationException("このバッファは CPU マップされていません (GpuMemoryKind を確認してください)。");
        return new Span<T>(p, count);
    }

    /// <summary>抽象ハンドル (バックエンド/コマンド記録から利用)。</summary>
    internal IGpuBackendBuffer Backend => _buffer;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buffer.Dispose();
    }
}
