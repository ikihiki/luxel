using System.Runtime.InteropServices;
using Luxel;
using Luxel.RenderGraph;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル22 (RenderGraph RG-M1): UI → 分離ガウシアン × 2 段 → 合成 のレンダーグラフ。
///
/// (1) Scene2D で UI を作り、(2) Rasterizer2D が UI バッファへ 1 回ラスタライズ。
/// (3) RenderGraph に取り込み、Transient な 2 つの中間バッファ (tmp / blr) を介して
///     水平→垂直の 9 タップガウシアンを 2 段、(4) 左半分=元の UI / 右半分=ブラー結果 を
///     compute で合成 → 最終バッファ。読み戻して PNG 出力、左右の差で検証する。
///
/// グラフ層は scene-agnostic で、Rasterizer2D / Scene2D を一切知らない (ImportBuffer / CreateBuffer のみ)。
/// </summary>
public static class Sample22BlurComposite
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BlurArgs
    {
        public uint SrcIndex, DstIndex, Width, Height;
        public int DirX, DirY;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositeArgs
    {
        public uint UiIndex, BlurIndex, DstIndex, Width, Height, SplitX, Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        const uint splitX = w / 2;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // --- UI を先にバッファへラスタライズ (RetainedCanvas は経由せず即時 Scene2D) ---
        using var raster = new Rasterizer2D(device);
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Rgba(60, 130, 240, 255),  20, 20, 100, 60, 12);
        scene.FillRoundedRect(Color2D.Rgba(230, 80, 100, 255), 140, 30, 90, 90, 16);
        scene.FillRoundedRect(Color2D.Rgba(40, 200, 120, 255),  30, 110, 120, 40, 8);
        scene.FillRoundedRect(Color2D.Rgba(255, 220, 60, 255), 170, 140, 60, 90, 10);
        scene.FillRoundedRect(Color2D.Rgba(180, 90, 220, 255),  20, 170, 130, 60, 14);
        using var encoded = raster.Encode(scene);

        using GpuBuffer ui  = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        using GpuBuffer final = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        ui.Span<byte>((int)fbBytes).Clear();
        final.Span<byte>((int)fbBytes).Clear();

        // UI を ui バッファへ書く。
        using (GpuCommandBuffer cmd0 = device.MainQueue.StartCommandRecording())
        {
            raster.Render(cmd0, encoded, Camera2D.Pixels, w, h, ui);
            cmd0.Finish();
            device.MainQueue.SubmitAndWait(cmd0);
        }

        // --- RenderGraph 構築 ---
        using GpuPipeline blurPipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute_blur"));
        using GpuPipeline compositePipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute_composite"));

        using var rg = new Luxel.RenderGraph.RenderGraph(device);

        BufferHandle hUi    = rg.ImportBuffer(ui,    "ui");
        BufferHandle hFinal = rg.ImportBuffer(final, "final");
        BufferHandle hTmp   = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blurH");
        BufferHandle hBlr   = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blurV");

        rg.AddPass("BlurH", PassQueue.Compute)
          .Read(hUi)
          .Write(hTmp)
          .Execute(ctx =>
          {
              var args = new BlurArgs
              {
                  SrcIndex = ctx.BindlessIndex(hUi),
                  DstIndex = ctx.BindlessIndex(hTmp),
                  Width = w, Height = h,
                  DirX = 1, DirY = 0,
              };
              ctx.Cmd.SetComputePipeline(blurPipeline)
                     .SetRootArguments(args)
                     .Dispatch((w + 7) / 8, (h + 7) / 8);
          });

        rg.AddPass("BlurV", PassQueue.Compute)
          .Read(hTmp)
          .Write(hBlr)
          .Execute(ctx =>
          {
              var args = new BlurArgs
              {
                  SrcIndex = ctx.BindlessIndex(hTmp),
                  DstIndex = ctx.BindlessIndex(hBlr),
                  Width = w, Height = h,
                  DirX = 0, DirY = 1,
              };
              ctx.Cmd.SetComputePipeline(blurPipeline)
                     .SetRootArguments(args)
                     .Dispatch((w + 7) / 8, (h + 7) / 8);
          });

        rg.AddPass("Composite", PassQueue.Compute)
          .Read(hUi)
          .Read(hBlr)
          .Write(hFinal)
          .Execute(ctx =>
          {
              var args = new CompositeArgs
              {
                  UiIndex = ctx.BindlessIndex(hUi),
                  BlurIndex = ctx.BindlessIndex(hBlr),
                  DstIndex = ctx.BindlessIndex(hFinal),
                  Width = w, Height = h, SplitX = splitX,
              };
              ctx.Cmd.SetComputePipeline(compositePipeline)
                     .SetRootArguments(args)
                     .Dispatch((w + 7) / 8, (h + 7) / 8);
          });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // --- 検証 ---
        Span<byte> fpx = final.Span<byte>((int)fbBytes);
        Span<byte> upx = ui.Span<byte>((int)fbBytes);

        // 左半分 (x < splitX): final == ui であること
        // 右半分: blur 結果のためエッジ近傍では明確に差が出る
        int leftMatches = 0, leftSampled = 0;
        int rightDiffs = 0, rightSampled = 0;
        for (uint y = 16; y < h - 16; y += 8)
        {
            for (uint x = 4; x < splitX - 4; x += 8)
            {
                int idx = (int)(y * w + x) * 4;
                if (fpx[idx] == upx[idx] && fpx[idx + 1] == upx[idx + 1] && fpx[idx + 2] == upx[idx + 2])
                    leftMatches++;
                leftSampled++;
            }
            for (uint x = splitX + 4; x < w - 4; x += 8)
            {
                int idx = (int)(y * w + x) * 4;
                int diff = Math.Abs(fpx[idx] - upx[idx]) + Math.Abs(fpx[idx + 1] - upx[idx + 1]) + Math.Abs(fpx[idx + 2] - upx[idx + 2]);
                if (diff > 4) rightDiffs++;
                rightSampled++;
            }
        }
        Console.WriteLine($"  左半分 (UI 透過): match {leftMatches}/{leftSampled}");
        Console.WriteLine($"  右半分 (Blur 適用): differ {rightDiffs}/{rightSampled}");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "rendergraph_blur.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, fpx);
        Console.WriteLine($"PNG 出力: {pngPath}");

        // 左半分は UI と一致、右半分は背景+エッジ近傍のうちエッジ部で差が出ているはず。
        // シーンは余白が多いので閾値は緩めに 1/8 以上 (背景同士は元から同色なので差が出ない)。
        bool leftOk = leftMatches >= leftSampled * 95 / 100;
        bool rightOk = rightDiffs >= rightSampled / 8;
        if (leftOk && rightOk)
        {
            Console.WriteLine("OK: RenderGraph (UI → BlurH → BlurV → Composite) が動作");
            return 0;
        }
        Console.Error.WriteLine($"FAILED: leftOk={leftOk}, rightOk={rightOk}");
        return 1;
    }
}
