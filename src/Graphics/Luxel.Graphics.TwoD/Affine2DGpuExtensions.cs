namespace Luxel.Graphics.TwoD;

internal static class Affine2DGpuExtensions
{
    internal static GpuTransform ToGpu(this Affine2D value) => new()
    {
        A = value.A,
        B = value.B,
        C = value.C,
        D = value.D,
        E = value.E,
        F = value.F,
    };
}
