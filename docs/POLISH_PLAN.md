# 品質の上積み計画 (QP: Quality Polish)

目標: 「2D UI としてあとは何が必要か」リストの 7 番 (D&D / スクロール慣性 / トランジション) と、
**コントロールへの適切なアニメーション付与**。機能は増やさず「触ったときの質感」を上げる。

## 設計原則 (全 M 共通)

- **アニメは変化時のみ、初期状態は瞬時** — Realize 直後は必ず静止値 (`AnimatedValue` の初回 Set は
  instant)。snap は非対話なので golden が揺れない (warmup 8 ステップは変更しない)。
- **状態強制 (hover 等) で走るフェードは 100ms 以下** — snap の warmup 133ms 内に静定する。
- **静定後に dirty を残さない** — アニメ完了後は signal 書き込みゼロ (アイドルで再描画 0)。
- 動きは全て transform / opacity / color の部分更新に乗せる (IC の恩恵をそのまま受ける)。
- ゲート: テスト + snap 50/50 ピクセル不変 (vk/dx) + 実窓 E2E (中間フレームのスクリーンショット)。

## QP-M1: モーション基盤

- `Luxel.UI/Motion.cs`: `Easing` (Linear/OutCubic/InOutCubic) +
  **`AnimatedValue`** — 目標値へ時間ベースで遷移する signal 裏打ちの値。
  `ctx.Animated(duration, ease)` で生成 (AddAnimation 常駐)、`Set(target)` で遷移開始、
  `Value` は tracked 読み (effect が transform/色に束縛)。初回 Set は instant。
  静止中は signal を書かない。`Motion.LerpColor(a, b, t)` (RGBA バイト lerp)。
- Accordion のアドホック指数平滑を `AnimatedValue` に置換 (挙動同等)。

## QP-M2: コントロールへの適用

| コントロール | アニメーション |
|---|---|
| Switch | つまみスライド (OutCubic 140ms) + トラック色クロスフェード |
| SegmentedControl / Tabs / RadioGroup | 選択ハイライトのスライド (OutCubic 160ms) |
| ListView | 選択ハイライトの移動 (OutCubic 120ms) |
| Dialog | 開閉フェード + スケール 0.96→1 (180ms) + scrim フェード |
| Drawer | 端からスライドイン (OutCubic 220ms) + scrim フェード |
| Toast | 下からスライド + フェード (180ms) |
| Dropdown / Select / Tooltip | フェード + 6px ドロップ (120ms) |
| Button / MenuRow | hover 色フェード (80ms — 状態強制の snap 静定内) |
| FocusRing | フェードイン (100ms) |

- オーバーレイは `holder.Visible` の即切替 → 「開 = Visible+アニメ開始 / 閉 = アニメ完了後に Visible=false」。
  opacity は EffectiveOpacity (親×自分) でサブツリーに継承される — holder 1 ノードのフェードで全体が消える。

## QP-M3: スクロール慣性 / スムーズスクロール

- ホイール 1 ノッチの瞬間ジャンプ → **目標オフセットへの平滑追従** (指数平滑、時定数 ~80ms)。
  連続ノッチは目標へ加算 (慣性の体感)。サムドラッグは従来どおり即時 (直接操作)。
- 対象: ScrollViewer / ListView。golden は静止位置のみなので不変。

## QP-M4: アプリ内 D&D + ListView 並べ替え

- `UiHost.BeginDrag(payload, ghost)` + `HitTarget.OnDragOver/OnDrop` — 既存の onDragStart/Drag/DragEnd
  配線の上に「ペイロード付きドラッグ」を重ねる。ゴーストは canvas 直下 (Z=4000) にポインタ追従。
- デモ: ListView 行の D&D 並べ替え (`AllowReorder`) + ドロップ位置インジケータ。
- OS ファイルドロップ (WM_DROPFILES) は任意の後続 (欲しくなったら)。

## 進捗

**QP-M1〜M4 全完了 (2026-07-04)** — テスト 390、snap 51/51 (vk/dx ピクセル不変、ListView/Reorder 追加)、実窓 E2E 済み。

- QP-M1: `Motion.cs` (Easing/AnimatedValue/`ctx.Animated()`/`Motion.LerpColor`)。Accordion 移行。
  MotionTests +6。**規約: `Set` は effect 内から呼んでよい (signal を書かない) が、
  `Set(…, instant: true)` は初回以外 signal を書くので effect 内から呼ばない** (drag ハンドラ等から)。
- QP-M2: Switch (スライド+色クロスフェード) / Segmented / Tabs (下線は位置と幅を別 AnimatedValue) /
  RadioGroup / ListView 選択 / Button hover (Normal↔Hover のみ lerp、押下/無効は即時) / MenuRow hover /
  FocusRing フェード / オーバーレイ開閉 (RealizeOverlays 一箇所で配置別 — Center=スケール、
  Edge=スライド、Toast=浮上、Below/Above=6px ドロップ。閉アニメ完了後に Visible=false)。
  E2E: Dialog 開閉の中間フレーム (半透明+縮小)、Drawer スライド中間を実窓捕獲。
- QP-M3: ScrollViewer/ListView のホイールを「目標 (_offset) + 表示 (shown) の平滑追従 (120ms)」に。
  サムドラッグは instant フラグで即時。**仮想化の再バインド/クリック判定/ハイライトは shown 基準**
  (滑走中に行が欠けない)。bench --wheel で再構築 0% のまま。
- QP-M4: `UiHost.BeginDrag(payload, ghost)` (ゴースト Z=4000 追従) +
  `HitTarget.OnDrop/OnDropMove/OnDropHover/AcceptsDrop` + `UiBuildContext.Host`。
  ListView `AllowReorder`+`OnReorder(from, to)` (4px 昇格でクリックと共存、挿入インジケータ付き)。
  ListView/Reorder ストーリー追加 (golden vk/dx ハッシュ一致)。E2E: ゴースト+インジケータ+並べ替え確認。
  OS ファイルドロップ (WM_DROPFILES) は未着手 (欲しくなったら)。
