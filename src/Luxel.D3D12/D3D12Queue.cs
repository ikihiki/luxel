using Luxel.Abstraction;
using Vortice.Direct3D12;

namespace Luxel.D3D12;

internal sealed class D3D12Queue : IGpuBackendQueue
{
    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _queue;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12DescriptorHeap _resourceHeap;
    private readonly ID3D12DescriptorHeap _samplerHeap;
    private readonly ID3D12Fence _fence;
    private ulong _fenceValue;
    private readonly object _queueLock;   // OneShotSubmit と共有 (同一 ID3D12CommandQueue の外部同期)

    public D3D12Queue(ID3D12Device device, ID3D12CommandQueue queue,
                      ID3D12RootSignature rootSignature, ID3D12DescriptorHeap resourceHeap,
                      ID3D12DescriptorHeap samplerHeap, object queueLock)
    {
        _device = device;
        _queue = queue;
        _rootSignature = rootSignature;
        _resourceHeap = resourceHeap;
        _samplerHeap = samplerHeap;
        _queueLock = queueLock;
        _fence = device.CreateFence(0, FenceFlags.None);
    }

    public IGpuBackendCommandBuffer StartCommandRecording()
        => new D3D12CommandList(_device, _rootSignature, _resourceHeap, _samplerHeap);

    public void Submit(IGpuBackendCommandBuffer commandBuffer)
    {
        var list = ((D3D12CommandList)commandBuffer).Handle;
        lock (_queueLock) _queue.ExecuteCommandList(list);
    }

    public void WaitIdle()
    {
        ulong target;
        lock (_queueLock)   // ++ と Signal を直列化しキュー上の順序を保証、完了待ちはロック外
        {
            target = ++_fenceValue;
            _queue.Signal(_fence, target);
        }
        while (_fence.CompletedValue < target) Thread.Yield();
    }
}
