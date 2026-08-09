using System.Runtime.InteropServices.JavaScript;

namespace Luxel.Graphics.WebGPU.Browser;

internal interface IBrowserWebGpuInterop
{
    Task<string> InitializeAsync();
    string GetDiagnostics(int backend);
    string DrainLifecycleEvents(int backend);
    void RecordDiagnosticsError(int backend, string source, string name, string message, string stack);
    int CreateComputePipeline(int backend, string wgslBase64, string entryPoint);
    int CreateGraphicsPipeline(int backend, string vsBase64, string vsEntry, string psBase64, string psEntry, string rasterJson);
    int CreateTexture(int backend, int width, int height, int format, int usage, int bindlessIndex, string dataBase64);
    int CreateSampler(int backend, int filter, int address, int bindlessIndex);
    int CreateCommandBuffer(int backend);
    void CommandSetComputePipeline(int command, int pipeline);
    void CommandSetGraphicsPipeline(int command, int pipeline);
    void CommandSetRootConstants(int command, string dataBase64);
    void CommandDispatch(int command, int x, int y, int z);
    void CommandBeginRendering(int command, int color, int depth, float r, float g, float b, float a, float clearDepth, int clearStencil);
    void CommandSetStencilReference(int command, int reference);
    void CommandSetViewport(int command, float x, float y, float width, float height, float minDepth, float maxDepth);
    void CommandSetScissor(int command, int x, int y, int width, int height);
    void CommandEndRendering(int command);
    void CommandDraw(int command, int vertexCount, int instanceCount);
    void CommandCopyTextureToBuffer(int command, int texture, int destinationOffset, int bytesPerRow, int width, int height);
    void CommandCopyBufferToBuffer(int command, int sourceOffset, int destinationOffset, int bytes);
    void CommandBarrier(int command);
    void CommandFinish(int command);
    void UploadArena(int backend, int offset, string dataBase64);
    int Submit(int backend, int command);
    Task<string> CompleteAsync(int backend, int serial, string readbacksJson);
    Task<string> WaitIdleAsync(int backend, string readbacksJson);
    int CreateSurface(int backend, string canvasToken, int width, int height);
    void SurfacePresent(int surface, int sourceOffset, int stride, int width, int height);
    void SurfaceResize(int surface, int width, int height);
    void Release(int kind, int handle);
    string DisposeBackend(int backend);
}

internal sealed partial class BrowserWebGpuInterop : IBrowserWebGpuInterop
{
    private const string Module = "./luxel-webgpu-browser.js";

    public Task<string> InitializeAsync() => InitializeCoreAsync();
    public string GetDiagnostics(int backend) => GetDiagnosticsCore(backend);
    public string DrainLifecycleEvents(int backend) => DrainLifecycleEventsCore(backend);
    public void RecordDiagnosticsError(int backend, string source, string name, string message, string stack) => RecordDiagnosticsErrorCore(backend, source, name, message, stack);
    public int CreateComputePipeline(int backend, string wgslBase64, string entryPoint) => CreateComputePipelineCore(backend, wgslBase64, entryPoint);
    public int CreateGraphicsPipeline(int backend, string vsBase64, string vsEntry, string psBase64, string psEntry, string rasterJson) => CreateGraphicsPipelineCore(backend, vsBase64, vsEntry, psBase64, psEntry, rasterJson);
    public int CreateTexture(int backend, int width, int height, int format, int usage, int bindlessIndex, string dataBase64) => CreateTextureCore(backend, width, height, format, usage, bindlessIndex, dataBase64);
    public int CreateSampler(int backend, int filter, int address, int bindlessIndex) => CreateSamplerCore(backend, filter, address, bindlessIndex);
    public int CreateCommandBuffer(int backend) => CreateCommandBufferCore(backend);
    public void CommandSetComputePipeline(int command, int pipeline) => CommandSetComputePipelineCore(command, pipeline);
    public void CommandSetGraphicsPipeline(int command, int pipeline) => CommandSetGraphicsPipelineCore(command, pipeline);
    public void CommandSetRootConstants(int command, string dataBase64) => CommandSetRootConstantsCore(command, dataBase64);
    public void CommandDispatch(int command, int x, int y, int z) => CommandDispatchCore(command, x, y, z);
    public void CommandBeginRendering(int command, int color, int depth, float r, float g, float b, float a, float clearDepth, int clearStencil) => CommandBeginRenderingCore(command, color, depth, r, g, b, a, clearDepth, clearStencil);
    public void CommandSetStencilReference(int command, int reference) => CommandSetStencilReferenceCore(command, reference);
    public void CommandSetViewport(int command, float x, float y, float width, float height, float minDepth, float maxDepth) => CommandSetViewportCore(command, x, y, width, height, minDepth, maxDepth);
    public void CommandSetScissor(int command, int x, int y, int width, int height) => CommandSetScissorCore(command, x, y, width, height);
    public void CommandEndRendering(int command) => CommandEndRenderingCore(command);
    public void CommandDraw(int command, int vertexCount, int instanceCount) => CommandDrawCore(command, vertexCount, instanceCount);
    public void CommandCopyTextureToBuffer(int command, int texture, int destinationOffset, int bytesPerRow, int width, int height) => CommandCopyTextureToBufferCore(command, texture, destinationOffset, bytesPerRow, width, height);
    public void CommandCopyBufferToBuffer(int command, int sourceOffset, int destinationOffset, int bytes) => CommandCopyBufferToBufferCore(command, sourceOffset, destinationOffset, bytes);
    public void CommandBarrier(int command) => CommandBarrierCore(command);
    public void CommandFinish(int command) => CommandFinishCore(command);
    public void UploadArena(int backend, int offset, string dataBase64) => UploadArenaCore(backend, offset, dataBase64);
    public int Submit(int backend, int command) => SubmitCore(backend, command);
    public Task<string> CompleteAsync(int backend, int serial, string readbacksJson) => CompleteCoreAsync(backend, serial, readbacksJson);
    public Task<string> WaitIdleAsync(int backend, string readbacksJson) => WaitIdleCoreAsync(backend, readbacksJson);
    public int CreateSurface(int backend, string canvasToken, int width, int height) => CreateSurfaceCore(backend, canvasToken, width, height);
    public void SurfacePresent(int surface, int sourceOffset, int stride, int width, int height) => SurfacePresentCore(surface, sourceOffset, stride, width, height);
    public void SurfaceResize(int surface, int width, int height) => SurfaceResizeCore(surface, width, height);
    public void Release(int kind, int handle) => ReleaseCore(kind, handle);
    public string DisposeBackend(int backend) => DisposeBackendCore(backend);

    [JSImport("initialize", Module)] private static partial Task<string> InitializeCoreAsync();
    [JSImport("getDiagnostics", Module)] private static partial string GetDiagnosticsCore(int backend);
    [JSImport("drainLifecycleEvents", Module)] private static partial string DrainLifecycleEventsCore(int backend);
    [JSImport("recordDiagnosticsError", Module)] private static partial void RecordDiagnosticsErrorCore(int backend, string source, string name, string message, string stack);
    [JSImport("createComputePipeline", Module)] private static partial int CreateComputePipelineCore(int backend, string wgslBase64, string entryPoint);
    [JSImport("createGraphicsPipeline", Module)] private static partial int CreateGraphicsPipelineCore(int backend, string vsBase64, string vsEntry, string psBase64, string psEntry, string rasterJson);
    [JSImport("createTexture", Module)] private static partial int CreateTextureCore(int backend, int width, int height, int format, int usage, int bindlessIndex, string dataBase64);
    [JSImport("createSampler", Module)] private static partial int CreateSamplerCore(int backend, int filter, int address, int bindlessIndex);
    [JSImport("createCommandBuffer", Module)] private static partial int CreateCommandBufferCore(int backend);
    [JSImport("commandSetComputePipeline", Module)] private static partial void CommandSetComputePipelineCore(int command, int pipeline);
    [JSImport("commandSetGraphicsPipeline", Module)] private static partial void CommandSetGraphicsPipelineCore(int command, int pipeline);
    [JSImport("commandSetRootConstants", Module)] private static partial void CommandSetRootConstantsCore(int command, string dataBase64);
    [JSImport("commandDispatch", Module)] private static partial void CommandDispatchCore(int command, int x, int y, int z);
    [JSImport("commandBeginRendering", Module)] private static partial void CommandBeginRenderingCore(int command, int color, int depth, float r, float g, float b, float a, float clearDepth, int clearStencil);
    [JSImport("commandSetStencilReference", Module)] private static partial void CommandSetStencilReferenceCore(int command, int reference);
    [JSImport("commandSetViewport", Module)] private static partial void CommandSetViewportCore(int command, float x, float y, float width, float height, float minDepth, float maxDepth);
    [JSImport("commandSetScissor", Module)] private static partial void CommandSetScissorCore(int command, int x, int y, int width, int height);
    [JSImport("commandEndRendering", Module)] private static partial void CommandEndRenderingCore(int command);
    [JSImport("commandDraw", Module)] private static partial void CommandDrawCore(int command, int vertexCount, int instanceCount);
    [JSImport("commandCopyTextureToBuffer", Module)] private static partial void CommandCopyTextureToBufferCore(int command, int texture, int destinationOffset, int bytesPerRow, int width, int height);
    [JSImport("commandCopyBufferToBuffer", Module)] private static partial void CommandCopyBufferToBufferCore(int command, int sourceOffset, int destinationOffset, int bytes);
    [JSImport("commandBarrier", Module)] private static partial void CommandBarrierCore(int command);
    [JSImport("commandFinish", Module)] private static partial void CommandFinishCore(int command);
    [JSImport("uploadArena", Module)] private static partial void UploadArenaCore(int backend, int offset, string dataBase64);
    [JSImport("submit", Module)] private static partial int SubmitCore(int backend, int command);
    [JSImport("complete", Module)] private static partial Task<string> CompleteCoreAsync(int backend, int serial, string readbacksJson);
    [JSImport("waitIdle", Module)] private static partial Task<string> WaitIdleCoreAsync(int backend, string readbacksJson);
    [JSImport("createSurface", Module)] private static partial int CreateSurfaceCore(int backend, string canvasToken, int width, int height);
    [JSImport("surfacePresent", Module)] private static partial void SurfacePresentCore(int surface, int sourceOffset, int stride, int width, int height);
    [JSImport("surfaceResize", Module)] private static partial void SurfaceResizeCore(int surface, int width, int height);
    [JSImport("release", Module)] private static partial void ReleaseCore(int kind, int handle);
    [JSImport("disposeBackend", Module)] private static partial string DisposeBackendCore(int backend);
}
