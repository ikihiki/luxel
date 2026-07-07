# 21 — DevTools のゲーム規模対応

## 概要

DevTools を capstone ゲーム 2 本 ([19](19-standalone-game-shipping.md) / [20](20-game2-3d-shooting-range.md)) の開発に耐える観測・操作ツールへ拡張する。現状はデモストーリー規模 (数十エンティティ) 前提の全量スナップショット方式で、ゲーム規模 (数百エンティティ + 物理 + パーティクル) では ①ECS インスペクタがスケールしない ②物理/カメラ/パーティクルの可視化がない ③FixedUpdate/タイムスケール非対応 ④ライブビューがポーリング駆動でゲームの fps に届かない、が問題になる。

[11](11-scripting-debug-tools.md) (Console タブ・入力リプレイ・外部デバッガ) とは独立 — 11 はスクリプティングのデバッグ層、本タスクはゲームランタイムの観測。重複しない。

## 背景と現状 (調査済み 2026-07-06)

- **フロントエンドは 2 つある (二正面)**:
  - **ブラウザ版**: `DebugServer` (HTTP loopback) + `wwwroot/index.html` (埋め込みリソース、単一ページ)。
  - **内蔵版**: [Luxel.DevTools.App](../src/Luxel.DevTools.App/DevToolsApp.cs) — `DevToolsApp.Launch(createDevice, listener, commands)` で**別 STA スレッドの UI 島** (自前 GpuDevice + WindowSystem + テーマ signal) としてネイティブウィンドウを起動。Luxel.UI/Controls 製 ([DevToolsUi.cs](../src/Luxel.DevTools.App/DevToolsUi.cs): Frame/Trees/Log/Stat/ECS/Res/Surf/Input/Audio/GPU/Graph パネル + Sparkline ダッシュボード)。データは **HTTP を介さず DevToolsListener を島スレッドから直接 rev ポーリング** (~160ms 毎、Frame は毎島フレーム)、操作は `EngineCommands.Enqueue` のみ。E2E 用の第二 DebugServer も持てる。
- **アーキテクチャ**: DiagnosticListener + EngineCommands の疎結合 (エンジンコアは DevTools を参照しない)。**両フロントエンドが同じ DevToolsListener (読み) + EngineCommands (書き) を共有する**ため、データ/プロトコル層の変更は一度で両方に効く。UI 表示だけが二重。購読者がいなければ emit は 0 コスト (`IsEnabled` 判定)。
- **タブ**: Home (FPS/Entity/Memory) / Perf (Phase×System 時間) / ECS / Surfaces / Input / Audio / UI Tree / RenderGraph / Resources / Console。
- **ECS スナップショット** ([Scene.cs](../src/Luxel.Framework/Scene.cs) `EmitEcsSnapshot`, 30 フレーム毎): **全エンティティ × 全コンポーネントを System.Text.Json で全量シリアライズ**。目安 100 entity ≈ 150KB/回。数百〜数千 entity では生成・転送・ブラウザ側 DOM が破綻する。値編集は `ecs.set` op (reflection で path walk) 実装済み。
- **pause/step**: `engine.pause` / `engine.resume` / `engine.step` 実装済み (`_paused` + `_stepRequests`)。**タイムスケールは未実装**。
- **Perf**: `DiagPhaseTiming[6]` が 6 フェーズ固定 — [14](14-framework-fixedupdate.md) の FixedUpdate 追加で崩れる。system 単位計測は opt-in (`EnableSystemPerfMonitor`)。
- **接続性**: Gallery 専用ではない — `new DevToolsListener(cmds)` + `new DebugServer(listener, port, windows)` の数行で任意アプリに統合可 ([Program.cs:91](../src/Luxel.Gallery/Program.cs) が手本)。
- **フレーム画像**: `DiagFrame` は RetainedCanvas.Render 経由 — **3D (RenderGraph) の絵が乗るかは未確認** (調査項目)。
- **フレーム配信経路** ([AppWindow.cs](../src/Luxel.Platform/AppWindow.cs) `EmitFrame` → [DebugServer.cs](../src/Luxel.DevTools/DebugServer.cs) `/frame`): framebuffer はホスト可視バッファなので GPU 読み戻しは行コピーのみ。配信は**生 RGBA の HTTP ポーリング** (rev 不変なら 304)。PNG は `&format=png` の単発用のみでライブ経路には無い。課題: ①`EmitFrame` が購読中毎フレーム `new byte[w*h*4]` (720p/60fps で約 220MB/s の GC 割り当て) ②ポーリング駆動なのでブラウザ側の実効 fps がゲーム fps に届かない。

## 実装方針

**大原則: 変更はデータ層 (emit / DevToolsListener / Protocol) に寄せ、ブラウザ版と内蔵版の両 UI から同じデータを表示する**。UI 実装は二重になるが最小に保つ (A のサマリ/詳細切替と C の key-value 追加程度)。内蔵版 UI を触るときは DevToolsUi の島テーマ規約 (グローバル UiTheme を閉包購読する Kit 複合ヘルパ禁止、`_theme` 参照) に従う。

### A. ECS インスペクタのスケール対応 (最優先)

「一覧は軽量、詳細は選択したものだけ」に切り替える:

1. **一覧スナップショット**: `DiagEcsSummary` = entityId + 表示名 + アーキタイプ (コンポーネント型名の列) だけ。JSON 値は含めない。30f 毎維持。
2. **詳細はオンデマンド**: 選択エンティティのみ `ecs.inspect` op → そのエンティティだけ全量 JSON を emit (毎スナップショット周期で追従更新)。既存の全量経路は「エンティティ数が閾値以下なら従来通り」のフォールバックで残して良い。
3. **サーバ側フィルタ**: `ecs.filter` op (名前部分一致 / コンポーネント型)。フィルタ結果だけ一覧に載せる — ブラウザ側 DOM 爆発と内蔵版 ListView/JsonPanel の行数爆発の両方を抑止 (フィルタは emit 側で効かせるのが二正面で一番安い)。
4. **表示名**: `DebugName` コンポーネント (string 1 個、`[SaveIgnore]` 相当の扱い) を Luxel.Ecs に追加し、ゲームが敵/弾/的に名前を付けられるように。無ければ従来通り Id 表示。
5. パーティクル ([16](16-particle-system.md)) は ECS エンティティではない (SoA 内部) — ECS タブに出ない前提で良い (C のゲーム統計で Alive 数を出す)。

### B. デバッグ描画レイヤ (gizmos)

ブラウザではなく**ゲーム画面上のオーバーレイ**として描く (RetainedCanvas の専用ノード群 or 専用 canvas、Z 最前面)。DevTools からトグル (`gizmo.enable {kind}`)、コードからも `DebugDraw.Line/Rect/Circle/Text` の即時 API:

- **物理** ([20](20-game2-3d-shooting-range.md) でほぼ必須): コライダーワイヤ (箱/球/ConvexHull/Mesh は AABB で可)、接触点 ([04](04-physics-contact-events.md) のイベントを購読)、トリガーボリューム、CCD on の剛体の色分け。3D → 2D 投影はゲームのカメラ (viewProj) で行う — DebugDraw は「ワールド → スクリーンの変換 delegate」を受け取る設計にして 2D/3D 両対応。
- **2D ゲーム** ([19](19-standalone-game-shipping.md)): TileMap の衝突タイル/Sweep 結果、AABB、CameraRig2D のデッドゾーン矩形と WorldBounds。
- **共通**: パーティクルエミッタ位置 + Alive 数、任意のゲーム側 DebugDraw。
- **golden との関係**: gizmo は明示トグルのみで有効化 (既定 off、e2e では触らない)。逆に「gizmo を on にした play」を 1 本だけ golden 化してレイヤ自体の回帰を守る (固定 dt なら決定的)。

### C. ゲーム統計の発信 (カスタム watch)

`EngineDiagnostics.Emit(Custom, ...)` に載る汎用 key-value API を用意し、Home タブに「Game」セクションとして表示:

```csharp
DevStats.Set("score", score);          // 数値/文字列。30f 毎にまとめて emit
DevStats.Set("state", "Playing");
DevStats.Set("particles.alive", ps.Alive);
DevStats.Set("bodies", physicsWorld.BodyCount);
```

スコア・残弾・HP・状態機械の現在状態など、ゲームが 1 行で観測に載せられる口。printf デバッグの受け皿でもある。表示はブラウザ版 Home + **内蔵版 Stat ダッシュボード** (既に flush/engine の key-value を ListView 表示している — 同じ枠に「Game」セクションを足すだけ) の両方。

### D. FixedUpdate / タイムスケール統合

1. **Perf タブの FixedUpdate 対応**: `DiagPhaseTiming[6]` 固定をフェーズ可変長に (14 実装時に壊れないよう先回りするか、14 と同時に)。FixedUpdate の「このフレームのステップ回数 / accumulator 残量 / Alpha / MaxSteps 超過 (捨てた時間)」を DiagPerf に追加 — 14 の「診断イベント」の受け皿を DevTools 側で持つ。
2. **`engine.timescale` op**: dt に係数を掛ける (0.1 でスローモーション、0 は pause と等価にしない — 既存 pause と直交)。実装は GameScene の dt 供給箇所 1 点。**play/e2e は timescale を使わない** (決定性は固定 dt の play が担保、timescale は手動デバッグ専用と明記)。

### E. スタンドアロンゲームへの統合 (両フロントエンド)

1. `LuxelHostBuilder` に `.WithDevTools(...)` 相当のオプトイン (Gallery の Program.cs で手書きしている定型の集約)。**内蔵版とブラウザ版を選べるようにする**: ゲーム引数の案 — `--devtools` = 内蔵ウィンドウ (`DevToolsApp.Launch`、第二 GpuDevice が必要な点はファクトリを渡す)、`--devtools-port <n>` = ブラウザ版 DebugServer、併用可。既定はどちらも off (publish 成果物で勝手に立てない)。
2. wwwroot は埋め込みリソースなので publish 追加作業は無いはず — 19 の publish スモークで `--devtools` (内蔵) / `--devtools-port` (ブラウザ) の両起動を各 1 回確認。内蔵版は Luxel.DevTools.App の参照が publish サイズに乗る — 気になるなら Release ではリンクしない構成も検討 (計測してから判断)。

### F. ライブビューのゲーム fps 化 (目標: ゲームと同等の滑らかさ、両フロントエンド)

**受け入れ条件: 720p/60fps のゲームを内蔵版・ブラウザ版どちらのライブビューでも実効 ~60fps で表示。メインスレッドの追加コストは購読中 < 0.5ms/フレーム、GC 割り当てゼロ。**

1. **割り当て除去 (共通)**: `EmitFrame` の tight コピー先をリングバッファ (2〜3 枚使い回し) に。**所有権はスレッド安全に**: LatestSlot は「最新参照の swap」で、読者が 2 系統 (DebugServer の HTTP スレッド + 内蔵版の島スレッド) いる — 読者が参照中のバッファを書き手が再利用しない規約 (世代カウンタ付きリング or 読者側コピーの明示) を決めてから実装する。
2. **内蔵版の 60fps 化**: 島は毎フレーム LatestSlot を直接読むので転送は既にゼロコスト。ボトルネック候補は島側の ImageView テクスチャアップロードと島ループの `Thread.Sleep(16)` ペーシング — 実測して 60fps に届かなければここを直す (アップロードの差分化 or ペーシング調整)。**内蔵版が最短経路なので、滑らかさ最優先の用途は内蔵版を推奨**と Docs に書く。
3. **ブラウザ版: HTTP ポーリング → WebSocket push**: フレーム確定時に push。**バックプレッシャは latest-wins** — クライアントの受信が遅れたら古いフレームを捨てて最新だけ送る (送信キュー深さ 1)。送信はオフスレッド、メインスレッドは絶対にブロックしない。ブラウザ側は受信 → `ImageData` blit (localhost の生 RGBA 220MB/s は loopback 帯域・canvas 描画とも余裕)。既存の `/frame` ポーリングは互換のため残す (format=png の単発用途)。
4. **(任意) ブラウザ版なめらか優先モード = MJPEG**: 高解像度でも帯域を抑えたい場合のトグル。multipart/x-mixed-replace ならブラウザが `<img>` でネイティブ再生。JPEG エンコードは**オフスレッド** + latest-wins (エンコードが 1 フレームに間に合わなければ間引く)。純 C# エンコーダ (ImageSharp) の速度が 60fps に届くかを最初に計測し、届かなければこのモードは「高解像度時の間引き付き」と割り切る。内蔵版には不要 (in-process なので生 RGBA で足りる)。
5. **やらないと決めたこと**: **GPU ハードウェアエンコード (H.264/MP4) のライブ配信はしない** — 4:2:0 の劣化が検証用途と矛盾、framebuffer が buffer (テクスチャでない) なので interop が必要、vk/dx 二重実装で規律違反、localhost では帯域の利得も薄い (2026-07-06 決定)。「動きを見る (滑らかさ)」は 1〜4 で、「ピクセルを見る (正確さ)」は既存の生 RGBA/PNG 経路で、モードを分けて両立する。
6. **計測**: DevStats/Perf に「配信 fps・ドロップ数・emit 時間」を出し (内蔵版 Stat / ブラウザ版 Home の両方に表示)、受け入れ条件を目視でなく数値で確認。pause 中は rev 不変 = push なし (既存挙動踏襲)。

## 作業ステップ

1. A (スケール対応): サマリ + オンデマンド詳細 + フィルタ + DebugName。単体テスト: サマリ形状、フィルタ一致、`ecs.inspect` の対象限定。1000 entity のダミー World で emit 時間を計測し Docs に記録。
2. C (ゲーム統計) + D2 (timescale): 小さい。C は単体テスト (Set → emit 内容)、timescale は「dt×0.5 で 2 倍フレーム数」のロジックテスト。
3. B (gizmos): DebugDraw コア + 物理/カメラ/タイルの標準 gizmo。デモストーリー「Demos/Framework/Gizmos」(gizmo on の golden 1 本)。
4. D1 (Perf 可変フェーズ + FixedUpdate 統計): [14](14-framework-fixedupdate.md) の実装と同期。
5. E (WithDevTools + 3D フレーム調査): 19 の骨組み publish 検証に合流。
6. F (fps 化): F1 (割り当て除去 — 二読者の所有権設計込み) は単独で先行可 (効果測定つき)。F2 (内蔵版の 60fps 実測・改善) → F3 (ブラウザ版 WebSocket push) → 実機でゲーム fps との一致を確認 → 必要なら F4 (MJPEG)。単体テスト: リング所有権 (読者参照中のバッファを上書きしない)、latest-wins のドロップ挙動。
7. Docs: Docs/Framework (または新 Docs/DevTools ページ) に「ゲームを観測する」節 — タブ一覧・DevStats・gizmo・timescale・ライブビューのモード (滑らかさ/正確さ) の使い方。

capstone との順序: **A/C/E は 19 のゲーム組み上げ前に済ませると開発自体が楽になる**。B の物理 gizmo は 20 の着手前までにあれば良い (04/05 のデバッグで威力を発揮)。

## 罠・注意

- **emit は購読者ゼロなら 0 コストの規律を守る** (`IsEnabled` ガード) — DevStats/gizmo データ収集も同様に。gizmo の DebugDraw 呼び出し自体はゲームコードに残って良いが、off 時に割り当てゼロで抜けること。
- ECS 値編集 (`ecs.set`) の reflection path walk は既存実装を流用 — サマリ化で壊さない (詳細表示中のエンティティにだけ編集 UI を出す)。
- index.html は単一ファイル自己完結 (外部 CDN 不可) — タブ追加もこの流儀で。
- `DebugName` は [15](15-save-load-settings.md) のセーブ対象外規約 (純データだが保存不要) — `[SaveIgnore]` の適用例第 1 号になる。
- pause 中も emit は続く既存挙動 (state 最新化) を維持 — gizmo/統計も pause 中に見えることがデバッグ価値。
- Ecs/Framework の `Phase` 名前衝突 (using alias)。

## 進捗

### 2026-07-06: ステージ ① のうち A / C / D 完了 (データ層 + 両フロントエンド + 単体テスト)

**済 (Q05 の一部)**:
- **A (ECS スケール)**: `DiagEcsSummary` (id + 名前 + アーキタイプ、値なし) を常時 emit、詳細は `ecs.inspect` の選択 entity のみ (未選択かつ ≤64 entity は全量フォールバック)。`ecs.filter` op でサーバ側フィルタ (名前/Id/component 型)。`DebugName` component を Luxel.Ecs に追加。ロジックは `Luxel.Ecs.EcsDiagnostics` (BuildSummary/BuildDetail/FilterMatch) に切り出し単体テスト済み。ブラウザ ECS タブ = サマリ一覧 + クリックで詳細 (右 Details ペイン) + フィルタ、内蔵版 = 一覧/詳細の 2 段パネル。
- **C (DevStats)**: `Luxel.Diagnostics.DevStats.Set(key, value)` (数値/文字列/bool)。購読者ゼロなら `IsEnabled` 判定だけで即 return (ゼロコスト)。30f 毎に `DiagCustom` を emit (`Flush`)。ブラウザ Home に "Game" セクション、内蔵版 Stat に "Game (DevStats)" カード。
- **D2 (timescale)**: `engine.timescale {value}` op で dt に係数 ([0,8] クランプ)。dt 供給の単一点 `FixedTimestep.ScaleDt`。play/e2e は timescale=1 (決定性維持)。
- **D1 (FixedUpdate 統計)**: `DiagPerf.Fixed` = `DiagFixedStep` (このフレームのステップ数 / Alpha / accumulator 残 / 捨てた累計)。Perf タブ (両版) に表示。フェーズ可変長化は Q03 で `DiagPhaseTiming[7]` 済み。
- 付随: `FixedTimestep` を `Luxel.Framework` (net10.0-windows、テスト不可) から Luxel core (net10.0) へ移設し単体テスト可能に。`EcsDiagnostics` は Luxel.Ecs へ (core 参照追加、循環なし)。
- 検証: `dotnet build` OK / `dotnet test` 604 passed / e2e 53 plays passed・golden diff 0 / index.html JS 構文 OK。

### 2026-07-06 (2): DebugDraw コア 完了

**済 (Q05 の一部)**:
- **DebugDraw コア** (`Luxel.TwoD.DebugDraw`, static 即時モード): `Line/Rect/Circle/Text` をワールド空間で溜め、`Flush(Scene2D, WorldToScreen, DebugTextDrawer?)` で最前面オーバーレイへ流す。`WorldToScreen` 委譲で 2D=恒等 / 3D=viewProj の両対応。テキストは `DebugTextDrawer` 委譲で Typography 非依存。カテゴリ (kind) 単位の Enable/Disable、`"all"` で一括、OFF カテゴリはゼロ割り当てで抜ける。`gizmo.enable {kind,on}` コマンドを Scene に登録。
- 配置: Luxel.TwoD (net10.0, テスト可)。Scene2D の `StrokeLine/StrokePolyline` で線・矩形・円を、委譲でテキストを描画。
- golden: **Demos/Framework/Gizmos** (Order 150) — 箱/円/線/ラベル + 無効カテゴリ非描画。単体テスト `DebugDrawTests` 7 件 (ゼロ割り当て / カテゴリ / 投影 / テキスト委譲 / Reset)。
- 検証: build OK / test 611 passed / e2e 54 plays passed・golden diff 0 (新規 Gizmos golden 1 枚のみ) / golden 目視 OK。

### 2026-07-06 (3): ステージ ② — B の 2D gizmo 完了 (Q12)

**済 (Q12)**:
- **`Gizmos2D`** (Luxel.TwoD、DebugDraw コアの上): `TileCollision(map, worldView, color)` = 表示矩形と重なる衝突タイルのワイヤ矩形 /
  `Sweep(map, box, delta, ...)` = 開始 box + Sweep 解決 box / `CameraRig(rig, deadzoneColor, boundsColor)` = 中央デッドゾーン矩形 + WorldBounds。
- **`ParticleGizmos.Emitter(ps, pos, color)`** (Luxel.Particles.TwoD): エミッタに十字+円マーカ + `alive N` ラベル。`ParticleSystem.EmitPosition` 公開。
- 各ヘルパは先頭で `DebugDraw.IsEnabled(kind)` 判定 → **OFF 時は列挙/割り当てゼロ**。カテゴリ: `gizmo.tiles` / `gizmo.camera` / `gizmo.particles`。
- テスト: `DebugDrawTests` に 4 件追記 (同クラス=静的状態を直列化。衝突タイル数=矩形数 / デッドゾーン+境界 / 境界なし / エミッタ 4 コマンド + OFF ゼロ)。計 729 passed。
- golden: **Demos/TwoD/Gizmos2D** (Order 151) — ゲーム画面 (塗り) 下地 + 3 種 gizmo を on にした 1 枚 (Canvas2D=Skia可・決定的、worldToScreen 恒等)。e2e 64/64 diff 0。

**残 (Q05 の残り) — B の物理 gizmo を除き完了**:
- B の機能別 gizmo: **2D (タイル/カメラ/エミッタ) は Q12 で完了**。**物理 gizmo (コライダーワイヤ/接触点/トリガー/CCD 色分け) はステージ③ = Q14** (03/04/05 の実装後、この DebugDraw + Gizmos2D の流儀で載せる)。**21 MD はこのステージ③完了まで残す**。

### 2026-07-07 (3): ステージ ③ — B の物理 gizmo 骨格 (Q14、コライダーワイヤ + CCD 色分け)

**済 (Q14)**:
- **`PhysicsGizmos`** (新プロジェクト `Luxel.Physics.Gizmos`、Luxel.Physics + Luxel.TwoD を参照 = `ParticleGizmos` の先例に倣う。物理コアは DebugDraw 非依存のまま)。`DrawColliders(world, dynamicColor, staticColor, ccdColor, width)` = ECS の `Collider` + `RigidBody`/`StaticBody` + `LocalTransform` を **OBB ワイヤ (12 辺)** で描画。寸法は `Collider.RenderScale` (箱=実寸、球/カプセル=外接ボックス)、姿勢は `LocalTransform` の回転 + 平行移動。**動的=緑 / 静的=灰 / CCD 有効=赤** を色分け。先頭で `DebugDraw.IsEnabled` 判定 → **OFF 時は ECS 列挙も割り当てもゼロ**。カテゴリ `gizmo.physics`。
- 3D → 2D 投影は `DebugDraw.Flush` の `WorldToScreen` に任せる (2D=恒等/3D=viewProj)。デモは決定的な等角投影。
- テスト: `DebugDrawTests` に 2 件追記 (箱 2 個 = 24 辺 / OFF ゼロ / CCD 色分けは Flush 後の Scene2D.Shapes 色で検証)。
- golden: **Demos/3D/PhysicsGizmos** (Order 129) — 床 (静的) + 動的箱 2 (軸整列 + 回転 OBB) + CCD 球の外接箱を等角投影で 1 枚 (Canvas2D=Skia 可・決定的、Bepu を回さず authored pose で gizmo 層だけを守る)。e2e 66/66 diff 0。
- Docs: Docs/DevTools の gizmo 節に `PhysicsGizmos.DrawColliders` を追記 + `Demos/3D/PhysicsGizmos` の StoryRef。

**残 (ステージ③ の完成 = Q15 以降)**:
- **接触点**の gizmo: [04](04-physics-contact-events.md) (Q15) の接触イベントを購読して接触点マーカを描く。
- **トリガーボリューム**の gizmo: 04 のトリガー Collider を専用色/破線で描く。
- これらが済んだら **21 の全ステージ完了 → 21 MD を削除** (README 一覧からも 21 行を削除)。

### 2026-07-07: ステージ ① の E / F / Docs 完了 (Q05 クローズ)

**済 (Q05 の残り)**:
- **E (WithDevTools 統合)**: 新アセンブリ `Luxel.Framework.DevTools` (橋渡し層、Framework 本体は DevTools 非依存)。`LuxelHostBuilder.WithDevTools(DevToolsOptions)` が `DevToolsListener` + `DebugServer`(ブラウザ) / `DevToolsApp`(内蔵) + `IFramePublisher` を `IHostedService` として host に載せる (host.Start/StopAsync でライフサイクル管理)。引数解釈 `DevToolsOptions.Parse` は純ロジックなので net10.0 の `Luxel.DevTools` 側に置きテスト可能に。フラグ: `--devtools[ port]` / `--devtools-port <n>` = ブラウザ (port 省略で自動)、`--devtools-native` = 内蔵 (第二 GpuDevice factory 要)、併用可。**当初 MD 案の「`--devtools`=内蔵」から変更** — S17 のブラウザ検証フローと互換を優先し `--devtools`=ブラウザ、内蔵は `--devtools-native`。`LuxelCavern` を集約 API へ移行 (手書き `CavernDevServer` 削除、提示ループは `IFramePublisher.Publish` を呼ぶだけ)。ポート衝突 (Windows 予約帯) でも DebugServer 起動失敗を握ってゲーム本体は継続。
  - **3D (RenderGraph) フレーム調査**: `IFramePublisher.Publish` はホスト可視 (host-mapped) の RGBA `GpuBuffer` を受ける汎用口で、バックエンド非依存。2D (Cavern) は提示バッファをそのまま渡せる。**3D ゲームは RenderGraph の最終カラーターゲットを host-mapped バッファへ resolve/readback してから Publish する必要がある** (自動 RG キャプチャは無い) — これは 2D と同じ要件。Q18 (3D capstone) 未着手のため実機ライブ確認は Q18 で。
- **F (ライブビュー fps 化)**:
  - **F1 割り当て除去**: `FrameChannel` (seqlock 付き 3 枚リング) を新設し `DevToolsListener` のフレームスロットを `LatestSlot<byte[]>` から置換。書き手 (ゲーム main スレッド) はスロットへコピーするだけで定常割り当てゼロ。読み手 2 系統 (HTTP + 内蔵版島) は seqlock で整合検証しつつ自前 body へコピー (破れなし)。`AppWindow.EmitFrame` も tight/DiagFrame 使い回しで毎フレーム new byte[] 排除。単体テスト `FrameChannelTests` 7 (body 形状/304/latest-wins/リサイズ/未サイズ無視/`ReadInto` 再利用/二読者 2 万フレーム torn なし)。
  - **F3 WebSocket push**: `DebugServer` に `GET /ws/frame` — frame rev が進むたび最新フレームを binary で push、latest-wins (各送信を await、遅い受信は中間フレーム間引き、キュー深さ 1)、メインスレッド非ブロック、pause 中は送信なし。既存 `/frame` ポーリングは互換で残置。ブラウザ `index.html` は `connectFrameSocket()` で購読・`blitFrame()` 共通化・接続中はポーリング停止・切断で自動フォールバック/再接続。`FrameChannel.ReadInto(ref buf)` で読み手も送信バッファ使い回し (2MB body を LOH に積むと gen2 GC がゲーム main を巻き込むため)。
  - **F2 内蔵版**: `DevToolsUi.UpdateFrame` の main フレーム読みを `GetFrameInto(ref _frameBuf)` に変更 (島スレッドの LOH churn ゼロ)。ペーシングは既存 16ms (60fps 目標) 維持。
  - **F4 MJPEG: 不採用** (計測して判断)。loopback 生 RGBA が実機 25MB/s で余裕 (60fps≈120MB/s も帯域内)、帯域は制約でない。MJPEG はリモート/高解像度向けで F5 のスコープ外方針と一致。
  - **実機検証 (Claude in Chrome)**: `LuxelCavern.exe vk --devtools`(自動ポート) を起動し DevTools を開いて確認: `/ws/frame` が 960×540 (~2MB) を live push、受信 fps == publish fps で欠落なし、Home に live frame + DevStats (fps/particles/state) 表示。**プール化の効果**: WS 25.5MB/s 配信中でも gen2 GC は 5 秒で +1 (プール化前は ~1.5/s)。ゲーム自身は Debug + 非フォアグラウンド窓で ~13fps だったが、それは配信経路でなくゲームの提示レート (配信は追従・非ボトルネック)。**60fps リテラル値の確認には実フォアグラウンドの 60fps ゲームが要る**が、loopback 帯域と main スレッドのゼロ割り当ては数値で担保済み。
- **Docs**: `Docs/DevTools` (DocsRuntime.cs) に「スタンドアロンゲームへ結線する — WithDevTools」+「ゲームを観測する」節 (DevStats / DebugDraw・gizmo / timescale / ECS スケール / ライブビューのモード=滑らかさ vs 正確さ) を追記。
- **検証**: `dotnet build` (全ソリューション) OK / `dotnet test` 805 passed (+12) / e2e 65/65・golden diff 0 / 実機 chrome で live view 確認。

### 2026-07-07 (2): 性能修正 — フレーム読み戻しの write-combined ペナルティ

**問題** (ユーザー報告): DevTools 購読中にゲームが ~10fps へ低下 (devtools なしは正常)。**実測で原因確定**: `FramePublisher` の padded→tight コピーが提示バッファ (`GpuMemoryKind.HostMapped` = write-combined/uncached) を毎フレーム CPU 読みしていて **copy=75.15ms / emit=0.11ms** (960×540)。WC メモリの CPU 読みは通常 RAM の ~40 倍遅い。旧 `CavernDevServer` は 4 フレームに 1 回スキップして隠していた (それでも ~19ms/frame)、毎フレーム化で顕在化。

**修正**: `IFramePublisher` を **GPU 読み戻しの定石**へ — `GpuMemoryKind.HostCached` (READBACK) バッファへ `GpuCommandBuffer.CopyBuffer` で GPU コピーしてから cached を CPU 読み (高速)。さらにライブ配信を 30fps に間引き。**実測 75ms → ~0.6ms/publish** (約 100 倍改善)。readback/tight/DiagFrame は使い回しで割り当てゼロ、`FramePublisher` は `GpuDevice` を DI 注入・`IDisposable` で readback を破棄。chrome で 960×540 が正しく streaming 継続を確認。

**注意 (別課題)**: `AppWindow.EmitFrame` (Gallery 系) も同じ WC 直読みパターンを持つが、FramePublisher とは別経路。実クライアントが 60fps で張り付く shipped アプリは無いため今回は据え置き (必要になれば同じ readback 定石を適用)。

## スコープ外

- GPU pass 単位のタイムスタンプ計測 (Tier 2 の既存項目)、メモリ allocation tracking、リモート (loopback 外) 接続と認証、DevTools UI のフレームワーク化 (素の HTML/JS を維持)、入力記録リプレイ (→ [11](11-scripting-debug-tools.md) B)、Console/REPL タブ (→ [11](11-scripting-debug-tools.md) A)。
- **GPU ハードウェアエンコードのライブ配信** (F5 の決定)。**クリップ録画 (H.264/MP4 をファイル保存)** は将来の別機能 — やるなら Media Foundation に CPU フレームを渡す方式 (ハードウェア MFT が効く・GPU interop 不要・ライブ経路と独立) で、Windows 専用オプションとして。
