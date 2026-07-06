# 18 — スプライトアトラス + タイルマップ (2D)

## 概要

2D ゲーム制作の主要素材である ①スプライトアトラス (1 テクスチャに複数スプライトを詰めて UV 矩形で参照) と ②タイルマップ (グリッド状のタイル描画 + 衝突グリッド) を実装する。**描画プリミティブは既に揃っている** — GpuPath の Image シェイプ (bindless テクスチャ + UV 矩形: SrcIndex/SrcStride/SrcW/SrcH、[src/Luxel.TwoD/Primitives.cs](../src/Luxel.TwoD/Primitives.cs)) がサブ矩形サンプリングをサポートしており、その上のデータ層とバッチングが無いだけ。

## 背景と現状

- **Image シェイプ**: PaintKind.Image で bindless バッファをソースに UV 矩形描画可 — アトラス参照の GPU 側はこれで完結。
- **画像ロード**: Luxel.Imaging (ImageSharpDecoder) + リソース DAG (型,uri) + AssetsGpu (GPU アップロード) が既存。
- **RetainedCanvas**: 移動 = Transform 書込のみ、内容差し替え = ReserveContent + Content 書込 (パーティクル/HUD の前例)。タイルマップのチャンク描画はこの上に組む。
- **Skia CPU バックエンドは Image シェイプ非対応** (既知) — **headless 単体テストで絵の検証はできない**。テストはデータ/UV 計算で書き、絵は e2e golden (GPU) で確認する。
- 物理 (衝突) は Luxel.Physics (Bepu, 3D) — 2D 物理は無いので、タイル衝突は**物理エンジンに依存しない AABB グリッドクエリ**として提供する (2D 物理はロードマップ上も別プロジェクト扱い)。

## 実装方針

### 1. スプライトアトラス

- **データ**: `SpriteAtlas` = テクスチャ参照 + `名前 → (px 矩形, ピボット)` の辞書。定義は JSON:
  ```json
  { "texture": "sprites.png", "sprites": { "player_idle_0": { "x":0, "y":0, "w":32, "h":32, "px":16, "py":32 } } }
  ```
- **ロード**: リソース DAG に (SpriteAtlas, uri) ノード + JSON パースステップ (source:FileSource → Json → SpriteAtlas。テクスチャは既存の画像パイプラインへの依存キー)。
- **パッカーは作らない (v1)**: 無料ツール (TexturePacker free / ftpack 等) や手作業タイルシートの JSON を読む側に徹する。自動パックは将来 (必要ならビルド時リソースステップとして)。
- **描画口**: `Scene2D.DrawSprite(atlas, name, x, y, scale, ...)` 即時 + RetainedCanvas 用に「スプライト 1 枚 = Image パス 1 本」のヘルパ。アニメ (フレーム列) は `SpriteAnimation` (名前プレフィクス + fps → 現フレーム名) の小さなユーティリティ。

### 2. タイルマップ

- **データ**: `TileSet` = SpriteAtlas + タイルサイズ (+ タイル id → スプライト名/衝突フラグ)。`TileMap` = int グリッド (幅×高さ、0 = 空) + TileSet。直交正方タイルのみ (isometric/hex は将来)。
- **描画**: チャンク分割 (例 32×32 タイル / チャンク) — 1 チャンク = UiNode 1 個に Image パス列を焼く。静的なチャンクは一度構築したら不変 (Transform のみ = スクロールはカメラ側)。`SetTile(x,y,id)` は該当チャンクだけ Content 再構築。**可視チャンクのみ実体化** (カメラ矩形との交差) で大マップに耐える。
- **衝突**: `TileMap.QueryAabb(rect) → タイル列挙` と `Sweep(aabb, delta) → 移動可能量` の 2 API (プラットフォーマーの定番)。物理エンジン非依存の純ロジック — テストが書きやすい。Bepu 連携 (衝突タイルを static Box 群として登録) はオプションのブリッジとして後付け可能な設計に。
- **マップ定義の読み込み**: v1 は CSV/JSON の自前形式。**Tiled (.tmj = JSON) の最小 import** (タイルレイヤ 1 枚 + tileset 参照のみ) は価値が高いので、余力があれば入れる (外部エディタが使える = マップエディタを作らなくて済む)。

### 3. テスト + デモ + Docs

- 単体テスト (GPU 不要): アトラス JSON → UV 矩形の計算 / SpriteAnimation のフレーム進行 (固定 dt) / TileMap の QueryAabb・Sweep (境界、角、ゼロ移動) / SetTile のチャンク dirty 判定 / 可視チャンク選択。
- e2e デモ: 「Demos/TwoD/Sprites」(アトラスから複数スプライト + アニメ 1 体、play: Step(n) → Snap でフレームが進んだ絵) / 「Demos/TwoD/Tilemap」(小さなマップ + カメラスクロール、play: Key/Drag でスクロール → Snap)。**アセット**: 数十 KB の CC0 タイルセット (Kenney 等) を assets/ に追加 (ライセンス表記を添える)。
- Docs/TwoD にアトラス/タイルマップ節を追加。

## 作業ステップ

1. SpriteAtlas (JSON + リソースステップ) + UV テスト。
2. 描画ヘルパ + SpriteAnimation + デモストーリー (golden は GPU e2e)。
3. TileMap データ + チャンク描画 + 可視チャンク管理。
4. 衝突 (QueryAabb/Sweep) + テスト。
5. (余力) Tiled 最小 import。
6. Docs + golden 更新。

## 罠・注意

- **Skia CPU に Image シェイプが無い**: UiHost headless テストでスプライトを含む絵を検証しない (プレースホルダにもならず例外の可能性 — 挙動を確認して、未対応なら Skia 側は「Image パスをスキップ」の安全動作にしておくと headless E2E が守られる)。
- bindless テクスチャのアップロードは既存 AssetsGpu 経路を使う (行 256B 整列等の罠は既存コードが吸収)。
- チャンク Content の容量見積り: 1 タイル = Image パス 1 本。ReserveContent の過小確保は Rebuild を誘発 (統計 LastSegmentBytesWritten で確認)。
- ピクセルアート想定のサンプリング (nearest) が Image シェイプで選べるか確認 — 選べなければ v1 は linear のみで割り切り、Docs に注記 (シェーダ側 sampler の話になるので改修は別判断)。
- golden 用アセットは assets/ に置き goldens/ と分離 (既存規約)。

## スコープ外

- 自動アトラスパッカー、isometric/hex、Tiled のオブジェクトレイヤ/無限マップ、2D 専用物理エンジン、マップエディタ内製。
