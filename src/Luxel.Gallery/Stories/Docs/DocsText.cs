using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — テキスト章 (Typography / Editor)。</summary>
public static class DocsText
{
    private static readonly DocMarkdown LayoutExample = new("""
        ```csharp
        var layout = new TextLayout(source, new TextLayoutOptions
        {
            MaxWidth = 300,            // ∞ = 折り返しなし
            Wrap = TextWrap.Word,      // None / Word / Char
            Align = TextAlign.Center,  // Left / Center / Right / Justify
            LineHeight = 1.2f,
        });
        Size size = layout.Size;                  // 計測
        layout.Draw(scene, x, y);                 // 描画 (色ごとの列挙も提供)
        int idx = layout.HitTest(px, py);         // 座標 → テキスト位置 (クラスタ吸着)
        Rect caret = layout.CaretRect(idx);       // キャレット矩形
        Rect[] sel = layout.SelectionRects(a, b); // 選択範囲の行別矩形
        ```
        """);

    [Story("Docs/Typography", Width = 800, Height = 480, Order = 40)]
    public static Widget Typography(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # テキスト (Luxel.Typography)

        HarfBuzz シェーピング + 自前レイアウトのテキスト基盤です。2D レイヤ (Luxel.TwoD)
        はテキストを知らない純粋レイヤのままで、フォント依存は Typography に隔離されています。
        ICU (完全な UAX#14/#29) は `Luxel.Typography.Icu` アダプタ — 使うアプリだけが参照します。

        ## パイプライン

        ```mermaid
        flowchart LR
        spans[spans] --> para[段落分割]
        para --> runs[run 分割 - スタイル/フォールバック境界]
        runs --> hb[HarfBuzz シェーピング]
        hb --> brk[行分割 - break 機会で貪欲詰め]
        brk --> comp[行組み - align/ベースライン]
        ```

        レイアウトは 1 回走らせて結果 (`TextLayout`) を保持・再利用します。
        描画とキャレット/ヒットテストが**同じ結果**から出るので、必ず一致します。

        ## TextLayout API

        {LayoutExample}

        実物: [Text/Multiline](story:Text/Multiline) (wrap / 禁則 / Justify) /
        [Text/EllipsisVAlign](story:Text/EllipsisVAlign) (maxLines + 省略記号)。

        ## セグメンタ (ITextSegmenter)

        行分割・グラフェム・単語境界の判定は 1 インターフェースに隔離されています:

        - **SimpleSegmenter** (内蔵、依存ゼロ) — 空白 + CJK 文字境界 + 代表的な禁則セット
          (行頭の「。、ゃっ」等 / 行末の「「『」等)。グラフェムは .NET の StringInfo
        - **Icu アダプタ** — icu.net の BreakIterator で完全な UAX#14/#29。
          `TextLayoutOptions.Segmenter` かプロセス既定で差し込む

        Justify は行の余り幅をラテンは空白へ、空白のない CJK 行は**字間へ**分配します
        (和文組版の字間調整)。段落最終行は分配しません — CSS と同じです。

        ## フォールバックとリッチテキスト

        `FontCollection` は優先順のフォント列です。run 分割時に未収載文字の連続を次候補
        フォントの run に切り出します (UI フォント → 日本語 → カラー絵文字 COLR)。
        行内のベースラインは run の最大 ascent に揃います:

        {StoryRef(ctx, "RichText/Basic")}

        保持型キャンバスは 1 ノード 1 色のため、色ごとに GlyphRun をグループ化して
        1 色 1 ノードで実体化します — テーマ連動色はノード単位の Effect でそのまま効きます。

        ## キャレットと選択 (合字対応)

        キャレットの単位は**グラフェム境界**。fi 合字のように 1 グリフに複数グラフェムが
        入る場合はグリフ advance を按分します (HarfBuzz 推奨の近似)。HitTest はクラスタ
        矩形の中央で左右に吸着し、選択は行ごとの矩形列です。

        次: [Docs/Editor](story:Docs/Editor) — この上に載る文書モデルとエディタへ。
        """, toc: true, fences: DocsFences));

    private static readonly DocMarkdown FormatInterface = new("""
        ```csharp
        public interface IDocumentFormat
        {
            RichDocument Parse(string source);
            string Serialize(RichDocument doc);
            bool SupportsHybrid { get; }                    // 行指向フォーマットのみ true
            Block ParseLine(string line);                   // hybrid の局所再パース
            bool TryAutoFormat(DocumentEditor ed, string inserted);   // 行頭 "- " 等の確定
            bool TryBlockCommit(DocumentEditor ed);         // Enter 時のブロック確定
        }
        ```
        """);

    [Story("Docs/Editor", Width = 800, Height = 480, Order = 41)]
    public static Widget Editor(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # ドキュメントとエディタ (Luxel.Document)

        リッチテキスト編集は 3 層に分かれます。Document 層は UI/GPU 非依存の純ロジックで、
        エディタの全意味論が UI なしでテストできます。

        ```mermaid
        flowchart TB
        typo[Luxel.Typography - TextLayout/キャレット] --> doc[Luxel.Document - モデル/編集/Markdown]
        doc --> ctl[Luxel.Controls - TextArea/RichTextEditor]
        ```

        ## RichDocument — ブロック列のモデル

        - **ブロック**: Heading / Paragraph / ListItem / Quote / CodeBlock / Divider /
          Callout / **Embed** (payload 付き — 画像・テーブル・任意 widget)
        - **インライン**: `InlineRun(Text, InlineStyle)` — Bold / Italic / Code / Link / Math。
          色やサイズはテーマとブロック型から導出します (WYSIWYG の一貫性)
        - **位置**: `DocPos(Block, Offset)`。操作はグラフェム単位。ブロックは Version を持ち、
          表示側レイアウトキャッシュのキーになります

        ## DocumentEditor — 編集エンジン

        挿入/削除、**Enter = ブロック分割** (リスト内は次項目、空項目で解除)、
        **行頭 Backspace = 前ブロックと結合** (リスト/引用はまず型解除)、スタイルトグル
        (Ctrl+B/I/E)、ブロック型変換、ブロック跨ぎ選択。undo/redo は逆操作ジャーナルで、
        連続タイプは 1 op に合体します。IME 合成はキャレットのあるブロック内に限定 —
        TSF の文書 = 現在ブロックです。

        実物: [RichTextEditor/Basic](story:RichTextEditor/Basic) (ツールバー付き) /
        [TextArea/Basic](story:TextArea/Basic) (プレーン複数行)。

        > [!NOTE]
        > エディタ系ストーリーは docs ページへの埋め込みに対応していません (embed 内
        > RichTextEditor の入れ子は既知の制限) — リンクで実物ページへ飛んでください。

        ## Markdown と hybrid 編集

        パースは **Markdig** (フル CommonMark + コールアウト + CJK 強調 + 絵文字 +
        SmartyPants)、シリアライザは正規形を出す自前実装で、**round-trip が安定**します
        (md → doc → md が収束)。1 ソース行 = 1 表示行の行指向モデルで、空行も保存されます。

        hybrid 表示 = Typora 風: キャレットの入ったブロックだけ**ソース表示**に切り替わり、
        離れると再パースして整形表示へ戻ります。行頭 `- ` や `# ` のオートフォーマットも
        フォーマット側の責務です。実物: [MarkdownEditor/Hybrid](story:MarkdownEditor/Hybrid) /
        [MarkdownEditor/VisualSource](story:MarkdownEditor/VisualSource) (双方向バインド)。

        ## IDocumentFormat — パーサーが文章をすべて管理する

        エディタ本体は「ブロック列の編集と表示」だけを持ち、テキスト往復・記法・embed 判定は
        **文書フォーマット実装の責務**です。Markdown は一実装にすぎません:

        {FormatInterface}

        ## 埋め込みブロック (Embed)

        `BlockKind.Embed` + `IBlockPayload` がデータ、widget 化は表示側の
        `BlockWidgetRegistry` が行います。canonical 形は `FencePayload(Info, Body)` —
        リゾルバのいない環境では**ただのコードブロックとして完全に保全**されます。
        画像・テーブル・チャート ([MarkdownEditor/Embeds](story:MarkdownEditor/Embeds))、
        そして独自フォーマット + ライブ実行の [LiveCode/StrudelLike](story:LiveCode/StrudelLike) も
        この機構です。**この docs ページ自体**の mermaid / 数式 / ライブ UI も同じ経路
        (IFenceResolver + BlockWidgetRegistry) で動いています。
        """, toc: true, fences: DocsFences));
}
