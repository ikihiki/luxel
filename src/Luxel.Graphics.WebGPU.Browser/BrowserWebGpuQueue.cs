using System.Text.Json;
using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics.WebGPU.Browser;

internal sealed class BrowserWebGpuQueue : IAsyncGpuBackendQueue
{
    private readonly BrowserWebGpuBackend _backend;
    private readonly object _sync = new();
    private int _lastSerial;

    internal BrowserWebGpuQueue(BrowserWebGpuBackend backend) => _backend = backend;

    public IGpuBackendCommandBuffer StartCommandRecording()
    {
        _backend.ThrowIfDisposed();
        return new BrowserWebGpuCommandBuffer(_backend);
    }

    /// <summary>Uploads HostMapped shadows and starts queue submission; it never blocks a JavaScript Promise.</summary>
    public void Submit(IGpuBackendCommandBuffer commandBuffer)
    {
        BrowserWebGpuCommandBuffer command = RequireCommand(commandBuffer);
        command.MarkSubmitted();
        UploadHostMappedBuffers();
        lock (_sync) _lastSerial = _backend.Interop.Submit(_backend.Handle, command.Handle);
    }

    public async ValueTask SubmitAsync(IGpuBackendCommandBuffer commandBuffer, CancellationToken cancellationToken = default)
    {
        Submit(commandBuffer);
        int serial;
        lock (_sync) serial = _lastSerial;
        await CompleteAsync(serial, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Browser queues cannot synchronously wait for Promise completion.</summary>
    public void WaitIdle() => throw new PlatformNotSupportedException("Browser WebGPU cannot synchronously block Promise completion. Use WaitIdleAsync().");

    public async ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
    {
        _backend.ThrowIfDisposed();
        BrowserWebGpuBuffer[] readbacks = SnapshotReadbacks();
        string result = await _backend.Interop.WaitIdleAsync(_backend.Handle, SerializeReadbacks(readbacks)).WaitAsync(cancellationToken).ConfigureAwait(false);
        ApplyReadbacks(readbacks, result);
    }

    internal void UploadBuffer(BrowserWebGpuBuffer buffer)
    {
        buffer.ThrowIfDisposed();
        if (buffer.Kind == GpuMemoryKind.HostMapped)
            _backend.Interop.UploadArena(_backend.Handle, checked((int)buffer.Offset), Convert.ToBase64String(buffer.Shadow));
    }

    private async Task CompleteAsync(int serial, CancellationToken cancellationToken)
    {
        BrowserWebGpuBuffer[] readbacks = SnapshotReadbacks();
        string result = await _backend.Interop.CompleteAsync(_backend.Handle, serial, SerializeReadbacks(readbacks)).WaitAsync(cancellationToken).ConfigureAwait(false);
        ApplyReadbacks(readbacks, result);
    }

    private void UploadHostMappedBuffers()
    {
        foreach (BrowserWebGpuBuffer buffer in _backend.SnapshotBuffers()) UploadBuffer(buffer);
    }

    private BrowserWebGpuBuffer[] SnapshotReadbacks()
        => _backend.SnapshotBuffers().Where(static b => b.Kind == GpuMemoryKind.HostCached).ToArray();

    private static string SerializeReadbacks(BrowserWebGpuBuffer[] buffers)
        => JsonSerializer.Serialize(buffers.Select(static b => new { offset = checked((int)b.Offset), size = checked((int)b.Size) }));

    private static void ApplyReadbacks(BrowserWebGpuBuffer[] buffers, string base64)
    {
        if (buffers.Length == 0) return;
        byte[] data = Convert.FromBase64String(base64);
        int cursor = 0;
        foreach (BrowserWebGpuBuffer buffer in buffers)
        {
            if (buffer.IsDisposed) { cursor = checked(cursor + (int)buffer.Size); continue; }
            data.AsSpan(cursor, checked((int)buffer.Size)).CopyTo(buffer.Shadow);
            cursor = checked(cursor + (int)buffer.Size);
        }
        if (cursor != data.Length) throw new InvalidOperationException("Browser WebGPU readback payload length did not match requested buffers.");
    }

    private BrowserWebGpuCommandBuffer RequireCommand(IGpuBackendCommandBuffer value)
    {
        _backend.ThrowIfDisposed();
        if (value is not BrowserWebGpuCommandBuffer command || !ReferenceEquals(command.Owner, _backend))
            throw new ArgumentException("Command buffer belongs to another backend.", nameof(value));
        command.ThrowIfDisposed();
        return command;
    }
}
