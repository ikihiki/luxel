using System.Runtime.CompilerServices;

namespace Luxel.AssetsGpu;

/// <summary>
/// CPU 側 staging (T[]) + GPU 側 GpuBuffer (HostMapped) のペア。
/// <see cref="Data"/> を書き換えて <see cref="MarkDirty"/> すると次の <see cref="ResourceSystem.Pump"/> で GPU に反映される。
/// Resources 経由で publish されると、cross-pass で同じ buffer を共有できる (RGRE-M2a)。
///
/// 値そのもの (RenderBuffer 参照) は不変 ─ 消費側は <see cref="Buffer"/> を <c>rg.ImportBuffer</c> で毎フレーム取り込む。
/// 内部の staging → GPU 反映と Reloaded 伝播は Pump が1回で行う。
/// </summary>
public sealed class RenderBuffer<T> : IDisposable where T : unmanaged
{
    private readonly T[] _staging;
    private GpuBuffer _buffer;
    private volatile bool _dirty;
    private int _version;
    private bool _disposed;

    public RenderBuffer(GpuDevice device, int count, string name = "renderbuffer")
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        _staging = new T[count];
        ulong size = (ulong)count * (ulong)Unsafe.SizeOf<T>();
        _buffer = device.Malloc(size, GpuMemoryKind.HostMapped);
        // 初期化 (ゼロ状態を GPU に反映)
        _dirty = true;
    }

    /// <summary>編集用 CPU staging。書き換えたら <see cref="MarkDirty"/> を呼ぶ。</summary>
    public Span<T> Data => _staging.AsSpan();
    public int Count => _staging.Length;
    /// <summary>Flush 済み内容を持つ GPU バッファ (bindless で参照可能)。</summary>
    public GpuBuffer Buffer => _buffer;
    /// <summary>Flush 毎に +1。消費側の変更検知に。</summary>
    public int Version => _version;

    public ref T this[int index] => ref _staging[index];

    public void MarkDirty() => _dirty = true;

    /// <summary>Pump から呼ばれる ─ dirty なら GPU に書き込み、true を返す (呼び出し側は Version++/Reloaded を発火)。</summary>
    internal bool Flush()
    {
        if (!_dirty) return false;
        _dirty = false;
        _staging.AsSpan().CopyTo(_buffer.Span<T>(_staging.Length));
        _version++;
        return true;
    }

    /// <summary>Pump を待たず GPU に即時反映する (Extractor が Extract 直後の draw で参照するケース向け)。
    /// Reloaded event は発火しない ─ 消費側は同ターン内で参照するのみ想定。</summary>
    public void FlushImmediate()
    {
        if (!_dirty) return;
        _dirty = false;
        _staging.AsSpan().CopyTo(_buffer.Span<T>(_staging.Length));
        _version++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buffer.Dispose();
    }
}
