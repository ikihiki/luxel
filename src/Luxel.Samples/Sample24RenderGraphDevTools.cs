using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Luxel;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.RenderGraph;
using Luxel.TwoD;

namespace Luxel.Samples;

/// <summary>
/// サンプル24 (RG-M3): RenderGraph を DevTools 越しに観測する。
/// サンプル23 と同じ反復ブラー + DeadPass 構成のグラフを構築 → Execute → HTTP /rendergraph で取得し、
/// passes / resources / aliasing / culling フラグを HttpClient で検証する。
///
/// EngineDiagnostics の DiagnosticListener パイプを介すため、レンダーグラフ→DevTools は疎結合
/// (Luxel.RenderGraph は Luxel.DevTools を一切参照しない)。
/// </summary>
public static class Sample24RenderGraphDevTools
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
        return RunAsync(createDevice).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(Func<GpuDevice> createDevice)
    {
        const uint w = 128, h = 128;
        ulong fbBytes = (ulong)(w * h * 4);

        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // DevTools 起動 (空きポート自動取得、loopback)
        var cmds = new EngineCommands();
        using var listener = new DevToolsListener(cmds);
        using var server = new DebugServer(listener, port: 0);
        server.Start();
        Console.WriteLine($"DevTools server: {server.Url}");

        // --- UI 描画 ---
        using var raster = new Rasterizer2D(device);
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Rgba(60, 130, 240, 255), 10, 10, 50, 30, 8);
        scene.FillRoundedRect(Color2D.Rgba(230, 80, 100, 255), 70, 20, 45, 60, 10);
        scene.FillRoundedRect(Color2D.Rgba(40, 200, 120, 255), 15, 60, 60, 25, 6);
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

        // --- RenderGraph (Sample23 と同じ構造、観測対象) ---
        using GpuPipeline blurPipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute_blur"));
        using GpuPipeline compositePipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute_composite"));

        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hUi    = rg.ImportBuffer(ui, "ui");
        BufferHandle hFinal = rg.ImportBuffer(final, "final");
        BufferHandle hTmp1  = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "tmp1");
        BufferHandle hBlur1 = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blur1");
        BufferHandle hTmp2  = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "tmp2");
        BufferHandle hBlur2 = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blur2");
        BufferHandle hDead  = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "dead");

        Action<PassContext> Blur(BufferHandle src, BufferHandle dst, int dx, int dy) => ctx =>
        {
            var args = new BlurArgs { SrcIndex = ctx.BindlessIndex(src), DstIndex = ctx.BindlessIndex(dst), Width = w, Height = h, DirX = dx, DirY = dy };
            ctx.Cmd.SetComputePipeline(blurPipeline).SetRootArguments(args).Dispatch((w + 7) / 8, (h + 7) / 8);
        };

        rg.AddPass("BlurH1", PassQueue.Compute).Read(hUi).Write(hTmp1).Execute(Blur(hUi, hTmp1, 1, 0));
        rg.AddPass("BlurV1", PassQueue.Compute).Read(hTmp1).Write(hBlur1).Execute(Blur(hTmp1, hBlur1, 0, 1));
        rg.AddPass("BlurH2", PassQueue.Compute).Read(hBlur1).Write(hTmp2).Execute(Blur(hBlur1, hTmp2, 1, 0));
        rg.AddPass("BlurV2", PassQueue.Compute).Read(hTmp2).Write(hBlur2).Execute(Blur(hTmp2, hBlur2, 0, 1));
        rg.AddPass("Composite", PassQueue.Compute).Read(hUi).Read(hBlur2).Write(hFinal).Execute(ctx =>
        {
            var args = new CompositeArgs { UiIndex = ctx.BindlessIndex(hUi), BlurIndex = ctx.BindlessIndex(hBlur2), DstIndex = ctx.BindlessIndex(hFinal), Width = w, Height = h, SplitX = w / 2 };
            ctx.Cmd.SetComputePipeline(compositePipeline).SetRootArguments(args).Dispatch((w + 7) / 8, (h + 7) / 8);
        });
        rg.AddPass("DeadPass", PassQueue.Compute).Write(hDead).Execute(_ => { });

        using (GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // --- HTTP 越しの検証 ---
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };
        int ok = 0, fail = 0;
        void Check(bool cond, string label) { if (cond) { ok++; Console.WriteLine($"  OK  {label}"); } else { fail++; Console.WriteLine($"  NG  {label}"); } }

        // 計装イベントは Listener.OnNext で同期発火するが、用心のため一度 listener にひと呼吸入れる
        await Task.Delay(50);
        string body = await http.GetStringAsync("/rendergraph");
        Check(!string.IsNullOrWhiteSpace(body) && body != "{}", $"/rendergraph 取得 ({body.Length}B)");
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        // passes 配列
        JsonElement passes = root.GetProperty("passes");
        Check(passes.GetArrayLength() == 6, $"passes 数=6 (登録ぶん)");

        // executedPassCount = 5 (DeadPass culled)
        int executed = root.GetProperty("executedPassCount").GetInt32();
        Check(executed == 5, $"executedPassCount=5 (DeadPass culled), got {executed}");

        // DeadPass の culled=true、他は false
        JsonElement dead = default;
        foreach (var p in passes.EnumerateArray()) if (p.GetProperty("name").GetString() == "DeadPass") dead = p;
        Check(dead.ValueKind != JsonValueKind.Undefined && dead.GetProperty("culled").GetBoolean(), "DeadPass.culled=true");
        int culledCount = 0;
        foreach (var p in passes.EnumerateArray()) if (p.GetProperty("culled").GetBoolean()) culledCount++;
        Check(culledCount == 1, $"culled パス数=1, got {culledCount}");

        // resources 配列
        JsonElement resources = root.GetProperty("resources");
        Check(resources.GetArrayLength() == 7, $"resources 数=7 (ui/final/tmp1/blur1/tmp2/blur2/dead)");

        // physicalTransientCount = 2 (alias の効果)
        int physical = root.GetProperty("physicalTransientCount").GetInt32();
        Check(physical == 2, $"physicalTransientCount=2, got {physical}");

        // tmp1/tmp2 が同 PhysicalSlot で aliased
        int slot(string nm) { foreach (var r in resources.EnumerateArray()) if (r.GetProperty("name").GetString() == nm) return r.GetProperty("physicalSlot").GetInt32(); return -2; }
        bool aliased(string nm) { foreach (var r in resources.EnumerateArray()) if (r.GetProperty("name").GetString() == nm) return r.GetProperty("isAliased").GetBoolean(); return false; }
        Check(slot("tmp1") == slot("tmp2") && slot("tmp1") >= 0, $"tmp1/tmp2 同一 slot={slot("tmp1")}");
        Check(slot("blur1") == slot("blur2") && slot("blur1") >= 0, $"blur1/blur2 同一 slot={slot("blur1")}");
        Check(aliased("tmp1") && aliased("tmp2") && aliased("blur1") && aliased("blur2"), "tmp/blur 全部 aliased=true");

        // External 種別の確認
        Check(resources.EnumerateArray().Any(r => r.GetProperty("name").GetString() == "ui" && r.GetProperty("kind").GetString() == "External"), "ui=External");
        Check(resources.EnumerateArray().Any(r => r.GetProperty("name").GetString() == "tmp1" && r.GetProperty("kind").GetString() == "Transient"), "tmp1=Transient");

        Console.WriteLine($"  -> {ok}/{ok + fail} 件 OK");
        return fail == 0 ? 0 : 1;
    }
}
