using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Luxel.DevTools;

/// <summary>
/// デバッグ HTTP サーバ (loopback)。<see cref="DevToolsListener"/> のデータをプル配信する:
///   GET /          埋め込み index.html
///   GET /poll      軽量 JSON (frameRev/treeRev/stat/logs) ← Web はこれを自分の cadence で叩く
///   GET /frame?rev=N   最新フレーム binary (8B ヘッダ + RGBA)。rev 不変なら 304
///   GET /tree?rev=N    最新ツリー JSON。rev 不変なら 304
///   POST /cmd      操作 (単体 {op,..} or 配列) → EngineCommands へ Enqueue
/// リソースの整理 (ウィンドウと UI は 1:1 ではないため別リソース):
///   ウィンドウ: GET /windows (一覧+載っている UI 名) / GET /winframe?id=&rev= (提示ピクセル)
///   フレーム系 (/frame /winframe /uiframe) は <c>&format=png</c> で PNG を返す (AI/ブラウザ確認用)
///              — <see cref="IWindowRemoteHost"/> (省略可) からのプル。操作は POST /cmd の window.*
///   UI:        GET /trees (全 UiHost の tree bundle) / GET /uiframe?i= (オフスクリーン UI 画像)
///              — 操作は POST /cmd の入力 op + ui.set ("ui" index/名前でルーティング)
/// 高頻度データはプッシュせず最新のみ保持→rev 差分でプルさせ負荷を下げる。
/// </summary>
public sealed class DebugServer : IDisposable
{
    private readonly DevToolsListener _listener;
    private readonly Luxel.Diagnostics.IWindowRemoteHost? _windows;
    private readonly HttpListener _http = new();
    private CancellationTokenSource? _cts;
    private static readonly string IndexHtml = LoadIndexHtml();

    public int Port { get; }
    public string Url => $"http://127.0.0.1:{Port}/";

    public DebugServer(DevToolsListener listener, int port = 0, Luxel.Diagnostics.IWindowRemoteHost? windows = null)
    {
        _listener = listener;
        _windows = windows;
        Port = port == 0 ? FreePort() : port;
        _http.Prefixes.Add(Url);
    }

    public void Start()
    {
        _http.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_cts.Token);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); }
            catch { break; }   // 停止時
            _ = Task.Run(() => { try { Handle(ctx); } catch { /* 1 リクエストの失敗は無視 */ } });
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        string path = req.Url?.AbsolutePath ?? "/";
        switch (req.HttpMethod, path)
        {
            case ("GET", "/"): WriteText(ctx, IndexHtml, "text/html; charset=utf-8"); break;
            case ("GET", "/poll"): WriteText(ctx, _listener.BuildPoll(QueryLong(req, "logSince") ?? 0), "application/json"); break;
            case ("GET", "/frame"): WriteFrame(ctx, QueryLong(req, "rev"), IsPng(req)); break;
            case ("GET", "/tree"): WriteTree(ctx, QueryLong(req, "rev")); break;
            case ("GET", "/primitives"): WriteText(ctx, _listener.GetPrimitives() ?? "{}", "application/json"); break;
            case ("GET", "/gpu"): WriteText(ctx, _listener.GetGpu() ?? "{}", "application/json"); break;
            case ("GET", "/resources"): WriteText(ctx, _listener.GetResources() ?? "{}", "application/json"); break;
            case ("GET", "/rendergraph"): WriteText(ctx, _listener.GetRenderGraph() ?? "{}", "application/json"); break;
            case ("GET", "/perf"): WriteText(ctx, _listener.GetPerf() ?? "{}", "application/json"); break;
            case ("GET", "/ecs"): WriteText(ctx, _listener.GetEcs() ?? "{}", "application/json"); break;
            case ("GET", "/ecssummary"): WriteText(ctx, _listener.GetEcsSummary() ?? "{}", "application/json"); break;
            case ("GET", "/custom"): WriteText(ctx, _listener.GetCustom() ?? "{}", "application/json"); break;
            case ("GET", "/surfaces"): WriteText(ctx, _listener.GetSurfaces() ?? "{}", "application/json"); break;
            case ("GET", "/input"): WriteText(ctx, _listener.GetInputState() ?? "{}", "application/json"); break;
            case ("GET", "/audio"): WriteText(ctx, _listener.GetAudio() ?? "{}", "application/json"); break;
            case ("GET", "/runtime"): WriteText(ctx, _listener.GetRuntime() ?? "{}", "application/json"); break;
            case ("GET", "/trees"): WriteText(ctx, _listener.GetTrees() ?? "{}", "application/json"); break;
            case ("GET", "/uiframe"): WriteUiFrame(ctx, (int)(QueryLong(req, "i") ?? 0), IsPng(req)); break;
            case ("GET", "/windows"): WriteText(ctx, _windows?.ListWindowsJson() ?? "[]", "application/json"); break;
            case ("GET", "/winframe"): WriteWinFrame(ctx, (int)(QueryLong(req, "id") ?? 0), QueryLong(req, "rev"), IsPng(req)); break;
            case ("POST", "/cmd"): HandleCmd(ctx); break;
            default: ctx.Response.StatusCode = 404; ctx.Response.Close(); break;
        }
    }

    private static bool IsPng(HttpListenerRequest req) => req.QueryString["format"] == "png";

    /// <summary>フレーム body (8B ヘッダ w,h LE + RGBA) を octet-stream か PNG で書き出す。</summary>
    private static void WriteFrameBody(HttpListenerContext ctx, byte[] body, bool png)
    {
        if (png)
        {
            int w = BitConverter.ToInt32(body, 0), h = BitConverter.ToInt32(body, 4);
            byte[] data = Png.Encode(w, h, body.AsSpan(8));
            ctx.Response.ContentType = "image/png";
            ctx.Response.OutputStream.Write(data, 0, data.Length);
        }
        else
        {
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.OutputStream.Write(body, 0, body.Length);
        }
        ctx.Response.Close();
    }

    private void WriteUiFrame(HttpListenerContext ctx, int index, bool png)
    {
        byte[]? body = _listener.GetUiFrame(index);
        if (body is null) { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }
        WriteFrameBody(ctx, body, png);
    }

    /// <summary>ウィンドウ id の提示ピクセル (8B ヘッダ + tight RGBA、format=png で PNG)。rev 不変は 304、未知 id は 404。</summary>
    private void WriteWinFrame(HttpListenerContext ctx, int id, long? sinceRev, bool png)
    {
        if (_windows is null) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
        (byte[]? body, long rev) = _windows.GetFrame(id, sinceRev);
        ctx.Response.Headers["X-Rev"] = rev.ToString();
        if (rev < 0) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
        if (body == null) { ctx.Response.StatusCode = sinceRev.HasValue ? 304 : 204; ctx.Response.Close(); return; }
        WriteFrameBody(ctx, body, png);
    }

    private void WriteFrame(HttpListenerContext ctx, long? sinceRev, bool png)
    {
        (byte[]? body, long rev) = _listener.GetFrame(sinceRev);
        ctx.Response.Headers["X-Rev"] = rev.ToString();
        if (body == null) { ctx.Response.StatusCode = sinceRev.HasValue ? 304 : 204; ctx.Response.Close(); return; }
        WriteFrameBody(ctx, body, png);
    }

    private void WriteTree(HttpListenerContext ctx, long? sinceRev)
    {
        (string? json, long rev) = _listener.GetTree(sinceRev);
        ctx.Response.Headers["X-Rev"] = rev.ToString();
        if (json == null) { ctx.Response.StatusCode = sinceRev.HasValue ? 304 : 204; ctx.Response.Close(); return; }
        WriteText(ctx, json, "application/json");
    }

    private void HandleCmd(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        string body = reader.ReadToEnd();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (JsonElement el in doc.RootElement.EnumerateArray()) Dispatch(el);
            else Dispatch(doc.RootElement);
        }
        catch { /* 不正 JSON は無視 */ }
        ctx.Response.StatusCode = 204;
        ctx.Response.Close();
    }

    private void Dispatch(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String)
            _listener.EnqueueCommand(op.GetString()!, el.Clone());   // doc 破棄後も使うため Clone
    }

    private static void WriteText(HttpListenerContext ctx, string text, string contentType)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.ContentType = contentType;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    private static long? QueryLong(HttpListenerRequest req, string key)
        => long.TryParse(req.QueryString[key], out long v) ? v : null;

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static string LoadIndexHtml()
    {
        Assembly asm = typeof(DebugServer).Assembly;
        string? name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        if (name == null) return "<!doctype html><meta charset=utf-8><h1>index.html missing</h1>";
        using Stream s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _http.Stop(); _http.Close(); } catch { }
    }
}
