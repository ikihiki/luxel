using Luxel.Graphics.RenderGraph;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// Luxel.Graphics.RenderGraph と Resources の橋渡し (RGRE-M1/M2)。
/// <see cref="ResourceHandle{T}"/> を直接受ける Import 糖衣と、RenderBuffer/RenderTarget への write 糖衣を提供。
/// </summary>
public static class RenderGraphExtensions
{
    // === M1: Import (Resources → RG) ===

    /// <summary>Resources からロード済みの <see cref="GpuTexture"/> を RG に import する。</summary>
    public static TextureHandle ImportTexture(this global::Luxel.Graphics.RenderGraph.RenderGraph rg, ResourceHandle<GpuTexture> handle, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!handle.IsReady || handle.Value is null)
            throw new InvalidOperationException($"ResourceHandle<GpuTexture> ({handle.Uri}) not ready. Await handle.Ready first.");
        return rg.ImportTexture(handle.Value, name ?? handle.Uri.ToString());
    }

    /// <summary>Resources からロード済みの <see cref="GpuBuffer"/> を RG に import する。</summary>
    public static BufferHandle ImportBuffer(this global::Luxel.Graphics.RenderGraph.RenderGraph rg, ResourceHandle<GpuBuffer> handle, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!handle.IsReady || handle.Value is null)
            throw new InvalidOperationException($"ResourceHandle<GpuBuffer> ({handle.Uri}) not ready. Await handle.Ready first.");
        return rg.ImportBuffer(handle.Value, name ?? handle.Uri.ToString());
    }

    // === M2a: Publish 型を RG に import (Import 糖衣) ===

    /// <summary><see cref="RenderTarget"/> を RG に import して TextureHandle を得る。</summary>
    public static TextureHandle ImportRenderTarget(this global::Luxel.Graphics.RenderGraph.RenderGraph rg, RenderTarget target, string name = "renderTarget")
    {
        ArgumentNullException.ThrowIfNull(target);
        return rg.ImportTexture(target.Texture, name);
    }

    /// <summary><see cref="RenderBuffer{T}"/> を RG に import して BufferHandle を得る。</summary>
    public static BufferHandle ImportRenderBuffer<T>(this global::Luxel.Graphics.RenderGraph.RenderGraph rg, RenderBuffer<T> buffer, string name = "renderBuffer") where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return rg.ImportBuffer(buffer.Buffer, name);
    }
}
