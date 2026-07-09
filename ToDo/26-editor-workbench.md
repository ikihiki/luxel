# 26 — エディタ Workbench: 複数エディタを束ねるシェル + 基盤整備

## 概要

Luxel の成熟した編集コンポーネント (テキスト新スタック = ADR-0006 / ノード新スタック = ADR-0009) を「**開いて・並べて・保存する**」**Workbench フレームワーク**を新規に作り、Gallery をその上で再構築する。あわせて土台の穴 (PointerEvent の修飾キー・文書レンダラ・メニュー/コマンド・基盤 UI コントロール) を塞ぐ。

これは**複数 ADR にまたがる大プログラム**。決定は次の 5 ADR が正 (着手前に該当を読む):

- **ADR-0010** `Workbench-Framework` — `IEditorDocument`/`Workspace`/`DockTree`(モデル)/`IDocumentStore`
- **ADR-0011** `Pointer-Button-Modifiers` — PointerEvent にボタン/修飾キー (D&D の前提)
- **ADR-0012** `Rich-Document-Stack` — Markdown/リッチ文書を**テキストスタックの構成**として実装 (別プロジェクト無し)、RichTextEditor 置換
- **ADR-0013** `Menu-Command-System` — CommandRegistry を単一の真実にメニュー/パレット/ツールバー/キーマップを生成 (リボン非採用)
- **ADR-0014** `Workbench-Ui-Controls` — DocumentTabs / DockHost / PropertyGrid / StatusBar / AssetBrowser

## 背景と現状 (2026-07-09 調査)

- **編集コンポーネントは完成** — `Luxel.Editor`+`TextEditorView` / `Luxel.NodeGraph`+`NodeGraphView`。両者は鏡写し (Doc→State→Transaction→ChangeSet→History→Decoration→Geometry→Commands→View)。
- **シェル層が全面的に無い** — 唯一のシェルは `GalleryApp` (Storybook 風・単一ドキュメント)。複数 doc タブ・ドッキング・FS 連動ツリー・コマンドパレット・メニューバー・複数バッファ・open/save 経路がいずれも未実装。`Splitter`/`Tabs`(ビュー切替専用)/`TreeView`/`ContextMenu`/`PopupPlacer` は部品として在る。
- **永続化は未結線** — `IFileStore` は設定/セーブ専用、`Luxel.Resources`/`res://` は read-only。`Luxel.Controls` に File IO は 0 件。
- **PointerEvent の穴** — ボタン/修飾キーが無く ([src/Luxel.UI/PointerEvent.cs](../src/Luxel.UI/PointerEvent.cs))、Alt+Click マルチカーソル・pan ドラッグ・タブ/ペイン D&D が全て延期中。
- **RichTextEditor は現役の Docs 描画エンジン** — `Kit.Docs()` が返す型で Docs/ADR 全 11 モジュールを描く (ブロック + 埋め込み UI + mermaid + 数式 + fence + 全文検索)。行指向のテキスト新スタックでは代替不可。内容プロセッサ (Diagram/MathText/Highlight.TextMate) は `Luxel.Document` のブロック型に結合。
- **旧スタック整理の方針** (ユーザー決定 2026-07-09): `CodeEditor` は削除 (TextEditorView が上位互換)。`RichTextEditor` は新文書スタックへ移行してから削除。

## ワークストリームと順序

```
0011 PointerEvent ─┬─────────────────────────────────────► D&D の前提
                   │
0012 リッチ文書 ───┼─► Docs 移行 ─► RichTextEditor/TextArea 削除 ┐
WS-B CodeEditor ───┘ (TextEditorView へ移植 → 削除)              │
                   0010 Workbench(モデル) ─► 0014 基盤UIコントロール ─► 0013 メニュー/コマンド
                                                                 │
                                                                 ▼
                                               WS-D 具体エディタ (エディタ作成の本体)
```

**着手順の既定 = 依存順**: 0011 → (0012 / WS-B は並行) → 0010 → 0014 → 0013 → WS-D。各ワークストリームは独立に「次へ」で着手できる粒度。

---

## WS-0 — PointerEvent 拡張 (ADR-0011) ✅ 完了 (2026-07-09、Q39)

- `PointerEvent` に `Button` (Left/Right/Middle) と `Modifiers` (Ctrl/Shift/Alt/Meta ビットフラグ) を追加。`UiHost` の PointerDown/Move/Up → `PointerEvent` → `HitTarget` を貫き、ドラッグ捕獲時のボタンも保持。
- Platform 層 (Win32 メッセージ) でイベント発生時点の状態を詰める。
- **無回帰**: 既存ハンドラは新フィールドを無視して従来どおり。全ドラッグ系の golden diff 0 を確認。
- 解禁: テキスト Alt+Click マルチカーソル (ADR-0006 S7 の延期解消) / ノード pan ドラッグ・Ctrl+Click 追加選択 (ADR-0009 の延期解消)。両エディタの view にこれらを足す。

## WS-A — Markdown/リッチ文書をテキストスタックの構成に (ADR-0012)

**別プロジェクトは作らない** — テキスト新スタック (`Luxel.Editor` + `TextEditorView`、ADR-0006) の構成として載せる。ブロックはテキストの射影 = 編集は ChangeSet、ブロック意味論はコマンド、表示は行/ブロック/widget 装飾 (CM6 / Obsidian Live Preview 流)。

- **S(A1)** テキストスタック拡張 — **font-variant Mark** (太字/斜体/見出しサイズ、`AffectsLayout`) + ジオメトリの行内 mixed-weight ラン対応 + ブロック widget の Markdown 利用。GPU 不要の単体テスト。
- **S(A2)** Markdown 装飾プロバイダ + widget リゾルバ (`Luxel.Controls`) — パーサ → Line/Block/Mark/LinePrefix + widget。内容プロセッサ (Diagram/MathText/Highlight.TextMate) を **widget コンテンツ**として再利用し、`Luxel.Document` 依存の共有ブロック/フォーマット型をここへ移設 (or 中立化)。**read-only 描画モード** = Docs レンダラ。
- **S(A3)** `Kit.Docs()` をこの構成へ差し替え、Docs/ADR 全 11 モジュールを移行 (golden 全再生成、意図差分のみ)。全文検索/TOC/`story:` リンクの無回帰。
- **S(A4)** **ブロック単位コマンド** (move/indent/heading/list 継続/fold) + 編集 (ハイブリッド/Live Preview) モード = Markdown エディタ。
- **S(A5)** `RichTextEditor` + `TextArea` + `Luxel.Document` の編集核 (`DocumentEditor`) を削除。TextArea 依存箇所は TextEditorView (プレーンモード) へ。
- 編集モード (S(A4)) は read-only レンダラの後段 — Markdown 編集需要が出た時点で厚くしてよい。

## WS-B — CodeEditor 削除

- `CodeEditor` を使う 5 ストーリー (CodeEditorStory / TextControlStories / ScriptHotReloadStory / ScriptingStory のノートブックセル含む) を `TextEditorView` + プロバイダ (syntax/診断/補完) へ移植。
- `src/Luxel.Controls/CodeEditor.cs` を削除。Docs/Editor の CodeEditor 節を TextEditorView に更新。golden は移植分のみ差分。

## WS-C — Workbench コア + 基盤 UI (ADR-0010 / 0014)

- **S(C1)** `Luxel.Workbench` コア — `IEditorDocument`/`IDocumentProvider`/`Workspace` (開閉・アクティブ・ダーティ集約・undo 委譲)。依存ゼロ・単体テスト。
- **S(C2)** `DockTree` (領域 + タブグループの再帰木・直列化/復元)、`IDocumentStore` (VFS に Write / open・save・saveAs・watch / ダーティ・外部変更検知)。純ロジック・単体テスト。
- **S(C3)** 基盤コントロール (ADR-0014、`Luxel.Controls`): `DocumentTabs` (ダーティ/×/D&D/オーバーフロー) / `DockHost` (DockTree 描画 + ドロップゾーン + 窓内フローティング) / `StatusBar`。golden。
- **S(C4)** `PropertyGrid`/Inspector (型/ECS/config 反映、型別エディタ) + `AssetBrowser` (TreeView×VFS)。golden。

## WS-M — メニュー/コマンド (ADR-0013)

- `CommandRegistry` { id/タイトル/キーバインド/enablement/run } を単一の真実に。
- `MenuBar` / `CommandPalette` (PopupPlacer 上) / `Toolbar` / Keymap をその純粋ビューとして生成。項目はパス文字列 + コマンド id で寄与登録。
- 共通メニュー (Workbench) + アクティブ `IEditorDocument` の文脈メニュー節を合成。`IEditorDocument` に「メニュー/ツールバー寄与」の面を追加。

## WS-D — 具体エディタ作成 (Workbench 上に載せる)

- **D1** 既存 view を `IEditorDocument` でラップし `DockHost`/`DocumentTabs` に host → **Code / Strudel / Node / RichDoc の 4 種**が並ぶ最小構成。
- **D2** `IDocumentStore` で open/save/ダーティ配線 — 上記が実ファイルを開閉保存。
- **D3** 新ドメインを「構成だけ」で 1 本追加し汎用性を実証 — **Slang シェーダエディタ** or **マテリアルグラフ** (INodeCatalog + プロバイダ)。
- **D4** `PropertyGrid` を使うエディタを 1 本 (0014 の実証) — 例: パーティクル/設定の Inspector 編集。
- **D5 (将来・別 ADR)** ビューポート view + transform gizmo → **2D/3D シーン/レベルエディタ**。payoff 最大だが新ビュー基盤が要るため本タスクのスコープ外。

## ドッグフード: Gallery 統合

- `GalleryApp` を Workbench で再構築。**既定レイアウトを現 chrome (サイドバー｜プレビュー｜下ペイン｜右パネル) の再現**にして golden 中立で移行 (ADR-0007 の「既存挙動を同等に写して diff 0」と同じ手口)。
- プレビュー/Docs/Knobs/Console が「ドックされたパネル」になる。docs をタブで開ける。

## 検証 (各ワークストリーム共通、README の完了の定義に従う)

- `dotnet build` / `dotnet test` (新規ロジックは GPU 不要の単体テスト、特に純ロジックの Workspace/DockTree/CommandRegistry/RichText geometry)。
- `dotnet run --project src/Luxel.Gallery -- vk e2e` diff 0 (意図差分のみ `--update`)。Gallery 移行と文書レンダラ差し替えは golden 面積が広い — 既定レイアウト再現 + 意図差分の切り分けを厳守。
- 起動時デッドリンク検証 (docs 移行後に `story:`/`#アンカー` が壊れていないこと)。
- 仕様は完了時に Gallery の Docs (Docs/Editor 等) と ADR へ現在形で記述。

## スコープ外 / 将来

- D5 のビューポート系 (シーン/レベル/アニメタイムライン) は独立 ADR。
- 排他モード IME 候補 (ADR-0008 / ToDo 24) は本タスクと独立。
- マルチ OS ウインドウへの tear-off (フローティングを別窓に) は任意の後段能力。
