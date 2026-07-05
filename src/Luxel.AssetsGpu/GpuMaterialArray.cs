using System.Numerics;
using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// bindless shader が material を uint index で lookup できるように、<see cref="MaterialGpuData"/> の
/// 配列を GPU に保持する。<see cref="GpuMaterial"/> の追加/変更で自動 dirty。
/// </summary>
public sealed class GpuMaterialArray : IDisposable
{
    private readonly List<GpuMaterial> _items = new();
    private readonly Dictionary<GpuMaterial, int> _index = new();
    private RenderBuffer<MaterialGpuData>? _buffer;
    private readonly GpuDevice _device;
    private int _capacity;

    public GpuMaterialArray(GpuDevice device, int initialCapacity = 16)
    {
        _device = device;
        _capacity = Math.Max(1, initialCapacity);
        _buffer = new RenderBuffer<MaterialGpuData>(device, _capacity, "materialArray");
    }

    public int Count => _items.Count;
    public IReadOnlyList<GpuMaterial> Items => _items;
    public GpuBuffer Buffer => _buffer!.Buffer;
    public uint BindlessIndex => Buffer.BindlessIndex;

    /// <summary>material を配列に登録して安定 index を返す。既登録なら既存 index を返す。</summary>
    public int Register(GpuMaterial mat)
    {
        if (_index.TryGetValue(mat, out var existing)) return existing;
        int idx = _items.Count;
        _items.Add(mat);
        _index[mat] = idx;
        EnsureCapacity(idx + 1);
        Write(idx, mat);
        return idx;
    }

    public int? IndexOf(GpuMaterial mat) => _index.TryGetValue(mat, out var i) ? i : null;

    /// <summary>特定 material の shader data を書き直す (parameter 変更後に呼ぶ)。</summary>
    public void MarkDirty(GpuMaterial mat)
    {
        if (!_index.TryGetValue(mat, out var idx)) return;
        Write(idx, mat);
    }

    /// <summary>変更を GPU に反映 (通常は次 Pump で自動、即時反映したいときに)。</summary>
    public void FlushImmediate() => _buffer?.FlushImmediate();

    private void Write(int idx, GpuMaterial mat)
    {
        _buffer!.Data[idx] = mat.ToShaderData();
        _buffer.MarkDirty();
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _capacity) return;
        int newCap = Math.Max(_capacity * 2, needed);
        var newBuf = new RenderBuffer<MaterialGpuData>(_device, newCap, "materialArray");
        for (int i = 0; i < _items.Count; i++) newBuf.Data[i] = _items[i].ToShaderData();
        newBuf.MarkDirty();
        newBuf.FlushImmediate();
        _buffer?.Dispose();
        _buffer = newBuf;
        _capacity = newCap;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _items.Clear();
        _index.Clear();
    }
}
