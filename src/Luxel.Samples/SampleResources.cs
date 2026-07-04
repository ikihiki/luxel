using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Luxel;
using Luxel.Resources;
using Luxel.AssetsGpu;

namespace Luxel.Samples;

/// <summary>
/// サンプル21: リソース管理システム (Luxel.Resources) の GPU 統合検証。
/// (型,uri) オートコンポーズ (uri→byte[]→CpuImage→GpuTexture, CPU デコード段を自動挿入)、
/// file/http ソース、並列ロード (bindless index 一意)、バンドル (cross-uri)、自動リロード、device-lost を確認。
/// </summary>
public static class SampleResources
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint TextureIndex; public uint SamplerIndex; }

    // バンドル例 (プレハブ相当): 2 テクスチャをまとめる
    public sealed class TexPair(GpuTexture a, GpuTexture b) { public GpuTexture A => a; public GpuTexture B => b; }
    private sealed class TexPairStep : IResourceStep<byte[], TexPair>
    {
        public Executor Executor => Executor.Cpu;
        public IEnumerable<string> Extensions => [".pair"];
        public async Task<TexPair> RunAsync(byte[] b, ResourceUri u, LoadContext ctx)
        {
            string[] s = Encoding.UTF8.GetString(b).Split(';');
            var a = ctx.Load<GpuTexture>(s[0]);
            var bb = ctx.Load<GpuTexture>(s[1]);
            await Task.WhenAll(a.Ready, bb.Ready);
            return new TexPair(a.Value, bb.Value);
        }
    }

    // 4 色 (赤緑青白) の 2x2 .tex を生成
    private static byte[] FourColorTex(byte br = 255, byte bg = 255, byte bb = 255) =>
        ImageCodec.EncodeTex(2, 2,
        [
            255, 0, 0, 255,   0, 255, 0, 255,
            0, 0, 255, 255,   br, bg, bb, 255,
        ]);

    public static int Run(Func<GpuDevice> createDevice)
    {
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");
        string root = AppContext.BaseDirectory;

        using var sys = new ResourceSystem(
            sources: ResourceSystemDefaults.BuiltinSources(assetRoot: root),
            steps:   [..ResourceSystemDefaults.BuiltinSteps(), new TexPairStep()]);
        sys.InstallAssetGpu(device);           // GPU 系 Step 一式を追加登録
        sys.Watch();

        int ok = 0, fail = 0;
        void Check(bool c, string m) { if (c) { ok++; Console.WriteLine($"  OK  {m}"); } else { fail++; Console.WriteLine($"  NG  {m}"); } }

        // 描画ヘルパ (textured quad で 4 象限の色を読む)
        using GpuSampler sampler = device.CreateSampler(GpuSamplerFilter.Point);
        using GpuTexture target = device.CreateRenderTarget(64, 64, GpuFormat.Rgba8Unorm);
        using GpuBuffer readback = device.Malloc(64 * 64 * 4, GpuMemoryKind.HostMapped);
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("textured"), GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        (byte r, byte g, byte b)[] Quads(GpuTexture tex)
        {
            var a = new DrawArgs { TextureIndex = tex.BindlessIndex, SamplerIndex = sampler.BindlessIndex };
            using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(target, null, 0, 0, 0, 1).SetGraphicsPipeline(pipeline).SetRootArguments(a)
               .Draw(3).EndRendering().Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(target, readback);
            cmd.Finish(); device.MainQueue.SubmitAndWait(cmd);
            Span<byte> px = readback.Span<byte>(64 * 64 * 4);
            (int x, int y)[] q = [(1, 1), (3, 1), (1, 3), (3, 3)];
            var res = new (byte, byte, byte)[4];
            for (int k = 0; k < 4; k++) { int i = ((q[k].y * 64 / 4) * 64 + (q[k].x * 64 / 4)) * 4; res[k] = (px[i], px[i + 1], px[i + 2]); }
            return res;
        }
        // 4 象限が非黒かつ全て異なる (= 2x2 テクスチャが正しくサンプリングされた。チャネル順非依存)
        bool Distinct4(GpuTexture t) { var q = Quads(t); return q.All(c => c.Item1 != 0 || c.Item2 != 0 || c.Item3 != 0) && q.Distinct().Count() == 4; }

        // (1) file からロード → CPU デコード段自動挿入 → 描画
        File.WriteAllBytes(Path.Combine(root, "quad.tex"), FourColorTex());
        var h = sys.Load<GpuTexture>("quad.tex");
        h.Ready.GetAwaiter().GetResult();
        Quads(h.Value);   // ウォームアップ (レンダーターゲット初期化)
        Check(h.IsReady && h.Value != null, "file: Load<GpuTexture> 完了 (uri→byte[]→CpuImage→GpuTexture 自動合成)");
        Check(Distinct4(h.Value), "  描画成功 (4 象限が 4 色で異なる)");

        // 中間 CPU 表現を共有取得 (再デコードなし、GPU 段スキップ)
        var img = sys.Load<CpuImage>("quad.tex");
        img.Ready.GetAwaiter().GetResult();
        Check(img.Value is { Width: 2, Height: 2 }, "中間 CpuImage を共有取得 (GPU 段スキップ)");

        // (2) 並列ロード (8 枚) → bindless index 一意
        for (int i = 0; i < 8; i++) File.WriteAllBytes(Path.Combine(root, $"p{i}.tex"), FourColorTex());
        var many = Enumerable.Range(0, 8).Select(i => sys.Load<GpuTexture>($"p{i}.tex")).ToArray();
        Task.WhenAll(many.Select(x => x.Ready)).GetAwaiter().GetResult();
        int distinct = many.Select(x => x.Value.BindlessIndex).Distinct().Count();
        Check(many.All(x => x.IsReady) && distinct == 8, $"並列 8 ロード, bindless index 一意 ({distinct}/8)");

        // (3) http ソースからロード
        using (var http = new TexHttpServer(FourColorTex()))
        {
            var hh = sys.Load<GpuTexture>($"{http.Url}q.tex");
            hh.Ready.GetAwaiter().GetResult();
            Check(hh.IsReady && Distinct4(hh.Value), $"http: Load<GpuTexture>({http.Url}q.tex) → 描画成功");
        }

        // (4) バンドル (cross-uri): pair → 2 テクスチャ
        File.WriteAllBytes(Path.Combine(root, "pair.pair"), Encoding.UTF8.GetBytes("p0.tex;p1.tex"));
        var pair = sys.Load<TexPair>("pair.pair");
        pair.Ready.GetAwaiter().GetResult();
        Check(pair.IsReady && pair.Value.A != null && pair.Value.B != null && Distinct4(pair.Value.A), "バンドル: TexPair が 2 テクスチャを cross-uri 合成");

        // (5) 自動リロード (ファイル変更 → 描画内容が変わる)
        var before = Quads(h.Value);
        File.WriteAllBytes(Path.Combine(root, "quad.tex"), FourColorTex(255, 255, 0));   // 右下を黄に
        bool reloaded = PumpUntil(sys, () => h.Version >= 1 && !Quads(h.Value).SequenceEqual(before), 5000);
        Check(reloaded, $"自動リロード: quad.tex 変更で描画が更新 (v{h.Version})");

        // (6) device-lost → 全 GPU 資源再生成 (bindless index が変わる)
        uint beforeIdx = h.Value.BindlessIndex;
        sys.NotifyDeviceLost();
        bool rebuilt = PumpUntil(sys, () => h.Value.BindlessIndex != beforeIdx, 5000);
        Check(rebuilt && Distinct4(h.Value), "device-lost: 再生成後も描画 OK (新 bindless index)");

        Console.WriteLine($"Resources 統合テスト: {ok} OK / {fail} NG");
        Console.WriteLine(fail == 0 ? "OK: (型,uri) オートコンポーズ + 並列 + http + バンドル + 自動リロード + device-lost を確認" : "FAILED");
        return fail == 0 ? 0 : 1;
    }

    private static bool PumpUntil(ResourceSystem sys, Func<bool> cond, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { sys.Pump(); if (cond()) return true; Thread.Sleep(8); }
        sys.Pump();
        return cond();
    }

    /// <summary>テスト用のローカル http サーバ (.tex バイトを返す)。</summary>
    private sealed class TexHttpServer : IDisposable
    {
        private readonly HttpListener _http = new();
        public string Url { get; }
        public TexHttpServer(byte[] payload)
        {
            int port = FreePort();
            Url = $"http://127.0.0.1:{port}/";
            _http.Prefixes.Add(Url);
            _http.Start();
            _ = Task.Run(async () =>
            {
                while (_http.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _http.GetContextAsync(); } catch { break; }
                    ctx.Response.OutputStream.Write(payload, 0, payload.Length);
                    ctx.Response.Close();
                }
            });
        }
        private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
        public void Dispose() { try { _http.Stop(); _http.Close(); } catch { } }
    }
}
