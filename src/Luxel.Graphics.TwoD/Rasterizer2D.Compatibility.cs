namespace Luxel.Graphics.TwoD;

/// <summary>旧GPU rasterizer名との互換facade。</summary>
[Obsolete("Use GpuDeviceRasterizer2D.")]
public sealed class Rasterizer2D : GpuDeviceRasterizer2D
{
    public Rasterizer2D(GpuDevice device) : base(device) { }

    public new EncodedScene Encode(Scene2D scene) => new(base.Encode(scene));

    public void Render(GpuCommandBuffer commandBuffer, EncodedScene scene, Camera2D camera,
        uint width, uint height, GpuBuffer framebuffer, bool transparent = false)
        => base.Render(commandBuffer, scene.Inner, camera, width, height, framebuffer, transparent);
}

/// <summary>旧GPU encoded scene名との互換facade。</summary>
[Obsolete("Use GpuEncodedScene2D.")]
public sealed class EncodedScene : IRasterScene2D
{
    internal EncodedScene(GpuEncodedScene2D inner) => Inner = inner;
    internal GpuEncodedScene2D Inner { get; }
    public IRasterizer2D Rasterizer => Inner.Rasterizer;
    public void Render(Camera2D camera, IRasterTarget2D target, bool transparent = false)
        => Inner.Render(camera, target, transparent);
    public void Dispose() => Inner.Dispose();
}
