using System.Text.Json;
using Luxel.Controls;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Graphics.TwoD;
using Luxel.UI;

namespace Luxel.Tests;

[CollectionDefinition("GlobalDiagnostics", DisableParallelization = true)]
public sealed class GlobalDiagnosticsCollection;

/// <summary>
/// DevTools 計装/リスナーの GPU 不要テスト。エンジンの Emit/操作レジストリ・最新のみ保持(coalesce)・
/// ログリング・ツリー introspection・キーマップを検証する (デバイス生成なし)。
/// </summary>
[Collection("GlobalDiagnostics")]
public class DevToolsTests
{
    // ---- EngineCommands: 登録→Enqueue→Drain (app スレッド適用) ----
    [Fact]
    public void Commands_Register_Enqueue_Drain()
    {
        var cmds = new EngineCommands();
        int sum = 0;
        cmds.Register("add", a => sum += (int)a!);
        cmds.Enqueue("add", 3);
        cmds.Enqueue("add", 4);
        cmds.Enqueue("nope", 1);          // 未知操作は無視
        Assert.Equal(0, sum);              // Drain まで適用されない
        int n = cmds.Drain();
        Assert.Equal(3, n);                // 未知含め 3 件処理 (no-op 含む)
        Assert.Equal(7, sum);
        Assert.Null(cmds.Invoke("nope", null));
    }

    // ---- LatestSlot: 100 回上書きしても最新のみ + rev=回数 ----
    [Fact]
    public void LatestSlot_Coalesces_To_Newest()
    {
        var slot = new LatestSlot<string>();
        for (int i = 0; i < 100; i++) slot.Publish($"v{i}");
        (string? value, long rev) = slot.Read();
        Assert.Equal("v99", value);
        Assert.Equal(100, rev);
    }

    // ---- LogRing: 容量超過でラップ、since カーソルで欠落なく取得 ----
    [Fact]
    public void LogRing_Wraps_And_Pages_By_Cursor()
    {
        var ring = new LogRing(4);
        for (int i = 0; i < 6; i++) ring.Add("input", $"e{i}");
        (LogEntry[] items, long cursor) = ring.Since(0);
        Assert.Equal(4, items.Length);                 // 直近 4 件のみ保持
        Assert.Equal("e2", items[0].Text);
        Assert.Equal("e5", items[^1].Text);
        Assert.Equal(6, cursor);
        Assert.Empty(ring.Since(cursor).items);         // 以降は無し
    }

    // ---- キーマップ (ブラウザ key 名 → Key) ----
    [Theory]
    [InlineData("Tab", Key.Tab)]
    [InlineData("Enter", Key.Enter)]
    [InlineData(" ", Key.Space)]
    [InlineData("Escape", Key.Escape)]
    [InlineData("ArrowLeft", Key.Left)]
    [InlineData("ArrowRight", Key.Right)]
    [InlineData("Backspace", Key.Backspace)]
    [InlineData("PageUp", Key.PageUp)]
    [InlineData("q", Key.None)]            // 印字文字は char 経路 → None
    public void ParseKey_Maps_Browser_Names(string name, Key expected)
        => Assert.Equal(expected, UiHostCommands.ParseKey(name));

    // ---- Widget ツリー introspection (GPU 不要) ----
    [Fact]
    public void DebugChildren_And_Detail()
    {
        // 子 Widget は indexer 経由で渡す
        var sp = Kit.Stack(true)[Kit.Text("a"), Kit.Text("b"), Kit.Text("c")];
        Widget[] kids = sp.DebugChildren().ToArray();
        Assert.Equal(3, kids.Length);
        Assert.Equal("a", kids[0].DebugDetail);
        Assert.Equal("Text", kids[0].DebugType);

        var border = Kit.Border()[Kit.Button(_ => { }, "ok")];
        Assert.Single(border.DebugChildren());
        Assert.Equal("ok", border.DebugChildren().First().DebugDetail);
        Assert.Empty(Kit.Border().DebugChildren());   // 子なし
    }

    // ---- エンジン Emit → リスナー (購読/coalesce/dedup/ログ) を通しで ----
    [Fact]
    public void Listener_Receives_And_Coalesces_Diagnostics()
    {
        var cmds = new EngineCommands();
        using var listener = new DevToolsListener(cmds);

        // 入力イベント → ログ
        EngineDiagnostics.Emit(EngineDiagnostics.Input, new DiagInput("click", "1,2"));
        EngineDiagnostics.Emit(EngineDiagnostics.Input, new DiagInput("char", "あ"));

        // 統計 → 最新のみ
        EngineDiagnostics.Emit(EngineDiagnostics.RenderFlush, new DiagFlush(5, 2, 320, true, 480, 320));

        // フレーム → 最新のみ (8B ヘッダ + RGBA)
        EngineDiagnostics.Emit(EngineDiagnostics.Frame, new DiagFrame(2, 1, new byte[2 * 1 * 4]));

        using (JsonDocument poll = JsonDocument.Parse(listener.BuildPoll(0)))
        {
            Assert.True(poll.RootElement.GetProperty("frameRev").GetInt64() > 0);
            Assert.Equal(480, poll.RootElement.GetProperty("stat").GetProperty("w").GetInt32());
            int logs = poll.RootElement.GetProperty("logs").GetArrayLength();
            Assert.True(logs >= 2);
        }

        (byte[]? body, long frev) = listener.GetFrame(null);
        Assert.NotNull(body);
        Assert.Equal(8 + 2 * 1 * 4, body!.Length);
        Assert.Equal(2, BitConverter.ToInt32(body, 0));   // width LE
        Assert.Equal(1, BitConverter.ToInt32(body, 4));   // height LE
        Assert.Null(listener.GetFrame(frev).body);         // 同 rev → 304 相当

        // ツリー: dedup (同一内容は rev を進めない)
        var tree = new DebugNode("Root", 0, 0, 10, 10, 0, null, []);
        EngineDiagnostics.Emit(EngineDiagnostics.Tree, tree);
        (string? json1, long trev1) = listener.GetTree(null);
        Assert.NotNull(json1);
        EngineDiagnostics.Emit(EngineDiagnostics.Tree, tree with { });   // 同一内容
        Assert.Equal(trev1, listener.GetTree(null).rev);                  // rev 不変
    }

    // ---- UiHost.DispatchUiSet: DevTools からの ui.set → Widget.SetDebugProp round-trip ----
    private sealed class ProbeWidget : Widget
    {
        public int Value;
        public bool Flag;
        public string Text = "";
        public readonly List<ProbeWidget> Children = new();
        public override IEnumerable<Widget> DebugChildren() => Children;
        public override IEnumerable<DebugProp> DebugProps() =>
        [
            new("count", "int", Value.ToString()),
            new("flag", "bool", Flag ? "true" : "false"),
            new("label", "string", Text),
        ];
        public override void SetDebugProp(string name, string type, JsonElement value)
        {
            switch (name)
            {
                case "count": Value = value.GetInt32(); break;
                case "flag": Flag = value.ValueKind == JsonValueKind.True; break;
                case "label": Text = value.GetString() ?? ""; break;
            }
        }
        protected override void PerformLayout(Constraints c, LayoutContext ctx) { }
        protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin) { }
    }

    [Fact]
    public void UiHost_DispatchUiSet_WritesBackToWidget()
    {
        var root = new ProbeWidget();
        var child = new ProbeWidget();
        var grand = new ProbeWidget();
        root.Children.Add(child);
        child.Children.Add(grand);

        // ルート
        using (var doc = JsonDocument.Parse("""{"path":"0","name":"count","propType":"int","value":42}"""))
            Assert.True(UiHost.DispatchUiSet(root, doc.RootElement));
        Assert.Equal(42, root.Value);

        // 深いパス (0.0.0 = grand)
        using (var doc = JsonDocument.Parse("""{"path":"0.0.0","name":"flag","propType":"bool","value":true}"""))
            Assert.True(UiHost.DispatchUiSet(root, doc.RootElement));
        Assert.True(grand.Flag);
        Assert.False(child.Flag);

        // string
        using (var doc = JsonDocument.Parse("""{"path":"0.0","name":"label","propType":"string","value":"hi"}"""))
            Assert.True(UiHost.DispatchUiSet(root, doc.RootElement));
        Assert.Equal("hi", child.Text);

        // 存在しないパス → false、副作用なし
        using (var doc = JsonDocument.Parse("""{"path":"0.9","name":"count","propType":"int","value":99}"""))
            Assert.False(UiHost.DispatchUiSet(root, doc.RootElement));
        Assert.Equal(42, root.Value);   // 変わっていない
    }

    [Fact]
    public void Widget_DebugProps_DefaultsEmpty_AndSetDebugPropIsNoOp()
    {
        var w = new ProbeWidget();
        Assert.Contains(w.DebugProps(), p => p.Name == "count");
        // 未知 name は無視 (base の contract)
        using var doc = JsonDocument.Parse("\"anything\"");
        w.SetDebugProp("unknown", "string", doc.RootElement);
        Assert.Equal(0, w.Value);   // 変化なし
    }
}
