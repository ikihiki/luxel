# Luxel.Gallery ネイティブ化プラン (HTML → UI システム自身で構築)

**status: 実装完了 (2026-07-03)** — IMG-M1/M2, NG-M1〜M4 すべて実装・E2E 検証済み。
実装メモ: knobs/props パネルはストーリー毎に変わるため「選択時のみ chrome を SetRoot 再構築」の
ハイブリッドに変更 (SurfaceView は同一インスタンス再利用でストーリー状態は生存)。
ストーリー実寸は SurfaceView の論理サイズ変更 (子 host Resize) で実現、サイズプリセット Select は不要になり省略。
状態強制/prop 編集は「Effect 内はフラグ/キューのみ、適用はフレームループ」パターン必須 (下記の罠参照)。

2026-07-03。gallery.html (ブラウザ) を廃止し、ギャラリーの UI そのものを Luxel.Controls で構築する
(ドッグフーディング)。実ウィンドウは先日整備したマルチウィンドウ基盤 (WindowSystem / WindowManager /
UiContent) に載せ、AI 検証は DevTools 統合リモート (/windows /winframe /trees /cmd) をそのまま使う。
**前提作業として 2D ラスタライザ/UI システムに Image プリミティブを追加し、ストーリーは iframe 相当
(別キャンバスに描いた結果の埋め込み) でプレビューする** (ユーザー指定)。

## 方針の要点

### 0. Image プリミティブ (IMG-M1) — 2D ラスタライザへの追加

「別バッファに描かれた RGBA を矩形にサンプリングして合成する」パスを新設する。
既存機構に素直に載る:
- **ソース = bindless バッファ** (framebuffer は既に `RasterArgs.FbIndex` の bindless バッファ書込なので、
  読み側も同じ `g_buffers[]` を index 参照するだけ。テクスチャヒープは使わない)。
- **GpuPath (64B) の空きパディングがちょうど 4 uint**: `SrcIndex, SrcStride, SrcW, SrcH` を格納。
  `Kind=2 (image)`、矩形パス (4 セグメント) の巻き数で AA/クリップは fill と共通。
  UV = (ローカル座標 - BMin) / (BMax - BMin) — シェーダで 2x3 affine の逆変換 (安価)。
- **合成**: 子キャンバスは `BgMode=1` (premultiplied RGBA) で描き、image パスは premultiplied
  合成 over。opacity は style をそのまま乗算 (実効 opacity 継承も自動で効く)。
- サンプリングは v1 = nearest (等倍 iframe 用途ではピクセル一致)。拡縮用 bilinear は任意拡張。
- API: `Scene2D`/`RetainedCanvas` に `ImageRect(rect, srcIndex, stride, w, h)` (UiNode 化、
  ソース差替 = path 書換の部分更新)。
- 検証: 子バッファに fill → 親の ImageRect 経由 → 読み戻しピクセル一致 (vk/dx)、透過合成、クリップ。

### 0.5. SurfaceView widget (IMG-M2) — iframe 相当

`SurfaceView` (Luxel.Controls): **子 RetainedCanvas + 子 UiHost + 専用 framebuffer** を所有し、
自身は親キャンバス上の image ノード 1 つとして描かれる widget。
- レイアウト: 固定 Width/Height (子 UiHost の論理サイズ)。
- 描画: 子キャンバスが dirty のときだけ子を render (`HasPendingChanges`、Gallery/WindowHost と同じ
  スキップ規則)。render は `ctx.AddAnimation` の Tick フックで駆動 (widget に毎フレームフックが
  無いため既存のアニメ経路を使う)。GPU バッファ間で完結し CPU 読み戻しなし。
- 入力ブリッジ: `AddHit(node, (0,0,w,h))` で受けた PointerDown/Drag/Wheel/クリックを**ローカル座標の
  まま子 UiHost へ転送** (transform 追従ヒットテストの成果でローカル座標が直接使える)。
  キー/文字はフォーカス時に転送 (`Focusable`)。
- Dispose: 子 host/canvas/fb の破棄。SetRoot 差替 = ストーリー切替 (親ツリーの再構築不要!)。

### 1. ストーリーの埋め込み = SurfaceView (iframe 方式)

- プレビュー領域 = `SurfaceView` (ストーリーの Width/Height)。中央配置。
- **ストーリー切替は SurfaceView 内の子 SetRoot だけ** — ギャラリー chrome は再構築しない
  (計測済み select 45〜58ms 相当のまま)。knob/状態/テーマ変更も子側の部分更新。
- ストーリーの Build は try/catch し、例外はプレビュー領域に赤字表示 (ギャラリーを巻き込まない)。

### 2. 影響範囲の分離 (chrome とストーリー) — iframe 化で自然に得られるもの

- **状態強制/フォーカス/オーバーレイ/例外がストーリー側 UiHost に閉じる**: 状態 walk は子 host の
  root 起点、Tab 巡回は chrome と混ざらない (転送しない限り)、ストーリーの Dialog/Toast も
  子キャンバス内 = プレビュー矩形にクリップされる。
- **テーマは v1 ではプロセス全体のまま** (UiTheme.Current は global signal — iframe 化でも
  ここは分離されない)。「テーマのサブツリースコープ化 (per-UiHost 化)」は follow-up として明記。

### 3. 実行形態

- `Luxel.Gallery` に **ネイティブ app モードを既定**として追加:
  `dotnet run --project src/Luxel.Gallery -- vk` → 実ウィンドウ 1 枚 (初期 1280x800, リサイズ可)。
  WindowManager + UiContent("gallery") + DebugServer(port) を結線 — AI は既存の
  /windows /winframe?id=1 /trees /cmd (ui ルーティング入力) で操作・検証できる。
  ギャラリー専用の HTTP (GalleryServer/gallery.html) は**パリティ達成後に削除**。
- `-- vk|dx snap [--update]` は現行どおり **GalleryHost (offscreen) を維持** — ヘッドレスで決定的、
  golden 33 枚は不変のはず (ストーリー実体化コードは共有)。

### 4. 画面構成 (3 ペイン)

```
+----------------+------------------------------+----------------+
| 検索 TextField | ツールバー:                   | Knobs (story)  |
| ストーリー一覧  |  [🌙 theme] [size Select]     |  型別エディタ   |
|  (Scroll)      |  [hover][pressed][focus][dis] | States         |
|  Component 見出 |------------------------------| Log (Scroll)   |
|  し + 行 Button | プレビュー (clip Box, 中央)    | Props (M3)     |
+----------------+------------------------------+----------------+
```

- 一覧: `Scroll[VStack[...]]`。Component 見出し + ストーリー行 (Button or MenuRow)。
  **検索フィルタは行の `Visible` トグル** (UI.Each は構築時展開のため、再構築ではなく
  UiNode.Visible の order 部分更新で絞り込む — 既存機能の良い実戦投入)。
- ツールバー: テーマ = Switch/Button (global)、サイズ = Select (story 既定/320x200/480x320/640x400/800x480)、
  状態強制 = トグル Button 4 つ。
- Knobs: StoryContext.Knobs を型でマップ — bool→Switch / int,float→TextField (将来 Slider+range 属性) /
  string→TextField / color→hex TextField (ColorPicker は Phase3 未実装のため) / enum ヒント→Select。
  編集は knob.Set (signal) → 部分更新。
- Log: StoryContext.LogSnapshot を「直近 N 行を join した 1 つの reactive Text」で表示
  (動的子要素の追加は再構築が要るため v1 は joined-text 方式) + clear ボタン。
- Props (M3): ストーリー部分木の DebugChildren/DebugProps を インデント付き行リストで表示、
  行クリックで選択 → 下に DebugProps エディタ (SetDebugProp 直呼び — ui.set の HTTP 経路は不要になり、
  同一プロセスなので SetRoot 再実体化も GalleryApp が自分で行う)。TreeView コントロールは作らず
  Scroll+行で済ませる (本物の TreeView 化は Phase3 候補)。

### 5. 新規/変更コード

- `src/Luxel.Gallery/App/GalleryApp.cs` — ルート UI 構築 + ストーリー選択/再構築の司令塔
  (現 GalleryHost の Select/BuildCurrent/SetState 相当をネイティブ UI 側に移植)。
- `src/Luxel.Gallery/App/KnobEditors.cs` — knob 型→widget マップ。
- `Program.cs` — 既定 = ネイティブ app (WindowManager+DebugServer)。snap は現行維持。
- 削除 (M4): `GalleryServer.cs`, `wwwroot/gallery.html`, GalleryHost の HTTP 専用部分
  (SnapshotRgba/Step/Render 等 snap に必要な部分は残す or Snapshot 側へ移す)。

### 6. 検証

- 単体: knob 型マップ、検索フィルタ (Visible)、状態強制の部分木限定。
- E2E (AI, DevTools 経由): /windows→gallery 窓、/trees→"gallery" UI にサイドバー+プレビューが見える、
  /cmd click (ui:"gallery") でストーリー行クリック→プレビュー切替を /winframe PNG で確認、
  knob 編集→反映、状態強制→ピクセル変化、Log 行の出現。
- snap 33/33 (vk/dx) 不変。

## マイルストーン

- **IMG-M1**: 2D ラスタライザに image プリミティブ — GpuPath Kind=2 + SrcIndex/SrcStride/SrcW/SrcH、
  raster2d_fine.slang のサンプリング+premultiplied 合成、Scene2D/RetainedCanvas の ImageRect API。
  vk/dx ピクセル検証 (バッファ間 blit 一致・透過・クリップ・opacity 継承)。
- **IMG-M2**: `SurfaceView` widget (iframe 相当) — 子 canvas/UiHost/fb 所有、dirty 時のみ子 render、
  入力ブリッジ (pointer/wheel/key ローカル転送)、子 SetRoot 差替。サンプル or ストーリーで実証
  (SurfaceView の中で Button/Scroll が動く・Dialog がプレビュー内にクリップされる)。
- **NG-M1**: GalleryApp 骨格 — ウィンドウ + 3 ペイン + ストーリー一覧 (検索=Visible フィルタ) +
  選択で SurfaceView の子 SetRoot 切替。DevTools E2E で切替を確認。
- **NG-M2**: ツールバー (テーマ/サイズ/状態強制=子 host 部分木) + Knobs エディタ + Log パネル。
- **NG-M3**: Props インスペクタ (子ツリー行 + DebugProps エディタ、SetDebugProp 直呼び)。
- **NG-M4**: gallery.html/GalleryServer 削除、snap 回帰 33/33、README/メモリ更新。

## リスク / ドッグフーディングで露見する既知ギャップ

| ギャップ | v1 の扱い | follow-up 候補 |
|---|---|---|
| テーマの部分木スコープ (プレビューのみ切替) | global で許容 (chrome も切替わる) | UiTheme の per-UiHost スコープ化 |
| 動的リスト追加 (Log 行, 一覧の増減) | joined-text / Visible フィルタ | keyed reconciler |
| ColorPicker / TreeView / SplitPane 不在 | hex TextField / 行リスト / 固定幅 | Controls Phase3 |
| widget に毎フレームフックが無い (子 render 駆動) | AddAnimation 経由で許容 | Widget.Tick 正式化 |
| image サンプリングが nearest のみ | 等倍 iframe では十分 | bilinear + 任意テクスチャソース (g_textures) |
| フォーカス巡回とキー転送の設計 | フォーカス時のみ子へ転送 | Focusables のスコープ化 |

## 将来拡張 (Image の派生価値)

- 画像ファイル/テクスチャ (g_textures) ソース対応 → Avatar 写真、glTF/3D サンプルの UI 内表示
- SurfaceView の拡縮表示 (bilinear) → ズーム付きプレビュー、ミニマップ
- WindowContent への流用 → 1 ウィンドウに複数 UiHost 合成 (以前見送った合成が Image で可能になる)
