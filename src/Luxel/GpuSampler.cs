using Luxel.Abstraction;

namespace Luxel;

/// <summary>サンプラのフィルタ。</summary>
public enum GpuSamplerFilter
{
    Point,
    Linear,
}

/// <summary>サンプラの UV wrap 挙動 (SamplerAddressMode)。</summary>
public enum GpuSamplerAddress
{
    /// <summary>[0..1] 外は端の色を保持 (デフォルト、既存互換)。</summary>
    Clamp,
    /// <summary>[0..1] 外はタイル状に繰り返す (glTF wrap:REPEAT 相当)。</summary>
    Repeat,
}

/// <summary>bindless サンプラ。<see cref="BindlessIndex"/> でシェーダから参照する。</summary>
public sealed class GpuSampler : IDisposable
{
    private readonly IGpuBackendSampler _sampler;
    private bool _disposed;

    internal GpuSampler(IGpuBackendSampler sampler) => _sampler = sampler;

    /// <summary>サンプラヒープ内インデックス。</summary>
    public uint BindlessIndex => _sampler.BindlessIndex;

    internal IGpuBackendSampler Backend => _sampler;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sampler.Dispose();
    }
}
