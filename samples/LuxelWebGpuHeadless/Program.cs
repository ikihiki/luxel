using Luxel.Graphics;
using Luxel.Graphics.WebGPU;
using LuxelWebGpuHeadless;

try
{
    using var device = new GpuDevice(WebGpuBackend.Create());
    Console.WriteLine(HeadlessWebGpuSample.Run(device).Summary);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"webgpu-headless: status=fail, error={exception.Message}");
    return 1;
}
