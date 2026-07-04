# エディタ プラン (リッチテキストエディタ + Markdown ビジュアルエディタ)

2026-07-03 起案。status: **全マイルストーン完了 (ED-M1〜M5, 2026-07-03)**。前提: TEXT_PLAN (TX-M0〜M5) 完了 —
TextLayout (折返し/禁則/整列/Justify/ellipsis)・クラスタ正確な CaretRect/HitTest/SelectionRects・
リッチスパン/フォールバック・ITextSegmenter (ICU 差し替え可)・TextEditor (グラフェム編集)・TSF/IME が既にある。

## 目的

1. **リッチテキストエディタ** — 複数行・スタイル混在 (太字/斜体/コード/リンク/見出し/リスト/引用/コード
   ブロック) の編集。キャレット/選択/IME/undo が「描画と一致」する (TextLayout と同一ジオメトリ)。
2. **Markdown ビジュアルエディタ** — 整形表示のまま編集する WYSIWYG。方式は **hybrid (Typora/Obsidian 風)**:
   キャレットのあるブロックだけソース (記法) を表示して編集し、離れると再パース → 整形表示。
   ソース全体編集モードとの切替も持つ。

## 構成 (レイヤ分離 — UI なしで全ロジックをテスト可能に)

```
Luxel.Typography     … 既存 (TextLayout / キャレット / セグメンタ)
Luxel.Document       … 新設。ドキュメントモデル + 編集エンジン + Markdown (UI/GPU 非依存・純ロジック)
  ├ RichDocument     … ブロック列 (Paragraph/Heading/ListItem/Quote/CodeBlock/Divider)
  │                    ブロック = InlineRun 列 (text + InlineStyle{Bold,Italic,Code,Link})
  ├ DocPos/DocRange  … 位置 = (blockIndex, charOffset)。グラフェム境界へ吸着
  ├ DocumentEditor   … TextEditor の文書版: 挿入/削除/Enter 分割/Backspace 結合/スタイルトグル/
  │                    ブロック型変換/選択/undo・redo/IME 合成 (現在ブロック内)
  └ Markdown         … parser = **Markdig (フル CommonMark)** の AST を RichDocument へ写像 /
                       serializer = 自前の正規形 (bold=**, リスト=- , 連番再採番)
Luxel.Controls
  ├ TextArea         … 複数行プレーン編集 (ED-M2 の成果物。ログ入力等にも単体で有用)
  ├ RichTextEditor   … リッチ編集 widget (ブロック毎 TextLayout + 部分更新 + スクロール)
  └ MarkdownEditor   … RichTextEditor の上に hybrid 表示 + 入力オートフォーマット + モード切替
Luxel.Platform
  └ Win32Clipboard   … コピー/ペースト (plain + markdown)。IClipboard 抽象でテストはフェイク
```

## ドキュメントモデル (RichDocument)

- **ブロック**: `Heading(level 1..3)` / `Paragraph` / `ListItem(ordered?, depth)` / `Quote` /
  `CodeBlock(lang)` / `Divider`。将来枠: Image/Table (ブロック型の追加で拡張できる構造にする)。
- **インライン**: `InlineRun(string Text, InlineStyle Style)`、`InlineStyle{Bold, Italic, Code, string? Link}`。
  色/フォントサイズは**テーマとブロック型から導出** (WYSIWYG の一貫性のため直接指定は持たない)。
- **位置**: `DocPos(int Block, int Offset)` (Offset は char index、操作はグラフェム単位)。
  `DocRange(anchor, caret)`。ブロック跨ぎ選択可。
- ブロックに **Version (int)** を持たせ、編集で bump — 表示側のレイアウトキャッシュキーになる。

## 編集エンジン (DocumentEditor)

- 既存 TextEditor の意味論を文書へ拡張:
  - 文字挿入/削除 (グラフェム単位)、**Enter = ブロック分割** (リスト内は次項目、空項目で Enter = リスト解除)、
    **行頭 Backspace = 前ブロックと結合** (リスト/引用はまず型解除)。
  - キャレット移動: ←→ (グラフェム)、↑↓ (**TextLayout の CaretRect/HitTest で x を保存して行間移動** —
    折返し行も正しく)、Home/End (表示行)、Ctrl+←→ (単語 = Segmenter.GetWordAt)。
  - 選択: Shift+移動 / ドラッグ (ブロック跨ぎ)。ダブルクリック=単語、トリプルクリック=ブロック。
  - **スタイルトグル**: 選択範囲に Bold/Italic/Code を適用/解除 (run の分割/結合)。Ctrl+B/I/E。
  - **ブロック型変換**: 段落⇄見出し⇄リスト⇄引用⇄コード。
- **undo/redo**: 逆操作ジャーナル。連続タイプは 1 op に合体 (coalesce)、境界操作 (Enter/削除/スタイル/型変換/
  IME 確定) は独立 op。IME 変換中は記録しない (確定で 1 op)。
- **IME**: 合成 (preedit) はキャレットのあるブロック内に限定。既存 `ImeComposition`/`ITextInput` の経路を
  そのまま使う — **TSF の文書 = 現在ブロック** とする (ACP をブロック内オフセットに写像。文書全体を
  ACP にすると Replace/通知が複雑になるため v1 はブロック局所。ブロック跨ぎの変換は存在しない前提)。

## 表示 (RichTextEditor widget)

- **ブロック毎に TextLayout** を構築 (InlineRun → TextSpan/SpanStyle への写像はテーマ由来:
  見出しサイズ、コードは等幅風フォント + 地色、引用は文字色 + 左バー、リストはマーカー "・"/"1." を
  接頭スパンでなくインデント + マーカーノードで描く)。ブロック y は積み上げ。
- **部分更新が最重要** (毎キー入力で SetRoot しない): ListView/Sparkline と同じ流儀 —
  ブロック = ノード群 (色ごと) を保持し、**編集されたブロックだけ Content 差替え + Invalidate**。
  キャレット (点滅)/選択ハイライト/IME 下線は専用ノードの transform/Content 部分更新。
  ブロックの追加/削除/高さ変化は後続ブロックの transform 平行移動 (構造変更なし)。
- スクロール: 自前オフセット (transform) + ホイール + **キャレット追従スクロール** (編集で必須)。
  仮想化は v1 なし (ブロック数百まで想定。レイアウトはブロック Version + 幅でキャッシュ)。
- ヒット: y でブロック特定 → ブロック内は TextLayout.HitTest (クラスタ/グラフェム吸着 — 既存)。

## Markdown

- **対象記法 (v1 = CommonMark 部分集合)**: 見出し `#`〜`###`、リスト `- ` / `1. ` (ネスト = インデント 2)、
  引用 `> `、フェンスコード ```` ``` ````、水平線 `---`、インライン `**bold**` `*italic*` `` `code` ``
  `[text](url)`。**対象外 v1**: テーブル・画像・HTML・脚注 (将来枠)。
- **パースは Markdig 1.3.2 (BSD-2-Clause) を採用** — 自前パーサは廃止 (ユーザー指示)。AST → ブロック列の写像で
  ソフト改行はブロック分割 (行指向モデル維持)。setext 見出し/インデントコード/autolink/正確な強調解決が無償で付く。
  ParseLine (1 行) も同経路 (hybrid 表示の局所再パース)。
- serializer は正規形を出す (`*` でなく `**`、リストは `- `)。**round-trip テスト: md → doc → md が安定**、
  doc → md → doc が同値。

## Markdown ビジュアル (hybrid) の動作

- 通常時: 全ブロック整形表示 (RichTextEditor と同じ描画)。
- **キャレットが入ったブロックはソース表示に切替** (そのブロックの markdown 文字列を TextArea 的に編集)。
  ブロックを離れたら再パース → 整形表示へ戻す。パース失敗はソースのまま (データを失わない)。
- **入力オートフォーマット** (整形表示側での直接編集も可能にする):
  行頭 `# ` + 空白 → Heading 化、`- ` → リスト化、` ``` ` → コードブロック化、
  `**x**` を打ち終えた時点で bold run 化 (インラインはスペース/ブロック離脱時に確定)。
- **モード**: `Visual` (hybrid) / `Source` (文書全体を 1 つのソースとして編集 = TextArea)。切替でシリアライズ⇄パース。

## マイルストーン

- **ED-M1: モデル + Markdown (UI なし)** — RichDocument/DocPos/InlineStyle、Markdown parser/serializer。
  round-trip・ネストリスト・フェンス内エスケープの単体テスト。Luxel.Document 新設 (依存: Typography のみ
  — グラフェム吸着に Segmenter を使う)。
- **ED-M2: 複数行プレーン編集 (TextArea)** — DocumentEditor のプレーン部分 (分割/結合/↑↓ x 保存移動/
  Home/End/選択/ドラッグ)、TextArea widget (ブロック=段落のみ、部分更新 + キャレット追従スクロール)、
  IME (現在ブロック = TSF 文書)。E2E: 実キー + 日本語変換 (`vk 95 tsf` 相当の対話検証)。
  **完了 (2026-07-03)。実装メモ:**
  - DocumentEditor は UI 非依存 (Luxel.Document)。挿入/削除は **run 保存** (挿入は直前文字のスタイル継承、
    削除は run 跨ぎ + 隣接同スタイル結合) — ED-M3 のリッチ編集が同じ経路に乗る。分割は段落を作る (型継承は M3)。
  - 部分更新キー: テキスト変化 = Block.Version / 構造変化 = **DocumentEditor.StructureVersion**。
    TextArea は BlockView (ノード + TextLayout + 表示文字列) を保持し、表示文字列の差分でだけ再レイアウト、
    高さ変化は後続ブロックの transform 平行移動。構造変化のみブロックノード列を作り直す (SetRoot なし)。
  - ↑↓ は goal-x 保存 + 隣接行/隣接ブロックへの TextLayout.HitTest、Home/End は表示行 (LineCharRange)。
  - Key に **A を追加** (Ctrl+A 全選択。無修飾文字は WM_CHAR 側)。
  - **クリップは内側コンテナへ** — ルートに掛けると FocusRing (Z=-1 面塗り) がクリップレイヤ内で全面に被る。
    なお現行 FocusRing はフォーカス中コントロール全面に Primary 0.45 が乗る (TextField も同じ、既存挙動)。
  - グリフ未収載は `TextArea.Fonts` (FontCollection) で RichTextView と同じ流儀のフォールバック。
  - E2E 済 (150% DPI 実ウィンドウ): 実キー (SendKeys) タイプ/Enter 分割、リモート op で 結合/クリック配置/
    ブロック跨ぎドラッグ選択 (改行延長の可視化込み)/compose 下線+対象節/commit 置換/↓×20 追従スクロール。
    実 IME はギャラリー (SurfaceView 内) で不通だった → **SurfaceView が textInput を転送していなかった**
    (親ホストの TSF 文書が空 + GetTextExt が常時 TS_E_NOLAYOUT)。ChildTextInput proxy で修正し、
    `vk 95 tsf` E2E に「窓 C = SurfaceView 内 TextArea」を追加 (実変換 'nihongo'→'日本語' で検証済)。
- **ED-M3: リッチ編集 (RichTextEditor)** — スタイルトグル (run 分割/結合 + Ctrl+B/I/E)、ブロック型変換、
  リストマーカー/引用バー/コード地色の描画、undo/redo。ツールバー例 (Button 列) 付きストーリー。
  **完了 (2026-07-03)。実装メモ:**
  - モデル (DocumentEditor): Toggle{Bold,Italic,Code} は「選択の全文字が持つ→解除、混在→適用」。
    ApplyStyle が run を境界分割 + 同スタイル隣接結合。CodeBlock/Divider はインライン対象外。
    SetBlockKind は同型トグルで段落へ、**CodeBlock へは範囲を 1 ブロックに結合 / から他型へは行毎に分解**。
    Enter: リスト=次項目 (空項目で解除)、引用=継続、見出し=後半段落、**コード=リテラル \n (末尾空行 Enter で脱出)**。
    行頭 Backspace は段落以外まず型解除。分割はリッチ (run 保存) に昇格。
  - **undo/redo = 逆操作ジャーナル**: 各操作が影響ブロック範囲 (start, count) を宣言 → 事前 clone、
    適用は範囲置換 + キャレット復元 (Apply が逆エントリを返す対称形)。タイプ合体 = 同一ブロック・
    1 秒以内・キャレット移動なし。TSF の preedit 更新 (ReplaceInBlock) も typing 扱いで合体。上限 200。
  - 表示 (RichTextEditor): 値は **markdown 文字列と双方向** (Serialize/Parse)。ブロック = コンテナノード +
    色キー毎の子ノード (DrawColorRuns 白描き、SpanStyle.Color をキーに流用: Text/Muted/Primary/Border/SurfaceAlt)。
    差分キー = Block.Version | リスト番号 | 合成表示。番号は表示側で再計算 (serializer と同じ規則)。
    bold/italic/mono は差し替え書体 (BoldFont 等、segoeuib/segoeuii/segoeuiz/consola — 未設定は通常書体)。
    goal-x はキャンバス座標 (ブロック毎 indent 差を吸収)。Ctrl+B/I/E/Z/Y (Key に B/E/I/Y/Z 追加)。
    ツールバーは `Apply(Action&lt;DocumentEditor&gt;)` 経由 (フォーカス不要)。
  - E2E 済: 行選択 → Ctrl+B → Ctrl+Z (実窓)、ツールバー B/H1 クリック (gallery 経由 pointerdown+up)。
    注: リモート op の `click` を story ホストへ直接送ると Button に届かない (ハーネス側の別件、調査タスク化)。
- **ED-M4: Markdown ビジュアル (MarkdownEditor)** — hybrid 表示 (アクティブブロック = ソース)、
  入力オートフォーマット、Visual⇄Source 切替。ストーリー + snap (編集前の静止状態)。
  **完了 (2026-07-03)。実装メモ — 専用 widget は作らず RichTextEditor の機能として実現:**
  - **MarkdownEditor = `RichTextEditor { HybridSource = true }`**。値が markdown 双方向なので
    Visual⇄Source は同一 signal を RichTextEditor / TextArea で共有すれば成立 (VisualSource ストーリーで実証)。
  - hybrid: キャレット進入で `SwapBlock` (**ジャーナル外** 1:1 置換) によりブロックを SerializeBlock した
    ソース段落へ展開 (等幅表示)、離脱で ParseLine して畳む — **離脱 = 記法の確定** (プレーン段落に
    打った "- x" も畳みで確定する)。コードブロックは対象外 (ソースが複数行 = 行指向が崩れる)。
    クリック進入は展開後に**もう一度ヒットテスト** (二段ヒット) で正確なキャレット、キー進入は
    行頭記法長 (PrefixLen) の近似写像。IME 合成中は展開/畳み込みしない。
  - **値の汚染に注意**: ソース展開中のブロックをそのまま Serialize すると記法がエスケープされる
    (\*\*bold\*\*) — signal へ流す値と外部変更比較は「展開ブロックを ParseLine で畳んだ姿」で直列化
    (SerializeForValue)。外部変更で SetBlocks したら _srcBlock リセット。
  - オートフォーマット (AutoFormat、既定 on、hybrid 中は行頭系 off — 畳み込みが同役割):
    行頭 "# "/"## "/"### "/"- "/"1. "/"> " + **空白 1 打鍵**で ApplyAutoFormat (prefix 削除+型変換、1 undo op)、
    "```lang" + Enter で ConvertToCodeFence。E2E 注意: リモート op で "- " を 1 つの char で送ると
    トリガしない (実キーボードは 1 文字ずつ)。
  - undo とソース状態: 展開中に積んだ undo エントリはソース段落時代のスナップショット — undo で
    ソース形に戻ることがある (キャレット移動時の SyncHybrid が畳み直す)。
- **ED-M5: 仕上げ** — Win32 クリップボード (IClipboard 抽象、plain + markdown 形式)、
  ダブル/トリプルクリック選択、Ctrl+Z/Y・Ctrl+A、パフォーマンス検証 (タイプ時に編集ブロック以外の
  再レイアウト/フル再構築が起きないことを DiagFlush で assert する E2E)、ドキュメント、
  **TSF display attribute 対応** — ITfContextOwnerCompositionSink + ITfDisplayAttributeProvider の購読で
  変換中テキストの節情報 (input/converted/target) を取得し、preedit 下線 + 変換対象節の強調を
  実 IME 経由でも表示する (現状は Replace で通常テキストとして流れ、下線は remote op 経由のみ)。
  **完了 (2026-07-03)。実装メモ:**
  - **クリップボード**: IClipboard 抽象 + 静的 `UiClipboard.Instance` (クリップボードは本質グローバル —
    Platform の Win32Clipboard を WindowHost が起動時登録、テストはフェイク差し)。Ctrl+C/X/V を
    TextField (plain、Pattern 規制つき、改行は空白へ)/TextArea (plain)/RichTextEditor
    (**markdown 形式 — Markdown.SerializeRange**: 端ブロックは選択 run のみ切り出し + 型記法保持) に配線。
    貼り付けはプレーン挿入 (\n はブロック分割、hybrid なら離脱時に記法確定)。
  - **ダブル/トリプルクリック**: onDragStart で 500ms/4px の連打判定 → Segmenter.GetWordAt で単語 /
    ブロック全選択。3 エディタ共通。
  - **TSF display attribute**: TsfTextStore が `ITfContextOwnerCompositionSink` (合成範囲追跡) +
    `ITfTextEditSink` (OnEndEdit で GUID_PROP_ATTRIBUTE 列挙 → CategoryMgr/DisplayAttributeMgr で
    TF_ATTR_TARGET_CONVERTED の範囲を解決)。装飾は**専用経路** `ITextInput.SetCompositionHighlight`
    (default interface method — preedit 本文は従来どおり SetText で文書内、装飾だけの通知) で
    UiHost → SurfaceView proxy → widget へ。attribute 読み失敗時は下線のみに退化 (_attrBroken)。
    `vk 95 tsf` で実変換のスクリーンショット取得 (preedit 下線 + 変換後の対象節地色、TextField と
    SurfaceView 内 TextArea の両方で確認済)。
  - **性能ゲート**: canvas の Rebuild は Content 差替えで起きる設計 (SetRoot なしの widget 局所性が本質)。
    ゲートは「タイプ 1 打鍵で編集ブロック以外の Block.Version / StructureVersion が動かない」の
    モデルテスト (Typing_BumpsOnlyEditedBlockVersion) として担保 — 表示側はこのキーで部分更新するため。
  - E2E 済: ダブルクリック単語選択 → Ctrl+C ('# 見出し' = markdown 形式) → Ctrl+V (プレーン挿入)。

## リスク / 判断メモ

- **部分更新の規律**: 毎キー入力で chrome SetRoot すると実用にならない — ブロック局所の Content 差替えを
  最初 (ED-M2) から作法にする。DiagFlush (fullRebuild=false) を性能ゲートにする。
- **TSF の範囲**: v1 は「現在ブロック = TSF 文書」。GetTextExt は既存の CaretRect 経路 (DPI スケール済) を
  ブロック y オフセット込みで返す。文書全体 ACP 化は将来 (必要になったら)。
- **undo の粒度**: タイプ合体の区切りは「1 秒無操作 / キャレット移動 / 境界操作」。IME は確定 = 1 op。
- **クリップボード**: OS API は Platform に隔離 (IClipboard)。コピーは plain と markdown の両形式を載せる。
  リッチ HTML 形式は範囲外 v1。
- **色/サイズの直接指定を持たない**: WYSIWYG は「意味 (見出し/強調)」だけを持ちテーマが見た目を決める —
  markdown との round-trip が壊れないための制約。任意色が要る用途は既存 RichTextView (表示専用) を使う。
- **snap 戦略**: エディタの golden は「静止状態」(初期文書 + 特定キャレット位置) のみ。編集シーケンスは
  E2E (リモート op + SendKeys) とロジック単体テストで担保。
