namespace Luxel.AssetsGpu;

/// <summary>
/// 差替可能な GPU テクスチャ。RG が毎フレーム描画結果を書き込む先として使い、
/// Resources 経由で他コンポーネント (別 RG / UI / thumbnail) が最新テクスチャを引ける (RGRE-M2a)。
///
/// テクスチャ本体 (<see cref="Texture"/>) は解像度変更が必要な場合のみ差し替わる。
/// 描画完了通知は <see cref="Luxel.Resources.ResourceHandle{T}.Reloaded"/> を通じて Pump で伝播する。
/// </summary>
public sealed class RenderTarget : IDisposable
{
    private readonly GpuDevice _device;
    private GpuTexture _texture;
    private volatile bool _touched;   // 描画側が「更新した」と signal
    private int _version;
    private bool _disposed;

    public RenderTarget(GpuDevice device, uint width, uint height, GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        _device = device;
        _texture = device.CreateRenderTarget(width, height, format);
        Width = width; Height = height; Format = format;
    }

    public GpuTexture Texture => _texture;
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public GpuFormat Format { get; }
    public int Version => _version;

    /// <summary>RG 側で 1 パス描画が終わったら呼ぶ ─ 次 Pump で Reloaded 発火。</summary>
    public void MarkTouched() => _touched = true;

    /// <summary>解像度を変更 (動的解像度)。古い GpuTexture は次 Pump で解放。</summary>
    public void Resize(uint width, uint height)
    {
        if (width == Width && height == Height) return;
        var oldTex = _texture;
        _texture = _device.CreateRenderTarget(width, height, Format);
        Width = width; Height = height;
        _pendingDispose = oldTex;
        _touched = true;
    }
    private GpuTexture? _pendingDispose;

    /// <summary>Pump から呼ばれる ─ touched なら Version++ + true を返す。</summary>
    internal bool Flush()
    {
        if (_pendingDispose is not null)
        {
            _pendingDispose.Dispose();
            _pendingDispose = null;
        }
        if (!_touched) return false;
        _touched = false;
        _version++;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture.Dispose();
        _pendingDispose?.Dispose();
    }
}
