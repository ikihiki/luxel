using System.Runtime.InteropServices;
using Luxel;

namespace Luxel.Samples;

/// <summary>
/// サンプル01: ブログの compute hello-world (Vulkan/D3D12 統一)。
/// CPU が <c>gpuMalloc</c> バッファに 1024 個の値を書き、GPU がルート引数経由で
/// 2 倍にして別バッファへ書き戻す。CPU が読み戻して <c>out[i] == in[i] * 2</c> を検証する。
///
/// バッファは bindless index (<see cref="GpuBuffer.BindlessIndex"/>) で参照し、
/// ルート引数 (index + 要素数) は inline で渡す。シェーダ・サンプルとも 1 ソースで両対応。
/// </summary>
public static class Sample01ComputeHelloWorld
{
    // compute01.slang の RootArgs と同一レイアウト。
    [StructLayout(LayoutKind.Sequential)]
    private struct RootArgs
    {
        public uint InputIndex;
        public uint OutputIndex;
        public uint Count;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const int n = 1024;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using GpuBuffer input = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);
        using GpuBuffer output = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);

        Span<float> inSpan = input.Span<float>(n);
        for (int i = 0; i < n; i++) inSpan[i] = i;
        output.Span<float>(n).Clear();

        var args = new RootArgs
        {
            InputIndex = input.BindlessIndex,
            OutputIndex = output.BindlessIndex,
            Count = n,
        };

        using GpuPipeline pipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute01"));

        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        cmd.SetComputePipeline(pipeline)
           .SetRootArguments(args)
           .Dispatch((n + 63) / 64)
           .Barrier(GpuStage.ComputeShader, GpuStage.All);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);

        Span<float> outSpan = output.Span<float>(n);
        int errors = 0;
        for (int i = 0; i < n; i++)
        {
            float expected = i * 2f;
            if (outSpan[i] != expected)
            {
                if (errors < 5)
                    Console.Error.WriteLine($"  mismatch [{i}]: got {outSpan[i]}, expected {expected}");
                errors++;
            }
        }

        if (errors == 0)
        {
            Console.WriteLine($"OK: {n} 要素すべてが 2 倍になっていることを確認 (例: out[10]={outSpan[10]})");
            return 0;
        }
        Console.Error.WriteLine($"FAILED: {errors} 件の不一致");
        return 1;
    }
}
