using Luxel.Abstraction;

namespace Luxel.D3D12;

internal sealed class D3D12Sampler : IGpuBackendSampler
{
    public D3D12Sampler(uint bindlessIndex) => BindlessIndex = bindlessIndex;

    public uint BindlessIndex { get; }

    public void Dispose() { /* サンプラはヒープ記述子のみ。解放はヒープ破棄時 */ }
}
