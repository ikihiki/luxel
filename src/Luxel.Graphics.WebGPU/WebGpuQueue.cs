using System.Runtime.InteropServices;
using Luxel.Graphics.Abstraction;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;

namespace Luxel.Graphics.WebGPU;

internal sealed unsafe class WebGpuQueue : IGpuBackendQueue
{
    private static readonly BufferMapCallback MapCallback = OnMapped;
    private readonly WebGpuBackend _backend;
    private readonly WebGpuApi _api;
    private readonly Queue* _queue;
    private readonly object _sync;
    private readonly Wgpu _native;

    internal WebGpuQueue(WebGpuBackend backend, Queue* queue, object sync)
    {
        _backend = backend; _api = backend.Api; _queue = queue; _sync = sync;
        if (!_api.TryGetDeviceExtension(backend.Device, out _native!))
            throw new WebGpuUnavailableException("The wgpu-native DevicePoll extension is unavailable.");
    }

    public IGpuBackendCommandBuffer StartCommandRecording() => new WebGpuCommandBuffer(_backend);

    public void Submit(IGpuBackendCommandBuffer commandBuffer)
    {
        if (commandBuffer is not WebGpuCommandBuffer command || command.Handle == null)
            throw new ArgumentException("A finished WebGPU command buffer is required.", nameof(commandBuffer));
        lock (_sync)
        {
            foreach (var buffer in _backend.Buffers) buffer.Upload(_api, _queue, _backend.Arena);
            command.UploadRoots(_queue);
            var handle = command.Handle;
            _api.QueueSubmit(_queue, 1, &handle);
            ReadBackHostCachedBuffers();
        }
    }

    public void WaitIdle()
    {
        lock (_sync) _native.DevicePoll(_backend.Device, true, null);
    }

    private void ReadBackHostCachedBuffers()
    {
        var buffers = _backend.Buffers.Where(static buffer => buffer.Kind == GpuMemoryKind.HostCached).ToArray();
        if (buffers.Length == 0) { _native.DevicePoll(_backend.Device, true, null); return; }

        ulong totalSize = 0;
        var offsets = new ulong[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            totalSize = WebGpuBackend.AlignUp(totalSize, 4);
            offsets[i] = totalSize;
            totalSize += WebGpuBackend.AlignUp(buffers[i].Size, 4);
        }

        var readbackDescriptor = new BufferDescriptor { Size = totalSize, Usage = BufferUsage.MapRead | BufferUsage.CopyDst };
        WgpuBuffer* readback = _api.DeviceCreateBuffer(_backend.Device, in readbackDescriptor);
        if (readback == null) throw new InvalidOperationException("Failed to create WebGPU readback buffer.");
        try
        {
            var encoderDescriptor = new CommandEncoderDescriptor();
            CommandEncoder* encoder = _api.DeviceCreateCommandEncoder(_backend.Device, in encoderDescriptor);
            if (encoder == null) throw new InvalidOperationException("Failed to create WebGPU readback encoder.");
            try
            {
                for (int i = 0; i < buffers.Length; i++)
                    _api.CommandEncoderCopyBufferToBuffer(encoder, _backend.Arena, buffers[i].Offset, readback, offsets[i], WebGpuBackend.AlignUp(buffers[i].Size, 4));
                var commandDescriptor = new CommandBufferDescriptor();
                CommandBuffer* command = _api.CommandEncoderFinish(encoder, in commandDescriptor);
                if (command == null) throw new InvalidOperationException("Failed to finish WebGPU readback commands.");
                try { _api.QueueSubmit(_queue, 1, &command); }
                finally { _api.CommandBufferRelease(command); }
            }
            finally { _api.CommandEncoderRelease(encoder); }

            var state = new MapState();
            var gcHandle = GCHandle.Alloc(state);
            try
            {
                _api.BufferMapAsync(readback, MapMode.Read, 0, (nuint)totalSize, new PfnBufferMapCallback(MapCallback), (void*)GCHandle.ToIntPtr(gcHandle));
                for (int i = 0; i < 1000 && !state.Completed; i++)
                    _native.DevicePoll(_backend.Device, true, null);
                if (!state.Completed) throw new TimeoutException("Timed out mapping WebGPU readback buffer.");
                if (state.Status != BufferMapAsyncStatus.Success) throw new InvalidOperationException($"WebGPU readback mapping failed: {state.Status}.");
                byte* mapped = (byte*)_api.BufferGetConstMappedRange(readback, 0, (nuint)totalSize);
                if (mapped == null) throw new InvalidOperationException("WebGPU returned a null readback mapping.");
                for (int i = 0; i < buffers.Length; i++) buffers[i].CopyFromMapped(mapped + offsets[i]);
                _api.BufferUnmap(readback);
            }
            finally { gcHandle.Free(); }
        }
        finally
        {
            _api.BufferDestroy(readback);
            _api.BufferRelease(readback);
        }
    }

    private static void OnMapped(BufferMapAsyncStatus status, void* userData)
    {
        var state = (MapState)GCHandle.FromIntPtr((nint)userData).Target!;
        state.Status = status;
        state.Completed = true;
    }

    private sealed class MapState
    {
        public volatile bool Completed;
        public BufferMapAsyncStatus Status;
    }
}
