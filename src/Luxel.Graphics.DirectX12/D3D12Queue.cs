using Luxel.Graphics.Abstraction;
using Vortice.Direct3D12;

namespace Luxel.Graphics.DirectX12;

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
    private readonly GpuLifecycleSource _lifecycle;

    public D3D12Queue(ID3D12Device device, ID3D12CommandQueue queue,
                      ID3D12RootSignature rootSignature, ID3D12DescriptorHeap resourceHeap,
                      ID3D12DescriptorHeap samplerHeap, object queueLock, GpuLifecycleSource lifecycle)
    {
        _device = device;
        _queue = queue;
        _rootSignature = rootSignature;
        _resourceHeap = resourceHeap;
        _samplerHeap = samplerHeap;
        _queueLock = queueLock;
        _lifecycle = lifecycle;
        _fence = device.CreateFence(0, FenceFlags.None);
    }

    public IGpuBackendCommandBuffer StartCommandRecording()
        => new D3D12CommandList(_device, _rootSignature, _resourceHeap, _samplerHeap);

    public void Submit(IGpuBackendCommandBuffer commandBuffer)
    {
        var list = ((D3D12CommandList)commandBuffer).Handle;
        try
        {
            lock (_queueLock) _queue.ExecuteCommandList(list);
        }
        catch (Exception exception)
        {
            PublishDeviceRemoved("ExecuteCommandList", exception);
            throw;
        }
    }

    public void WaitIdle()
    {
        ulong target;
        try
        {
            lock (_queueLock)   // ++ と Signal を直列化しキュー上の順序を保証、完了待ちはロック外
            {
                target = ++_fenceValue;
                _queue.Signal(_fence, target);
            }
        }
        catch (Exception exception)
        {
            PublishDeviceRemoved("Signal", exception);
            throw;
        }

        while (_fence.CompletedValue < target)
        {
            var removed = _device.DeviceRemovedReason;
            if (removed.Failure)
            {
                PublishDeviceRemoved("WaitIdle", null);
                throw new InvalidOperationException($"Direct3D 12 device was removed while waiting for fence {target}: {removed}.");
            }
            Thread.Yield();
        }
    }

    private void PublishDeviceRemoved(string operation, Exception? exception)
    {
        var result = _device.DeviceRemovedReason;
        int code = result.Code;
        GpuLifecycleReason reason = code switch
        {
            unchecked((int)0x887A0006) => GpuLifecycleReason.DeviceHung,
            unchecked((int)0x887A0007) => GpuLifecycleReason.DeviceReset,
            unchecked((int)0x887A0005) => GpuLifecycleReason.DeviceRemoved,
            _ => GpuLifecycleReason.Unknown,
        };
        _lifecycle.DeviceEvent(GpuDeviceLifecycleState.Lost, reason, code, result.ToString(),
            $"Direct3D 12 {operation} failed: {result}.", exception);
    }
}
