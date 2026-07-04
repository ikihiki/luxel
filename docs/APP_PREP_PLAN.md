# アプリ準備計画 (AP: App Prep)

進捗: **AP-M1〜M5 全完了** (2026-07) — テスト 384、snap 50/50 (vk/dx)、実窓 E2E 済み。

- AP-M5 実装メモ: `KeyGesture(Key, Ctrl, Shift, Alt)` (Luxel.UI) + `UiHost.RegisterShortcut/UnregisterShortcut`。
  KeyDown の配送順: Esc (オーバーレイ) → Tab (フォーカス) → フォーカス中コントロール →
  **未消費のときだけ**ショートカット。Gallery は Ctrl+D = テーマ切替を登録 (デモ兼 E2E)。
  E2E: フィールドフォーカス中の Ctrl+A は選択として消費 (奪わない)、同フォーカス中の Ctrl+D は
  TextField が消費しないのでショートカット発火 (落ちてくる) — 両方向を実窓確認。

- AP-M1 実装メモ: `UiError.Report` + `ErrorWidget` (赤枠縮退)。捕捉点は計画どおり
  (Effect / CompositeControl.BuildSafe / UiHost 入力 Guard / Tick アニメ / EngineCommands.Drain)。
- AP-M2 実装メモ: `CursorKind` は Luxel core (`WindowSystem.cs`)、`HitTarget.Cursor/CursorFunc/OnContext`
  (OnContext はローカル座標)。WM_SETCURSOR (HTCLIENT のみ)。右クリックは WindowHost の MouseUp b==1 →
  `IWindowContent.ContextClick`。メニューは `ContextMenu.Open/OpenForEditor/Close` (Luxel.Controls) —
  オーバーレイ機構ではなく **canvas 直下へ任意タイミング実体化** (全面ディスミスヒット先登録 = 行が前面勝ち)。
  エディタメニューは `FocusTarget.OnKey(Ctrl+X/C/V/A)` の再利用。SurfaceView は cursorFunc/onContext を子へ転送。
  - E2E で発見した修正: **UiHost.FocusTo は既フォーカス対象への OnFocus(true) を再発火しない**。
    再発火すると TextField のフォーカス時初期化 (`_ed.End(false)` = 選択クリア) が走り、
    右クリック→コピーの間に選択が消える (コピーだけ空振りする、というバグだった)。
- AP-M3 実装メモ: ListView は**可視行数 + 1 の固定ノードプール**を実体化し、スクロール/SetItems は
  再バインド (Content 再代入 = in-place)。SetItems の MarkNeedsRealize も不要になった (構造 dirty ゼロ)。
  **鍵 = `UiNode.ReserveContent(segs, paths)`** (新 API): 行毎の glyph 数の揺れが既定スラック (+25%)
  を超えるとフル再構築が頻発する (10 万行スクロールで 68%) ため、最長行のエンコードサイズ
  (`Scene2D.CountEncoded`、新 API) をプール全ノードの最低予約にする → **スクロール中の再構築 0%**。
  ゲート: ListView/Huge ストーリー (10 万行、golden は vk/dx ハッシュ一致)、
  bench `--wheel` ドライバ新設 (300f 連続スクロールで rebuilds 0)、実窓 E2E (ホイール/行選択/サムジャンプ)。
- AP-M4 実装メモ: `VectorFont` に (glyph, px, FlattenTolerance) → 平坦化済み輪郭点列のキャッシュ。
  `Scene2D.ExportContours` (キャッシュ構築) + `AppendClosedContours` (平行移動コピー、平坦化スキップ) 新設。
  **snap 50/50 ピクセル完全一致** (平坦化→平行移動の FP 差は 4x4 SS を揺らさなかった)。
  A/B (NOGFX_NO_GLYPH_CACHE=1 で無効化可): タイプ alloc 2006→1817 KB/frame (−9%)、
  スクロール再バインド 800→763 KB/frame (−5%)。**計測の教訓: ベジェ平坦化は alloc の主犯ではなかった**
  (平坦化は既存 List への追記で、alloc の大半はレイアウト/シェーピング/Scene オブジェクト churn) —
  さらなる削減は pooling/arena 化の別マイルストーン。タイプ中 60.0fps は維持 (実窓プロファイル)。

目標: strudel 型アプリを実際に組み始める前に、2D UI システムに残る**実用上の穴** 5 つを塞ぐ。
いずれも既存基盤 (dirty 伝播 / IC 増分更新 / オーバーレイ / クリップボード / WindowManager) の上に
素直に乗る — アーキテクチャ変更はない。

- **AP-M1: エラー境界** — ライブコーディングの前提。ユーザーコード (Build/Effect/入力ハンドラ/
  アニメーション/コマンド) の例外でアプリを落とさない。
  - `UiError.Report(ex, context)` (Luxel.UI): EngineDiagnostics (`Luxel.Input` の "error" ログ) +
    Console へ集約。
  - 捕捉点: ReactiveEffect.Execute (握って報告、effect は生かす — 次の変化で再試行)、
    CompositeControl の Build (失敗 → 赤枠 + 例外メッセージの ErrorWidget に差し替え、
    次の Rebuild で再試行)、UiHost の入力配送 (OnKey/OnText/OnCompose/クリック)、
    UiHost.Tick のアニメーション (throw したものは除去)、EngineCommands.Drain。
- **AP-M2: カーソル形状 + 右クリック** — エディタの基本 UX。
  - `CursorKind {Arrow, IBeam, Hand, ResizeH, ResizeV}`、`HitTarget.Cursor`。
    UiHost.PointerMove が hover 先のカーソルを `CurrentCursor` に記録 →
    WindowHost が Win32 SetCursor (WM_SETCURSOR)。テキスト編集系 = IBeam、Splitter = Resize。
  - 右ボタン配送 (現在 b==0 のみ) + `HitTarget.OnContextMenu(x,y)` +
    エディタ標準メニュー (切り取り/コピー/貼り付け/すべて選択 — 既存クリップボード配線を再利用、
    表示は Dropdown/MenuRow のオーバーレイをポインタ位置に)。
- **AP-M3: ListView 仮想化** — 可視行数分のノードだけ実体化し、スクロールで**再バインド**
  (Content 差し替え = IC の in-place が効く)。ノード数はスクロールで不変 = 構造 dirty なし。
  API (SetItems/OnSelect) は不変。10 万行で snap/操作が破綻しないこと。
- **AP-M4: グリフ線分キャッシュ** — タイプ時 CPU/GC の次のボトルネック。
  VectorFont に (glyph, px) → 平坦化済み輪郭点列のキャッシュ。AppendText/TextLayout.Draw は
  ベジェ平坦化をスキップして点列を平行移動コピー。bench の managed alloc で効果測定。
- **AP-M5: ショートカットシステム** — アプリ全域のキーマップ。
  `KeyGesture(Key, ctrl, shift, alt)` → Action の登録簿を UiHost に。配送順は
  「フォーカス中コントロールが未消費のときだけ」 (エディタのタイプ/Ctrl+B を奪わない)。

ゲート (全 M 共通): テスト + snap 49/49 ピクセル不変 (仮想化 ListView も golden 一致が証明) +
実窓 E2E。AP-M4 は bench で alloc 削減を数値確認。
