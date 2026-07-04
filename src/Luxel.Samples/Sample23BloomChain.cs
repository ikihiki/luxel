using System.Runtime.InteropServices;
using Luxel;
using Luxel.RenderGraph;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル23 (RenderGraph RG-M2): 反復ブラー (4 段) で **Transient aliasing** と **デッドパスカリング** を実証。
///
/// パス順 (登録順):
///   1. BlurH(ui) → tmp1
///   2. BlurV(tmp1) → blur1
///   3. BlurH(blur1) → tmp2     ← tmp1 はここで不要 (寿命終了) → tmp2 と物理 alias
///   4. BlurV(tmp2) → blur2     ← blur1 ここで不要 → blur2 と物理 alias
///   5. Composite(ui, blur2) → final
///   6. DeadPass: write deadBuf  (誰も読まない → デッドパスカリングで除外)
///
/// 期待: 4 個の 256x256 Transient が 2 個の物理バッファに alias、DeadPass は culled。
/// vk/dx で同一の最終ピクセル。
/// </summary>
public static class Sample23BloomChain
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
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // --- UI を ui バッファへラスタライズ ---
        using var raster = new Rasterizer2D(device);
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Rgba(60, 130, 240, 255),  20, 20, 100, 60, 12);
        scene.FillRoundedRect(Color2D.Rgba(230, 80, 100, 255), 140, 30, 90, 90, 16);
        scene.FillRoundedRect(Color2D.Rgba(40, 200, 120, 255),  30, 110, 120, 40, 8);
        scene.FillRoundedRect(Color2D.Rgba(255, 220, 60, 255), 170, 140, 60, 90, 10);
        scene.FillRoundedRect(Color2D.Rgba(180, 90, 220, 255),  20, 170, 130, 60, 14);
        using var encoded = raster.Encode(scene);

        using GpuBuffer ui    = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        using GpuBuffer final = device.Malloc(fbBytes, GpuMemoryKind.HostMapped);
        ui.Span<byte>((int)fbBytes).Clear();
        final.Span<byte>((int)fbBytes).Clear();

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
        BufferHandle hTmp1  = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "tmp1");
        BufferHandle hBlur1 = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blur1");
        BufferHandle hTmp2  = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "tmp2");
        BufferHandle hBlur2 = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blur2");
        BufferHandle hDead  = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "dead");

        Action<PassContext> MakeBlurExecute(BufferHandle src, BufferHandle dst, int dx, int dy) => ctx =>
        {
            var args = new BlurArgs
            {
                SrcIndex = ctx.BindlessIndex(src), DstIndex = ctx.BindlessIndex(dst),
                Width = w, Height = h, DirX = dx, DirY = dy,
            };
            ctx.Cmd.SetComputePipeline(blurPipeline).SetRootArguments(args).Dispatch((w + 7) / 8, (h + 7) / 8);
        };

        rg.AddPass("BlurH1", PassQueue.Compute).Read(hUi).Write(hTmp1)
          .Execute(MakeBlurExecute(hUi, hTmp1, 1, 0));
        rg.AddPass("BlurV1", PassQueue.Compute).Read(hTmp1).Write(hBlur1)
          .Execute(MakeBlurExecute(hTmp1, hBlur1, 0, 1));
        rg.AddPass("BlurH2", PassQueue.Compute).Read(hBlur1).Write(hTmp2)
          .Execute(MakeBlurExecute(hBlur1, hTmp2, 1, 0));
        rg.AddPass("BlurV2", PassQueue.Compute).Read(hTmp2).Write(hBlur2)
          .Execute(MakeBlurExecute(hTmp2, hBlur2, 0, 1));

        rg.AddPass("Composite", PassQueue.Compute)
          .Read(hUi).Read(hBlur2).Write(hFinal)
          .Execute(ctx =>
          {
              var args = new CompositeArgs
              {
                  UiIndex = ctx.BindlessIndex(hUi),
                  BlurIndex = ctx.BindlessIndex(hBlur2),
                  DstIndex = ctx.BindlessIndex(hFinal),
                  Width = w, Height = h, SplitX = w / 2,
              };
              ctx.Cmd.SetComputePipeline(compositePipeline).SetRootArguments(args).Dispatch((w + 7) / 8, (h + 7) / 8);
          });

        // 誰も読まない → カリングで除外されるべきパス。
        rg.AddPass("DeadPass", PassQueue.Compute).Write(hDead).Execute(_ =>
        {
            throw new InvalidOperationException("DeadPass は culled されたはずなので実行されない");
        });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // --- 検証 1: aliasing と culling ---
        int physical = rg.PhysicalTransientBufferCount;
        int executed = rg.LastExecutedPassCount;
        bool deadCulled = rg.IsPassCulled(5);
        bool t1aliased = rg.IsAliased(hTmp1) && rg.IsAliased(hTmp2);
        bool b1aliased = rg.IsAliased(hBlur1) && rg.IsAliased(hBlur2);
        // tmp1/tmp2 同 slot、blur1/blur2 同 slot
        int slotTmp = rg.GetPhysicalSlot(hTmp1);
        int slotBlr = rg.GetPhysicalSlot(hBlur1);
        bool slotPairs = slotTmp == rg.GetPhysicalSlot(hTmp2) && slotBlr == rg.GetPhysicalSlot(hBlur2);

        Console.WriteLine($"  論理 Transient: 5 (tmp1/blur1/tmp2/blur2/dead)");
        Console.WriteLine($"  物理 Transient バッファ数: {physical} (dead は割当なし + 同形 alias で減るはず)");
        Console.WriteLine($"  実行パス数: {executed} / 登録 6 (DeadPass culled)");
        Console.WriteLine($"  alias: tmp1↔tmp2 同 slot={slotTmp}, blur1↔blur2 同 slot={slotBlr}");

        // --- 検証 2: 画素 ---
        Span<byte> fpx = final.Span<byte>((int)fbBytes);
        Span<byte> upx = ui.Span<byte>((int)fbBytes);
        int leftMatches = 0, leftSampled = 0, rightDiffs = 0, rightSampled = 0;
        uint splitX = w / 2;
        for (uint y = 16; y < h - 16; y += 8)
        {
            for (uint x = 4; x < splitX - 4; x += 8)
            {
                int idx = (int)(y * w + x) * 4;
                if (fpx[idx] == upx[idx] && fpx[idx + 1] == upx[idx + 1] && fpx[idx + 2] == upx[idx + 2]) leftMatches++;
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
        Console.WriteLine($"  右半分 (Blur×2 適用): differ {rightDiffs}/{rightSampled}");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "rendergraph_bloom.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, fpx);
        Console.WriteLine($"PNG 出力: {pngPath}");

        bool pixelsOk = leftMatches >= leftSampled * 95 / 100 && rightDiffs >= rightSampled / 8;
        bool aliasingOk = physical == 2 && deadCulled && executed == 5
            && t1aliased && b1aliased && slotPairs;
        if (pixelsOk && aliasingOk)
        {
            Console.WriteLine("OK: RG-M2 (transient aliasing + dead pass culling + 反復ブラー) 検証成功");
            return 0;
        }
        Console.Error.WriteLine($"FAILED: pixelsOk={pixelsOk}, aliasingOk={aliasingOk}");
        return 1;
    }
}
