# 22 — テキストエディタ新スタック (Transaction ベース、greenfield)

## 概要

テキスト編集の**新しいスタックを新規に作る**。既存の DocumentEditor / TextArea / CodeEditor / RichTextEditor には**一切手を入れない** — 新機能としての追加。満たす要件は 4 つで、**同一エディタの同一行で同時に**成立する:

1. テキストエディタとして標準的な編集作業ができる
2. Strudel: 行内に UI コントロールを配置できる。どのシーケンスを鳴らしているか判別する文字の囲みを表示できる
3. C#: 文字列範囲に下線・波線を表示できる
4. コードエディタ: 行内で文字が色分けできる

CodeMirror 6 に倣った **Transaction + native 複数レンジ + 装飾状態 + 純射影ジオメトリ + 薄い view** の構成。決定の経緯と却下案は **ADR-0006** (`ADR/0006-Editor-New-Stack`) が正 — 着手前に読むこと。旧 **07 (マルチカーソル + 矩形選択)** は S7 に統合済み (07 MD 削除済み)。

**特大タスク・複数セッション前提**。ステージ S1〜S8 を NEXT.md のキュー 1 エントリ = 1 ステージで進める。全ステージ完了まで本 MD は削除しない。

## なぜ新スタックか (制約に由来する匂いの回避)

既存エンジン DocumentEditor は単一 Caret/Anchor。ここにマルチカーソルを載せると (a) 選択がプライマリ(engine)/セカンダリ(presenter)に分裂し、(b) 位置写像が「編集時の engine 内カーソル移動」と「装飾の別途 MapThrough」の 2 系統になる。どちらも単一カーソル制約の産物。native に複数レンジを持つモデルなら両方消える。要件は新機能なので、制約を引きずる移行ではなく綺麗なモデルを新規に作る (ADR-0006 参照)。

## プロジェクト構成と依存

- **`Luxel.Editor`** (新規、canvas 非依存) — 状態・変更代数・選択・装飾・ジオメトリ・コマンド。依存は `Luxel.Typography` (TextLayout/TextRect) と Rect 等のコア数学のみ。**旧 Luxel.Document に依存しない**
- **view は `Luxel.Controls` に新規ファイル** — 既に Luxel.UI/TwoD/Typography を参照済み。`Luxel.Controls → Luxel.Editor` の ProjectReference を追加。[UiComponent] ジェネレーター配線を既存コントロールと同じ場所に置ける
- **トークナイザ契約**: `SyntaxToken`/`TokenKind`/`ISyntaxHighlighter` ([src/Luxel.Document/Syntax.cs](../src/Luxel.Document/Syntax.cs)) は RichDocument 非依存で**再利用可**だが、新スタックを旧アセンブリに結ばないため `Luxel.Editor` に同等契約を定義し、既存 `TextMateHighlighter` (Gallery が注入) / `ICodeLanguage` (Luxel.Controls) は薄いアダプタで橋渡しする
- **再利用する既存資産**: `TextLayout` ([src/Luxel.Typography/TextLayout.cs](../src/Luxel.Typography/TextLayout.cs), canvas 非依存の shaping/折返し/HitTest/CaretRect/SelectionRects/インラインボックス)。インライン widget ホストの知見は RichTextEditor ([src/Luxel.Controls/RichTextEditor.cs](../src/Luxel.Controls/RichTextEditor.cs)) の 3 点セット (`SpanStyle.BoxW/BoxH` 占有 → `SelectionRects(off,off+1)`+`LineAscentAt` 配置 → `OnChildNeedsRealize` 高さ吸収) を**移植元として参照** (RichTextEditor 自体は触らない)

命名は暫定。着手時に見直してよい。

## アーキテクチャ

```
Luxel.Editor (canvas 非依存)
  EditorState        — 不変スナップショット: TextDoc + Selection + 装飾フィールド
  TextDoc            — 行インデックス付きテキスト (v1 は素朴実装可、rope 化は将来)
  Selection          — IReadOnlyList<SelectionRange> + MainIndex (native 複数レンジ)
  SelectionRange     — (int Anchor, int Head) フラットオフセット
  ChangeSet          — retain/insert/delete 列。MapPos(pos, assoc) / Compose / Invert
  Transaction        — Start + ChangeSet + NewSelection? + Effects。Apply() → 新 EditorState
  History            — 反転 ChangeSet のスタック (1 Transaction = 1 undo)
  Decoration         — Mark(fg/bg/underline/wavy/box) / Widget(size+key) / LinePrefix / Block / Line
  DecorationSet      — ソート済みレンジ + Map(ChangeSet)
  EditorGeometry     — 純射影: Configure(fonts,size,wrap,width) + 行 TextLayout キャッシュ +
                       ソース↔表示写像 + PosToCoords / CoordsToPos / SelectionRects /
                       DecorationRects / WidgetSlots / ContentHeight。選択を持たない
  Commands           — (EditorState) → Transaction の純関数群 (移動/編集/選択/マルチカーソル)
        ▼
Luxel.Controls (canvas)
  TextEditorView     — [UiComponent]。EditorState を Signal で持ち Transaction をディスパッチ。
                       ジオメトリ出力を Scene2D レイヤへ塗る + hit/focus/scroll/IME(TSF) 配線 +
                       widget resolver/Realize/高さ吸収 + キャレット点滅
  CodeEditorView     — 構成: TextEditorView + ガター + syntax/診断/検索プロバイダ + 補完 chrome
```

**写像の一枚岩**: `ChangeSet.MapPos` が唯一の位置写像。選択レンジ・装飾・非同期プロバイダの古い結果 (発行時 state からの ChangeSet を合成して現在へ写像) が全部同じ経路を通る。降順ループの職人芸は不要。

**レイヤ Z 順** (view): 行背景 → 囲み塗り → 選択 → テキスト → 下線/波線 → 囲み枠 → キャレット(複数) → インライン widget。

**境界の割り切り**: Scene2D 生成は view に残す (ノード分割・1 ノード 1 色 `ContentColors`・レイヤは retained tree の都合)。「矩形と写像まで Luxel.Editor、塗りは view」で固定。

## ステージ

### S1 — コア: 状態 + 変更代数 + 選択 + undo (canvas 不要)

`Luxel.Editor` プロジェクト新設。`TextDoc` (行インデックス)、`SelectionRange`/`Selection` (複数レンジ + main、重なりマージ正規化)、`ChangeSet` (MapPos/Compose/Invert)、`Transaction`/`EditorState.Apply`、`History` (反転 ChangeSet)。

- 単体テスト主体: ChangeSet の合成/写像、複数レンジが編集を跨いで正しく移動、undo/redo、選択正規化、1 Transaction = 1 undo。UI 未接続なので golden 影響なし

### S2 — 装飾を状態として持つ + プロバイダ

`Decoration` 型群、`DecorationSet` (ソート + `Map(ChangeSet)`)、装飾フィールドを Effect/Transaction で更新。プロバイダ契約 `(EditorState) → DecorationSet` (同期) + 非同期ディスパッチ (発行時 state からの変更合成で古い結果を写像)。

- 単体テスト: 編集を跨ぐ装飾写像、重なる Mark の重畳、非同期の古い結果の写像・破棄。レイアウト依存/非依存の分類を型で表す

### S3 — ジオメトリ (純射影、TextLayout 使用、canvas 不要)

`EditorGeometry`: Configure、行 TextLayout キャッシュ (行版数 + 装飾版数)、ソース↔表示ランレングス写像 (行頭 prefix・widget ボックス・IME 合成を統合)、`PosToCoords`/`CoordsToPos`/`SelectionRects`/`DecorationRects`/`WidgetSlots`/`ContentHeight`、縦移動/goal-x のコマンドヘルパ。

- 単体テスト: prefix/widget/IME 混在での pos↔座標往復、HitTest、折返し

### S4 — view widget + コマンド + 基本編集 (採用可能な最小)

`TextEditorView` [UiComponent]: EditorState Signal + Transaction ディスパッチ、Scene2D レイヤ、hit/focus/scroll、キャレット点滅、**IME/TSF ブリッジ** (`ITextInput`、合成は main レンジ)。`Commands` (移動/編集/選択) を純 `(state)→Transaction` で。

- e2e: 基本エディタ story (タイプ/選択/キャレット) + golden。[UiComponent] を足すと Reference/Overview の自動 API golden が変わる → --update 対象

### S5 — インライン widget + 行頭/ブロック装飾 (view ホスト)

widget resolver (`object Key` → Widget、BlockWidgetRegistry と同じ流儀)、Realize + `OnChildNeedsRealize` 高さ吸収、アンカー型/置換型の両 widget。LinePrefix (リスト番号/記号)、Block 背景/縦バー。

- デモ story: リスト番号付きテキスト + 行内 widget (数値リテラルにスライダ等)

### S6 — コードエディタ機能

`CodeEditorView` (構成): ガター (行番号、スクロール同期・クリップ)、syntax プロバイダ (`ISyntaxHighlighter` アダプタ → Mark.Foreground)、診断プロバイダ (`ICodeLanguage.Diagnose` → Mark.Underline 実線/波線)、検索 (Mark.Background)、現在行 (LineDecoration)、補完ポップアップ + dwell ホバー、行操作。

- story + golden。dwell 等はフレームカウント方式 (Q21 の前例、wall-clock 禁止)

### S7 — マルチカーソル + 矩形選択 (旧 07 統合、native なのでほぼコマンド)

native 複数レンジなので engine 手術は不要。コマンド追加が主:

- `Ctrl+D` = 次の同一語を追加選択 (全件済みでラップ)、`Alt+Click` = カーソル追加/同一位置で除去、`Escape` = セカンダリ解除 (補完ポップアップ優先)
- 矩形選択 (`Alt+Shift+ドラッグ` / `Ctrl+Alt+↑↓`) = 各行 1 レンジの縦列を生成。列→オフセットは**同一 x への HitTest** (等幅非依存)
- 編集適用は ChangeSet が一括 (N 挿入を 1 ChangeSet に、写像が前方ずれを吸収)。undo は 1 Transaction = 1 undo で自動。**IME は main レンジのみ** (合成開始でセカンダリ抑制)
- 描画: キャレット複数 (点滅同期、セカンダリは色/透明度を変える)、EnsureCaretVisible は main 基準
- 単体テスト: Ctrl+D×2 → 打鍵で 3 箇所置換が 1 undo / Alt+Click 追加除去 / 矩形 → 縦列編集 / IME でセカンダリ抑制。story + play + golden

### S8 — Strudel 採用 + 再生囲み + デモ + Docs

- Strudel REPL のライブブロックを `CodeEditorView` に切替 (StrudelStory.cs、StrudelBlock)。旧 CodeEditor はそのまま他所に残る
- **MiniNotation にソーススパン** ([src/Luxel.Strudel/MiniNotation.cs](../src/Luxel.Strudel/MiniNotation.cs)): 各アトムの (開始, 長さ) を Pattern イベントメタデータへ伝播 (現状はエラー位置のみ — greenfield)
- StrudelScheduler が「現在鳴っているイベントのスパン集合」を公開 → StrudelBlock が "playing" プロバイダ (`Mark.Box`) を毎フレーム流す。**音を出す play は `StrudelStory.HeadlessAudio` 判定に乗せる** (Vortice GC レース回避、メモリ/Q21)
- 行内 widget デモ (数値に置換型スライダ)。Docs/Editor を新スタック前提に書き直す

完了 = **本 MD を削除**し、仕様を Docs/Editor (+ Docs/Strudel の再生囲み節) に現在形で記載。

## 罠・注意

- **既存の 4 コントロールを 1 行も変えない** — Syntax.cs の契約型を再利用する場合も namespace 移動などはしない (旧コントロールに波及する)。アダプタで橋渡し
- 旧 CodeEditor/TextArea の削除は本タスクのスコープ外。新スタックが実証されてから別途判断
- [UiComponent] 規約: instance ctor 禁止 (NGUI002)、`[UiParam] Bindable<T>`、新コンポーネントで Reference/Overview の自動 API golden が変わる → --update に含める
- golden 差分はステージごとに意図分のみ。`--update` は全 PNG 再エンコードするので actual 昇格方式 (メモリ参照) か diff 除外リストで
- `Key` enum / KeyEvent の Alt 修飾が足りるか S7 着手時に確認 (E3 の前例: Win32 KeyMap 追加)
- 決定性: play は wall-clock/Task.Delay 禁止。時間は固定 dt、乱数は固定シード
- worktree で作業する場合は tools/ junction (メモリ/README 参照)

## スコープ外

- 旧 DocumentEditor スタックの改変・削除 (新スタックは並置。RichTextEditor は文書/Markdown 役で存続)
- `TextDoc` の rope 化 (v1 は素朴実装で可、インタフェースを rope 差し替え可能にしておく)
- 折返し有効時の矩形選択 (VS Code も表示行基準で乱れる — コードは折返しなし前提)
- `Ctrl+Shift+L` (全件選択) 等の派生コマンド (Ctrl+D 後に自然に足せるが必須でない)
- Strudel 以外の言語での行内 widget 実戦投入 (機構はデモ story で担保)
