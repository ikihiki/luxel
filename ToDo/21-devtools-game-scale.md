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

**残 (Q05 の残り — 次セッション以降)**:
- **DebugDraw コア** (B の gizmo 本体ではなく描画レイヤ基盤): RetainedCanvas 最前面オーバーレイ + `DebugDraw.Line/Rect/Circle/Text` 即時 API + ワールド→スクリーン変換 delegate (2D/3D 両対応)。off 時ゼロ割り当て。
- **E (WithDevTools 統合)**: `LuxelHostBuilder` 相当の `.WithDevTools(...)` オプトイン (Gallery Program.cs の定型集約)、`--devtools` (内蔵) / `--devtools-port` (ブラウザ)、3D フレーム (RenderGraph) が DiagFrame に乗るか調査。19 の publish スモークに合流。
- **F (ライブビュー fps 化)**: F1 割り当て除去 (リングバッファ + 二読者所有権) → F2 内蔵版 60fps 実測 → F3 ブラウザ WebSocket push (latest-wins) → 任意 F4 MJPEG。
- **Docs**: Docs/Framework or 新 Docs/DevTools に「ゲームを観測する」節 (E/F 完了時にまとめて執筆)。
- B の機能別 gizmo (物理/カメラ/タイル) は Q12/Q14 で対象機能実装後。

## スコープ外

- GPU pass 単位のタイムスタンプ計測 (Tier 2 の既存項目)、メモリ allocation tracking、リモート (loopback 外) 接続と認証、DevTools UI のフレームワーク化 (素の HTML/JS を維持)、入力記録リプレイ (→ [11](11-scripting-debug-tools.md) B)、Console/REPL タブ (→ [11](11-scripting-debug-tools.md) A)。
- **GPU ハードウェアエンコードのライブ配信** (F5 の決定)。**クリップ録画 (H.264/MP4 をファイル保存)** は将来の別機能 — やるなら Media Foundation に CPU フレームを渡す方式 (ハードウェア MFT が効く・GPU interop 不要・ライブ経路と独立) で、Windows 専用オプションとして。
