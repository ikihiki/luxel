using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — テキスト章 (Typography / Editor)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsText
{
    [Story("Docs/Typography", Order = 40)]
    public static Widget Typography(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());   // 新スタック: mermaid フェンス + コードブロックの描画 golden
        return DocNew(ctx, $$"""
        # テキスト (Luxel.Typography)

        HarfBuzz シェーピング + 自前レイアウトのテキスト基盤です。2D レイヤ (Luxel.TwoD) はテキストを知らない純粋レイヤのままで、フォント依存は Typography に隔離されています。ICU (完全な UAX#14/#29) は `Luxel.Typography.Icu` アダプタ — 使うアプリだけが参照します。

        ## パイプライン

        ```mermaid
        flowchart LR
        spans[spans] --> para[段落分割]
        para --> runs[run 分割 - スタイル/フォールバック境界]
        runs --> hb[HarfBuzz シェーピング]
        hb --> brk[行分割 - break 機会で貪欲詰め]
        brk --> comp[行組み - align/ベースライン]
        ```

        レイアウトは 1 回走らせて結果 (`TextLayout`) を保持・再利用します。描画とキャレット/ヒットテストが**同じ結果**から出るので、必ず一致します。

        ## TextLayout API

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

        実物: [Controls/Text/Multiline](story:Controls/Text/Multiline) (wrap / 禁則 / Justify) / [Controls/Text/EllipsisVAlign](story:Controls/Text/EllipsisVAlign) (maxLines + 省略記号)。

        ## セグメンタ (ITextSegmenter)

        行分割・グラフェム・単語境界の判定は 1 インターフェースに隔離されています:

        - **SimpleSegmenter** (内蔵、依存ゼロ) — 空白 + CJK 文字境界 + 代表的な禁則セット (行頭の「。、ゃっ」等 / 行末の「「『」等)。グラフェムは .NET の StringInfo
        - **Icu アダプタ** — icu.net の BreakIterator で完全な UAX#14/#29。`TextLayoutOptions.Segmenter` かプロセス既定で差し込む

        Justify は行の余り幅をラテンは空白へ、空白のない CJK 行は**字間へ**分配します (和文組版の字間調整)。段落最終行は分配しません — CSS と同じです。

        ## フォールバックとリッチテキスト

        `FontCollection` は優先順のフォント列です。run 分割時に未収載文字の連続を次候補フォントの run に切り出します (UI フォント → 日本語 → カラー絵文字 COLR)。行内のベースラインは run の最大 ascent に揃います:

        {{StoryRef(ctx, "Controls/RichText/Basic")}}

        保持型キャンバスは 1 ノード 1 色のため、色ごとに GlyphRun をグループ化して 1 色 1 ノードで実体化します — テーマ連動色はノード単位の Effect でそのまま効きます。

        ## キャレットと選択 (合字対応)

        キャレットの単位は**グラフェム境界**。fi 合字のように 1 グリフに複数グラフェムが入る場合はグリフ advance を按分します (HarfBuzz 推奨の近似)。HitTest はクラスタ矩形の中央で左右に吸着し、選択は行ごとの矩形列です。

        次: [Docs/Editor](story:Docs/Editor) — この上に載る文書モデルとエディタへ。
        """, toc: true);
    }

    [Story("Docs/Editor", Order = 41)]
    public static Widget Editor(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());   // 新スタック解説ページ (見出し/mermaid/コードの描画 golden)
        return DocNew(ctx, $$"""
        # ドキュメントとエディタ (Luxel.Document)

        テキスト編集は 2 層です。**Luxel.Document** が canvas 非依存のエディタコア (状態・編集・装飾・幾何)、**Luxel.Controls の `TextEditorView`** が塗り・入力・IME を足す薄いビュー。CodeMirror 6 に倣った設計です (決定は [ADR-0006](story:ADR/0006-Editor-New-Stack))。コアの全意味論が UI/GPU なしで単体テストできます。

        ```mermaid
        flowchart TB
        typo[Luxel.Typography - TextLayout/キャレット] --> core[Luxel.Document - 状態/編集/装飾/幾何]
        core --> view[Luxel.Controls TextEditorView - 塗り/入力/IME]
        ```

        ## EditorState と Transaction

        - **不変 `EditorState`** = `TextDoc` (テキスト) + `EditorSelection` (複数レンジ + main) + 装飾状態。編集は `Transaction` が運ぶ `ChangeSet` で、**`ChangeSet.MapPos` が選択・装飾・非同期結果の唯一の位置写像**
        - undo/redo は反転 `ChangeSet` の履歴 (`History`)。**1 Transaction = 1 undo**、連続タイプは 1 op に合体
        - 編集操作は `EditCommands` (挿入/削除/行操作/検索置換などの純関数)

        ## マルチカーソル (native)

        単一カーソルはレンジ 1 個にすぎません。`Ctrl+D` で次の同一語を追加、`Ctrl+Alt+↑↓` で縦列、`Esc` で解除。編集は 1 ChangeSet で全レンジ一括 = 1 undo。実物: [Controls/TextEditorView/MultiCursor](story:Controls/TextEditorView/MultiCursor)。

        ## 装飾は第一級の状態 (Decoration)

        すべての「見た目」は装飾として状態に載ります:

        - **Mark** — 前景色 / 背景 / 下線・波線 / 囲み / フォント変種 (Bold/Italic/Mono) / サイズ倍率 / 非表示
        - **Widget** — 行内 UI (範囲置換 or アンカー挿入、自動サイズ)
        - **BlockWidget** — 複数行を占有するブロック UI (表/図/数式/ライブ UI、自動高さ)
        - **LinePrefix** — 行頭の番号/箇条書き記号 (ソース 0 文字 ↔ 表示 k 文字)
        - **Block / Line** — 縦バー (引用) や行背景

        **レイアウトに効く** (色/変種/widget/prefix) と**効かない** (背景/下線/囲み = 矩形オーバーレイのみ、行キャッシュに触れず 60fps 更新可) を型で区別します。装飾は `IDecorationProvider` (テキストから導出・キャッシュ) か push 型の `SetDecorations` で載せます — シンタックス色・診断波線・検索ハイライト・現在行・Strudel 再生囲みは全てこの上に乗ります。

        ## ジオメトリは純射影 (EditorGeometry)

        `EditorGeometry` は `EditorState` + フォントから**表示行**を組む純射影で、選択を持ちません → canvas なしで単体テスト可能。ソース↔表示の桁写像で、行頭 prefix・行内 widget ボックス・非表示レンジ・IME 合成を 1 つのモデルに統合します。描画・キャレット・ヒットテストが同じ `TextLayout` から出るので必ず一致します。

        ## TextEditorView — 薄いビュー

        canvas がないとできないことだけを持ちます: 色別ノードでの塗り・入力配線・行内 widget のホスト・IME/TSF。補完/ホバーのポップアップは [ADR-0007](story:ADR/0007-Floating-Ui-Placement) の anchored placement で `CaretRect` にアンカーし画面端でフリップします。実物: [Controls/TextEditorView/Code](story:Controls/TextEditorView/Code) (色分け+波線+ガター) / [Widgets](story:Controls/TextEditorView/Widgets) (行内 widget) / [Strudel](story:Controls/TextEditorView/Strudel) (再生囲み)。Strudel REPL の各ライブブロックもこの `TextEditorView` です。

        ## コード編集 — 色分け・診断・補完

        コード向けの機能はビュー本体でなく**装飾プロバイダ + 言語サービス**で足します (view は言語非依存):

        - **`SyntaxHighlightProvider`** — `ISyntaxHighlighter` (フェンスの言語でコードをトークナイズ) をトークン色 Mark に。実装は別アセンブリ `Luxel.Highlight.TextMate` (VS Code と同じ .tmLanguage) を注入 — Controls/Document は TextMate 非依存
        - **`DiagnosticsProvider`** — `ICodeLanguage.Diagnose` の結果を波線 Mark に
        - **`CurrentLineProvider`** — キャレット行の行背景
        - **`ICodeLanguage`** (補完/診断/ホバー) を**外から注入**。C# 実装 (`ScriptWorkspace` を包む) は Gallery 側 (LSP のエディタ⇄サーバー分離と同じ疎結合)。**Ctrl+Space** でキャレット直下に補完ポップアップ (↑↓/Enter/Esc・タイプで絞り込み・クリック選択)、シンボルに**マウスを留めるとツールチップ**

        実物: [Controls/TextEditorView/Completion](story:Controls/TextEditorView/Completion)。

        ## Markdown レンダリングと Live Preview

        **`MarkdownDecorations.Build`** が markdown ソースを純関数で装飾に変換します (見出し = Bold+サイズ倍率・太字・斜体・インラインコード = Mono+背景・リンク・引用・リスト・コードフェンス・埋め込み)。これを `MarkdownProvider` (`IDecorationProvider`) 経由で `TextEditorView` に載せると markdown 文書レンダラになります。ワンショットは **`MarkdownDoc.Create`** — **この docs ページ自体もこれで描かれています** (旧 `Kit.Docs` の後継)。

        - **read-only モード** — 記法マーカ (`#` / `**` / `` ` `` / `>` / `-` / `[]()`) を非表示に畳み、リストは `•` bullet で見せる
        - **編集モード (Live Preview)** — `editable: true`。キャレットのある行だけマーカを raw で見せ、離れると整形に戻る (Typora 風)。実物: [Controls/TextEditorView/LivePreview](story:Controls/TextEditorView/LivePreview)

        見出しは TOC の `#アンカー` と `story:` リンクの素になり、起動時に**デッドリンク検証**が走ります。

        ## 埋め込みブロック (Embed)

        図/表/数式/ライブ UI は**ブロック widget** として文章の間に載ります。`embed <key>` フェンス (や `mermaid` / `math` 言語のフェンス) を `MarkdownDecorations` が自動高さ block widget にし、view の `WidgetResolver` が key + 本文から実 Widget を解決します (mermaid → `Luxel.Diagram`、数式 → `Luxel.MathText`、ライブ UI → 任意 widget)。リゾルバのいない環境では**ただのコードブロックとして保全**されます。行内 hole は `[￼](luxel-ui:N)` = 自動サイズの行内 widget。実物: [Controls/TextEditorView/DocEmbeds](story:Controls/TextEditorView/DocEmbeds) (mermaid + 数式) / [Embed](story:Controls/TextEditorView/Embed) (ライブ UI)。**この docs ページの mermaid / コードブロックも同じ経路**です。
        """, toc: true);
    }
}
