# GS: 2D/3D 描画結果のストーリー化 (2026-07-04 完了)

2D システム (Scene2D) と 3D/GPU システム (graphics PSO + offscreen) の描画結果を
Storybook のストーリー/docs に載せる基盤。**「描画結果を Widget にする」の一点に絞り**、
Story 属性/署名/knobs/snap/MDX の資産をそのまま再利用する。

## 書き方

```csharp
// 2D 静的
[Story("2D/Shapes")]
public static Widget Shapes() => Frame(Canvas2D(384, 220, draw: s =>
    s.FillRoundedRect(Tw.Blue500, 16, 16, 140, 90, 14)));

// 2D アニメ (t = Tick の累積秒。knob も普通に効く)
[Story("2D/Orbit")]
public static Widget Orbit(StoryContext ctx)
{
    Signal<float> speed = ctx.Signal("speed", 1f);
    return Frame(Canvas2D(384, 220, animate: (s, t) =>
        s.FillCircle(Tw.Blue500, cx + MathF.Cos(t * speed.Value) * 80, ..., 12)));
}

// 3D: IGpuScene (offscreen 自前レンダ) を GpuView で合成
[Story("3D/Triangle")]
public static Widget Triangle() => Frame(GpuView(320, 240, new TriangleScene()));

// リソースは ctx.Resources (ホスト所有 ResourceSystem) から
[Story("3D/TexturedQuad")]
public static Widget TexturedQuad(StoryContext ctx)
    => Frame(GpuView(320, 240, new TexturedScene(ctx.Resources), animated: false));
```

## 部品

- **Canvas2D** (Luxel.Controls): Scene2D を直接描く widget。静的 `draw:` / 毎フレーム `animate:(s,t)`。
  1 ノード (Content=Scene2D) なのでクリップ/transform/MDX 埋め込みが効く。
- **UiNode.ContentColors** (Luxel.TwoD, 新規): 保持型キャンバスの「1 ノード 1 色」を opt-out し、
  Scene2D の**形状ごとの色を保持**する (PathEncoder の styles を捨てずスロット割当。
  in-place 更新にも対応 — PathCapacity 分のスタイルスロットを対で予約)。
  制約: ノードの Color/Opacity 実行時変更は content 色に反映されない (実効 opacity 焼き込み)。
- **IGpuScene / GpuView** (Luxel.Controls): offscreen へ自前 GPU レンダ → 結果 (RGBA8 bindless
  バッファ) を image プリミティブで**ゼロコピー合成** (CPU 読み戻しなし、同一 queue submit 順で同期)。
  - `Init(device, w, h)` は **Dispose 後に再度呼ばれ得る** (リサイズ等の再実体化) — 全リソースを
    作り直せること。`Render(time)` の time は累積秒 (**wall-clock 禁止** — snap の決定性)。
  - 寿命は realize スコープ毎の **SceneGuard**: スコープ破棄で Dispose、再実体化で再 Init。
    once 所有だと SurfaceView.SetContent (リサイズ→旧ルート再実体化) で破棄済みシーンを
    Render して NRE になる (実 E2E で検出・修正済み)。
  - D3D12 の CopyTextureToBuffer は行 256B 整列 — ターゲット幅は 64 の倍数を推奨。
- **StoryContext.Resources** (ユーザー決定): ホスト所有の ResourceSystem を配布する窓口
  (Signal/Log と同じ「ホスト設備を借りる」形)。GalleryHost/GalleryApp が生成・毎フレーム
  Pump・Dispose。キャッシュはストーリー横断共有、ハンドルはシーンが Dispose (refcount)。
  **初回ロードの publish は Pump 不要** (直接反映) なので Init で Ready.Wait してよい =
  snap も決定的。ControlStories の静的 Lazy ResourceSystem は撤去し ctx.Resources へ移行。

## ゲート

- テスト 409、snap 58/58 (vk/dx — 2D/Shapes・2D/Orbit・3D/Triangle・3D/TexturedQuad 追加。
  ContentColors 変更の既存 54 不変は dx の verify-before-update で確認)
- 実窓 E2E: Triangle/Orbit がアニメ (連続フレーム差分)、speed knob 反映、ストーリー往復
  (TexturedQuad→Shapes→TexturedQuad) で story error 0 件 (SceneGuard の再 Init 確認)

## 既知の制約 (v1)

- Canvas2D の animate は毎フレーム再エンコード (スラック内なら in-place)。巨大シーンは
  ReserveContent 相当の予約を検討
- GpuScene の Render は SubmitAndWait (同期)。ストーリー用途では十分、非同期化は将来
- 部分再実体化 (MarkNeedsRealize) で「破棄→新 realize→旧スコープ破棄」の順になる経路が
  もしあれば SceneGuard が新シーンを殺す可能性 (GpuView は自発しないため現状未発生)
