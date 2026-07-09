using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0006-Editor-New-Stack", Order = 77)]
    public static Widget Adr0006(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0006 — テキストエディタは Transaction ベースの新スタックを新規に作る

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        テキスト編集系コントロールに、性質の異なる要件が**同一エディタの同一行で同時に**求められるようになりました: 標準的な編集作業、Strudel の行内 UI コントロール配置と「いま鳴らしているシーケンス」を示す文字の囲み、C# 診断の下線・波線、コードエディタの行内色分けです。

        既存のエディタ (TextArea / CodeEditor / RichTextEditor) は共有エンジン DocumentEditor の上に建っており、その中核は**単一 Caret/Anchor** です。当初はこの資産を活かす「三層への段階移行」を検討しましたが、詰めると **単一カーソル制約に由来する 2 つの匂い**が避けられないと分かりました:

        - **選択の分裂** — マルチカーソルを入れると、プライマリ (engine) とセカンダリ (presenter) に選択状態が二分される。片方が単一カーソルモデルの都合でしかない
        - **写像機構の二重化** — 編集時の「engine が内部でカーソルを動かす」経路と「装飾を別途 MapThrough で動かす」経路が別物になり、位置写像が 2 系統になる

        これらは *制約の産物*で、native に複数レンジを持つモデルなら消える構造です。今回の 4 要件は**新しい能力の追加**であり、既存を改変せず新機能として作れます。ならば制約を引きずった移行ではなく、**綺麗なモデルを新スタックとして作る**のが筋だ、という力学です。

        ## Decision

        既存の DocumentEditor / TextArea / CodeEditor / RichTextEditor には**一切手を入れず**、**Transaction ベースの新エディタスタックを新規に追加**します。中核は canvas 非依存の新プロジェクト `Luxel.Editor`、ビューは `Luxel.Controls` の薄い widget。CodeMirror 6 に倣った設計です。

        - **不変の `EditorState`** = テキスト文書 + `Selection` (複数レンジ + main index) + 装飾状態。**マルチカーソルが native** — 単一カーソルはレンジ 1 個の特殊ケースで、プライマリ/セカンダリの区別は存在しない
        - **編集は Transaction** — 変更を `ChangeSet` (retain/insert/delete 列) として運び、`ChangeSet.MapPos` が**唯一の写像機構**として選択・装飾・非同期プロバイダの古い結果を一括写像する。undo は反転 ChangeSet (1 Transaction = 1 undo なので、N カーソルの 1 打鍵が自動で 1 undo)
        - **装飾は第一級の状態** — Mark (前景色/背景/下線/波線/囲み)・Widget (サイズ + 不透明キー)・LinePrefix (行頭の番号/記号)・Block (行グループ背景/縦バー)・Line (行背景)。供給側 (シンタックス/診断/検索/Strudel 再生位置) は DecorationSet を差し替えるだけ。**レイアウトに効く装飾** (色/widget/prefix = 行再レイアウト) と**効かない装飾** (背景/下線/波線/囲み = 矩形再計算のみ) を区別し、後者は行キャッシュに触れず毎フレーム更新できる (再生囲みが 60fps で動く根拠)
        - **canvas 非依存のジオメトリ層** — `TextLayout` を使ってソース↔表示写像 (行頭 prefix・widget ボックス・IME 合成を同一機構に統合)・pos↔座標・各種矩形を計算。**選択状態を持たない純射影**。TextLayout が canvas 非依存なので、この層まで GPU 不要の単体テストで固まる (実 DOM 計測を要する DOM 系エディタに対する Luxel の優位点)
        - **view は canvas がないとできないことだけ** ([UiComponent]、Luxel.Controls) — Transaction のディスパッチ、ジオメトリ出力を RetainedCanvas レイヤへ塗る、hit/focus/scroll/IME(TSF) 配線、インライン widget のホスト (resolver + `OnChildNeedsRealize`)、キャレット点滅。生入力を Transaction に変換する「状態を投影して塗るだけ」の皮
        - **具体エディタは構成**で作る — `CodeEditorView` = view + ガター + syntax/診断/検索プロバイダ + 補完 chrome。Strudel ブロック = view + インライン widget + 再生囲みプロバイダ
        - **トークナイザ/言語契約は新スタック側に持つ** (model 非依存の `SyntaxToken`/`TokenKind` は再利用可)。既存の TextMate/Roslyn 実装は薄いアダプタで橋渡しし、新スタックは旧 Luxel.Document モデルに依存しない

        実装計画は ToDo/22 (段階 S1〜S8)。旧 07 のマルチカーソル + 矩形選択は S7 に畳んだ (native モデルではほぼコマンド追加で済む)。現在の姿は [Docs/Editor](story:Docs/Editor) が正。

        ## Alternatives

        - **既存コントロールの段階移行 (当初案)** — プライマリ/セカンダリ分裂と写像 2 系統を制約として抱え込み、単一カーソル前提の undo ハックも要る。加えて 3 コントロール共有の engine を触るため波及が広い。要件が新機能である以上、綺麗な新スタックの方が良い → 却下
        - **DocumentEditor を native 複数レンジへ改修** — RichTextEditor のブロックモデル・IME/TSF ブリッジ・879 本のテストに波及する大手術 → 却下
        - **CodeEditor を機能ごとに増築し続ける** — 要件の組み合わせのたびに描画パスが増殖し、等幅前提の負債も残る (元の課題そのもの) → 却下
        - **既存エディタコンポーネントの移植 (AvaloniaEdit 等)** — 自前レンダラ前提で RetainedCanvas に載らない ([ADR-0003](story:ADR/0003-Declarative-Signal-Ui) と同じ力学) → 却下

        ## Consequences

        - ✅ モデルが一枚岩 — マルチカーソルとバッチ undo が実質タダで手に入り、4 要件は装飾の直交な組み合わせになる
        - ✅ 中核 (状態/変更/選択/装飾写像/ジオメトリ) が canvas 非依存で厚く単体テストでき、e2e play への依存が減る
        - ✅ 等幅ハードコードが無く、プロポーショナル・日本語・合字・折返しが最初から正しい
        - ✅ `ChangeSet` が選択・装飾・非同期結果の位置写像を単一機構に統一 — 逆順ループの職人芸が要らない
        - ⚠️ **エディタスタックが 2 つ併存する** — 旧 CodeEditor/TextArea は新スタックが実証されるまで残す (削除は将来の別判断)。RichTextEditor は文書/Markdown 役として存続。保守面積が一時的に増える
        - ⚠️ **新規コード量が大きい** — IME/TSF ブリッジ・undo・ChangeSet 代数を新 view 向けに正しく作り直す (DocumentEditor の実装は流用しない)
        - ⚠️ Strudel/デモを新コントロールへ移行する必要があり、MiniNotation にソーススパンの配管を足す (greenfield)
        - ⚠️ 「view を純粋な塗り役に保つ」境界規律を維持する必要がある — ロジックは canvas 非依存の層に置く
        """, toc: true, fences: DocsFences));
}
