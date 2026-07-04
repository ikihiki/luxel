using Luxel.Controls;
using Luxel.Document;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// EX-M5: strudel REPL 型ライブコードブロックの実証。
/// **markdown でない独自フォーマット** (LiveScriptFormat) が文書をすべて管理する:
///   `--` 始まりの行 = コメント段落 / それ以外の行 = ライブブロック (パターン)。
///   段落にパターンを打って **Enter するとその行がライブブロック化** (TryBlockCommit) —
///   ブロックは「コードエディタ + Run/Stop + 波形出力」の複合 widget として動く。
/// 基盤 3 保証の実証: コード往復 (Run = Commit → undo 可) / Tick 駆動 (波形が流れる) / 破棄時 Dispose。
/// </summary>
public static class LiveCodeStory
{
    /// <summary>非 markdown の文書フォーマット — パーサーが文章をすべて管理する実例。</summary>
    private sealed class LiveScriptFormat : IDocumentFormat
    {
        public RichDocument Parse(string source)
        {
            var blocks = new List<Block>();
            foreach (string line in (source ?? "").Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("--"))
                    blocks.Add(new Block(BlockKind.Paragraph, line[2..].TrimStart()));
                else if (line.Length == 0)
                    blocks.Add(new Block(BlockKind.Paragraph));
                else
                    blocks.Add(new Block(BlockKind.Embed) { Payload = new FencePayload("live", line) });
            }
            return RichDocument.FromBlocks(blocks);
        }

        public string Serialize(RichDocument doc)
            => string.Join("\n", doc.Blocks.Select(SerializeBlock));

        public string SerializeRange(RichDocument doc, DocPos min, DocPos max)
            => new DocumentEditor(doc).GetText(min, max);

        public bool SupportsHybrid => false;   // 記法がない (ソース = 表示) — hybrid 不要
        public Block ParseLine(string line) => Parse(line).Blocks[0];
        public string SerializeBlock(Block b) => b switch
        {
            { Kind: BlockKind.Embed, Payload: FencePayload f } => f.Body,
            { Kind: BlockKind.Paragraph, Length: 0 } => "",
            _ => "-- " + b.Text,
        };

        public bool TryAutoFormat(DocumentEditor ed, string inserted) => false;

        /// <summary>パターン行で Enter → その行をライブブロック化 (strudel の「行を評価」)。</summary>
        public bool TryBlockCommit(DocumentEditor ed)
        {
            Block b = ed.CaretBlock;
            if (b.Kind != BlockKind.Paragraph || b.Length == 0 || b.Text.StartsWith("--")) return false;
            ed.ConvertToEmbed(new FencePayload("live", b.Text));
            return true;
        }
    }

    /// <summary>ライブブロック: コードエディタ + Run/Stop + 波形出力の**複合コントロール**
    /// (CompositeControl — 手書き Layout/Realize なしで既存コントロールを Build で宣言)。
    /// 実行内容 (ここでは数列パターン → スクロール波形) はアプリ責務の見本 — 基盤は
    /// コード往復 (Commit)・Tick 駆動・Dispose だけを保証する。</summary>
    private sealed class LiveCodeBlock : CompositeControl, IDisposable
    {
        private readonly Signal<string> _code;      // 内部状態 (独自 Signal) — TextArea と双方向
        private readonly Action<IBlockPayload> _commit;
        private readonly TextArea _editor;          // 状態を保つ子はフィールド保持 (Rebuild を跨いで生存)
        private readonly Button _run, _stop;
        private readonly Sparkline _wave;
        private readonly float _maxW;

        private float[] _pattern = [];
        private readonly float[] _buf = new float[96];
        private float _phase;
        private bool _playing = true;

        public LiveCodeBlock(FencePayload payload, float maxWidth, Action<IBlockPayload> commit)
        {
            _maxW = MathF.Max(120, maxWidth);
            _commit = commit;
            _code = new Signal<string>(payload.Body);
            _editor = TextArea(_code, height: 46f, width: _maxW);
            _run = Button(_ => Run(), "Run");
            _stop = Button(_ => _playing = !_playing, "Stop/Go", variant: Variant.Ghost);
            _wave = Sparkline(_maxW, 44f);
            SetPattern(payload.Body);
        }

        protected override Widget Build()
            => VStack(spacing: 4)[
                   HStack(spacing: 6)[_run, _stop],
                   _editor,
                   _wave];

        protected override void OnRealize(UiBuildContext ctx)
        {
            // Tick 駆動 (再生中は波形が流れる)。スコープ破棄でアニメーションも消える
            ctx.AddAnimation(dt =>
            {
                if (_playing && _pattern.Length > 0) Advance(dt);
                return false;
            });
        }

        private void Run()
        {
            SetPattern(_code.Value);
            _commit(new FencePayload("live", _code.Value));   // 文書へ確定 (undo 可) — ブロックは作り直される
        }

        private void SetPattern(string code)
        {
            _pattern = code.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => float.TryParse(x, out float f) ? f : 0f).ToArray();
            if (_pattern.Length == 0) _pattern = [0f];
            Advance(0);   // 初期波形 (snap 決定性: phase=0 の静止波形)
        }

        private void Advance(float dt)
        {
            _phase += dt * 12f;
            for (int i = 0; i < _buf.Length; i++)
            {
                float p = _pattern[(int)(_phase + i * 0.4f) % _pattern.Length];
                _buf[i] = p * (0.75f + 0.25f * MathF.Sin((_phase + i) * 0.35f));   // ゆらぎ (ライブ感)
            }
            _wave.SetValues(_buf);
        }

        public override string? DebugDetail => $"live ({_pattern.Length} steps)";

        public void Dispose() => _playing = false;   // ブロック削除/差し替え/ツリー破棄で停止
    }

    [Story("LiveCode/StrudelLike", Height = 520)]
    public static Widget StrudelLike(StoryContext ctx)
    {
        // フォーマット + widget 解釈を対で構成 — 専用フォーマットは解釈を固定して配布する形の実例
        var format = new LiveScriptFormat();
        var widgets = new BlockWidgetRegistry()
            .Register("live", bc => new LiveCodeBlock((FencePayload)bc.Payload, bc.MaxWidth, bc.Commit));

        Signal<string> src = ctx.Signal("source",
            "-- LiveScript: markdown ではない独自フォーマット (パーサーが文章をすべて管理)\n" +
            "-- パターン行で Enter するとライブブロック化される (下の段落で試す)\n" +
            "3 1 4 1 5 9 2 6\n" +
            "-- ↑ Run = コードを文書へ確定 (Ctrl+Z で戻せる)、Stop/Go = 再生トグル\n" +
            "8 8 1 8 8 2\n" +
            "-- ここに数列を打って Enter:\n");

        RichTextEditor ed = RichTextEditor(src, height: 420, format: format, widgets: widgets);
        ed.Fonts = JpFallbackShared;
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[ed]];
    }

    /// <summary>ControlStories の JpFallback を共有 (別クラスからは internal アクセスできないため再構築)。</summary>
    private static readonly Luxel.Typography.FontCollection JpFallbackShared =
        new(Luxel.Typography.VectorFont.LoadSystem(), Luxel.Typography.VectorFont.LoadSystemJapanese());
}
