using Luxel.Audio;
using Luxel.Audio.Sequencing;
using Luxel.Controls;
using Luxel.Document;
using Luxel.Platform;
using Luxel.Strudel;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// Strudel REPL — パターン言語 (Luxel.Strudel) + 汎用シーケンシング層 (Luxel.Audio.Sequencing) の実演。
/// 文書はパターン行主体の独自フォーマット: `--` 行 = コメント、それ以外の行で Enter → ライブブロック化。
/// 各ブロックが独立スロット (d1/d2… 相当) を持ち、Run = 評価 + ホットスワップ (クロックは止まらない)。
/// 音は初回 Run で XAudio2 を遅延初期化 (失敗時 NullBackend) — 初期表示は無音で snap 決定的。
/// </summary>
public static class StrudelStory
{
    /// <summary>E2E/headless では実 XAudio2 を避けて <see cref="NullAudioBackend"/> を使う
    /// (golden は音を snap しない + Vortice のエンジンコールバック GC レースを踏まない)。E2e ランナーが設定。</summary>
    internal static bool HeadlessAudio { get; set; }

    // ---- セッション (プロセスで 1 個 — XAudio2 マスタリングボイスと同じ寿命) ----
    private static class Session
    {
        public static StrudelScheduler Sched = new(chunkSeconds: 0.1);
        private static StreamMixerSink? _mixer;
        private static readonly Dictionary<int, object> _owners = new();   // slot → 現在のブロック
        private static int _nextSlot;
        private static readonly float[] _peaks = new float[96];

        public static bool AudioReady => _mixer is not null;

        public static int ClaimSlot(int? requested, object owner)
        {
            int slot = requested ?? ++_nextSlot;
            _nextSlot = Math.Max(_nextSlot, slot);
            _owners[slot] = owner;   // 再構築された新ブロックが所有権を引き継ぐ
            return slot;
        }

        /// <summary>所有者のときだけ停止 (Run のコミット → 再構築で旧ブロックの Dispose が
        /// 新ブロックのスロットを殺さないためのガード)。</summary>
        public static void Release(int slot, object owner)
        {
            if (_owners.TryGetValue(slot, out object? cur) && ReferenceEquals(cur, owner))
            {
                _owners.Remove(slot);
                Sched.SetPattern(slot, null);
            }
        }

        /// <summary>評価してスロットへ反映する。戻り値はエラーメッセージ (null = 成功)。</summary>
        public static string? Eval(int slot, string code)
        {
            try
            {
                EvalResult r = StrudelEval.Evaluate(code);
                if (r.Cps is double c) Sched.Cps = c;
                if (r.Pattern is not null)
                {
                    EnsureAudio();
                    Sched.SetPattern(slot, r.Pattern);
                }
                return null;
            }
            catch (StrudelEvalError e) { return e.Message; }
        }

        public static void Stop(int slot) => Sched.SetPattern(slot, null);
        public static void Hush() => Sched.Hush();

        private static void EnsureAudio()
        {
            if (_mixer is not null) return;
            IAudioBackend backend;
            if (HeadlessAudio) backend = new NullAudioBackend();   // E2E: 実 XAudio2 を触らない (決定的 + Vortice callback GC レースを避ける)
            else
                try { var x = new XAudio2Backend(); x.Initialize(); backend = x; }
                catch { backend = new NullAudioBackend(); }        // 実デバイス無し (CI 等) も無音で動く
            _mixer = new StreamMixerSink(StrudelKit.CreateBank(), backend);
            Sched.AddSink(_mixer);
        }

        /// <summary>毎 Tick: ミキサのキューを 3 チャンク (300ms) 先まで満たす。</summary>
        public static void Pump()
        {
            if (_mixer is null) return;
            int guard = 0;
            while (_mixer.BuffersQueued < 3 && guard++ < 8) Sched.RenderWindow();
        }

        public static ReadOnlySpan<float> Peaks()
        {
            if (_mixer is null) return _peaks;   // 無音 (ゼロ) — snap 決定的
            _mixer.CopyPeaks(_peaks);
            return _peaks;
        }

        public static void ResetForE2e()
        {
            _mixer?.Dispose();
            _mixer = null;
            _owners.Clear();
            _nextSlot = 0;
            Array.Clear(_peaks);
            Sched = new StrudelScheduler(chunkSeconds: 0.1);
        }
    }

    internal static void ResetForE2e() => Session.ResetForE2e();

    // ---- ライブブロック: TextEditorView (新スタック。診断波線 + 補完 + 再生囲み + Ctrl+Enter 評価) + Run/Stop ----
    private sealed class StrudelBlock : CompositeControl, IDisposable
    {
        private readonly Signal<string> _code;
        private readonly Signal<string> _status = new("");
        private readonly TextEditorView _editor;
        private readonly DiagnosticsProvider _diag;
        private readonly Button _run, _stop;
        private readonly int _slot;
        private readonly float _maxW;
        private string _lastSpanKey = "";

        /// <summary>コードエディタ (play からフォーカス/型付け/Ctrl+Enter するために公開)。</summary>
        internal TextEditorView Editor => _editor;
        /// <summary>直近評価が成功したか (play の Expect 用)。</summary>
        internal bool LastRunOk { get; private set; }
        /// <summary>診断数 (波線、play の Expect 用)。</summary>
        internal int DiagnosticCount => _diag.Count;

        public StrudelBlock(string body, float maxWidth)
        {
            _maxW = MathF.Max(160, maxWidth);
            _code = new Signal<string>(body);
            // block は文書内 ```strudel フェンス 1 つ = 独立スロット (本文キャッシュで 1 インスタンス、
            // 再描画で消えない)。Run はブロック自身の _code を再評価するだけ (再構築なし = 音が途切れない)。
            _slot = Session.ClaimSlot(null, this);
            _editor = TextEditorView(_code, editorHeight: 62f, editorWidth: _maxW - 130);
            (_, _, _, _editor.EditorFont) = StoryKit.EditorFaces.Value;
            _editor.LanguageService = StrudelCodeLanguage.Instance;                      // 補完
            _diag = new DiagnosticsProvider(StrudelCodeLanguage.Instance, () => UiTheme.T);
            _editor.Providers.Add(_diag);                                                // 診断波線
            _editor.OnKeyIntercept = ev =>                                              // Ctrl+Enter = 現ブロックを評価
            {
                if (ev.Key == Key.Enter && ev.Ctrl) { Run(); return true; }
                return false;
            };
            _run = Button(_ => Run(), "Run");
            _stop = Button(_ => { Session.Stop(_slot); _status.Value = "stopped"; }, "Stop", variant: Variant.Ghost);
        }

        protected override Widget Build()
        {
            Func<string> status = () => _status.Value;
            return HStack(spacing: 6)[
                _editor,
                VStack(spacing: 4)[
                    HStack(spacing: 4)[_run, _stop],
                    Text(status, 11f, color: Bind.From(() => UiTheme.T.TextMuted))]];
        }

        // 毎フレーム: スロットの再生トークンを Mark.Box で囲む (変化時のみ Refresh — 大半のフレームは据え置き)
        protected override void OnRealize(UiBuildContext ctx)
        {
            ctx.AddAnimation(_ =>
            {
                IReadOnlyList<SourceSpan> spans = Session.Sched.ActiveSpans(_slot);
                string key = string.Join(",", spans.Select(sp => $"{sp.Start}:{sp.Length}"));
                if (key != _lastSpanKey)
                {
                    _lastSpanKey = key;
                    uint c = UiTheme.T.Primary;
                    var boxes = spans.Select(sp => (Decoration)new MarkDecoration(sp.Start, sp.End, Box: new BoxStyle(c))).ToList();
                    _editor.SetDecorations("playing", new DecorationSet(boxes));
                }
                return false;
            });
        }

        private void Run()
        {
            string? err = Session.Eval(_slot, _code.Value);
            LastRunOk = err is null;
            _status.Value = err ?? $"d{_slot} ♪";
        }

        public override string? DebugDetail => $"strudel d{_slot}";

        public void Dispose() => Session.Release(_slot, this);
    }

    [Story("Demos/Strudel/Repl", Height = 560, Order = 2031)]
    public static Widget Repl(StoryContext ctx)
    {
        // 各セルは ```strudel フェンス — 本文キーでキャッシュし同一 StrudelBlock を返す (再描画で状態が消えない)
        var blocks = new List<StrudelBlock>();   // play が Editor/Ctrl+Enter/診断を叩くための参照
        var cellByBody = new Dictionary<string, StrudelBlock>();
        StrudelBlock CellFor(string body)
        {
            if (!cellByBody.TryGetValue(body, out StrudelBlock? cell))
            {
                cell = new StrudelBlock(body, 560f);
                cellByBody[body] = cell;
                blocks.Add(cell);
            }
            return cell;
        }

        Signal<string> src = ctx.Signal("source",
            "Strudel REPL: 各 ```strudel セルを **Ctrl+Enter** または Run で評価 (再 Run = ホットスワップ)。\n\n" +
            "```strudel\ns(\"bd*2 [~ sd] hh*4\").gain(0.9)\n```\n\n" +
            "別セルは独立スロット — 重ねて鳴る。silence を Run するとそのスロットだけ止まる。\n\n" +
            "```strudel\nnote(\"c3 eb3 g3 <bb3 c4>\").s(\"saw\").slow(2)\n```\n\n" +
            "例: `.every(2, rev)` / `.jux(fast(2))` / `.degrade()` / `cps(0.6)` でテンポ。\n");

        Signal<float> cps = ctx.Signal("cps", 0.5f);
        var wave = Sparkline(300f, 30f, bars: true);

        // 新スタック: markdown 文書レンダラ + ```strudel フェンスを embed 扱いし StrudelBlock に解決
        TextEditorView ed = MarkdownDoc.Create(src, () => UiTheme.T, width: 560f, height: 430f,
            mono: StoryKit.EditorFaces.Value.Mono, embedKinds: new[] { "strudel" }, fonts: StoryKit.JpFallback.Value);
        ed.WidgetResolver = key => key is EmbedRef { Key: "strudel" } r ? CellFor(r.Body) : null;

        var root = new ReplRoot(ed, wave, cps);
        Func<string> cpsLabel = () => $"cps {cps.Value:0.00}";
        ctx.Play(async d =>
        {
            await d.Snap();                                           // 初期 (strudel セル × 2、無音)
            await d.Expect(() => blocks.Count >= 2, "```strudel フェンスがライブブロック化");

            // 診断波線: 2 つ目のブロック末尾に未知メソッドを足す (型付け → Sync → 再診断 → 波線)
            await d.Click(blocks[1].Editor);
            await d.Key(Key.End);
            await d.Type(".nope()");
            await d.Step(1);
            await d.Expect(() => blocks[1].DiagnosticCount > 0, "不正記法で診断波線が出る");
            await d.Snap("diag");

            // Ctrl+Enter: 1 つ目のブロック (正しい記法) を評価 → スロットに反映 (コミットで再構築)
            await d.Click(blocks[0].Editor);
            await d.Key(Key.End);
            await d.Key(Key.Enter, ctrl: true);
            await d.Step(2);
            await d.Expect(() => blocks[0].LastRunOk, "Ctrl+Enter で評価成功");
            // 再生囲み (Mark.Box) は実配線済み — ただし Session は process-wide static でサイクル位置が
            // 走行順に依存するため snap しない (決定的な囲みの絵は Controls/TextEditorView/Strudel に)
        });
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(spacing: 8)[
                HStack(spacing: 10)[
                    Slider(cps, min: 0.2f, max: 1.5f, width: 140f),
                    Text(cpsLabel, color: Bind.From(() => UiTheme.T.Text)),
                    Button(_ => { Session.Hush(); ctx.Log("hush"); }, "hush", variant: Variant.Ghost),
                    wave],
                root]];
    }

    /// <summary>ルート: エディタを包み、Tick でセッションを駆動する (ポンプ + 波形 + cps 同期)。</summary>
    private sealed class ReplRoot(Widget editor, Sparkline wave, Signal<float> cps) : CompositeControl
    {
        protected override Widget Build() => editor;

        protected override void OnRealize(UiBuildContext ctx)
        {
            ctx.AddAnimation(_ =>
            {
                Session.Sched.Cps = cps.Value;
                Session.Pump();
                if (Session.AudioReady) wave.SetValues(Session.Peaks(), min: 0f, max: 1f);
                return false;
            });
        }
    }
}
