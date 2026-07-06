# 19 — capstone ①: 2D プラットフォーマー「Luxel Cavern (仮称)」+ スタンドアロン publish 検証

## 概要

**「Gallery の外で動くゲームを 1 本、dotnet publish して他のマシン (リポジトリ外のパス) で起動する」**を最後まで通す。エンジンとしての完成の定義そのものであり、アセット同梱・シェーダ配置・フォント・保存先・起動時間の穴が全部ここで見つかる。

題材は **2D 探索アクションプラットフォーマー「Luxel Cavern (仮称)」** — Tier 1 タスク群 (13/14/15/16/17/18) + 01/10 を「飾りではなく、無いとゲームが成立しない形」で使う検証場を兼ねる (2026-07-06 決定。当初案の Breakout 移植は「カメラ追従・タイルマップ・セーブが自然に入らない」ため置き換え)。3D 系タスク (03/04/05/09) の検証場は capstone ② ([20](20-game2-3d-shooting-range.md))。

**まず最小構成 (タイトル画面だけ) で publish を 1 回通し、見つかった穴を潰してからゲームを太らせる**のが本タスクの進め方。

## ゲーム仕様

### 状態遷移

```
Title ──はじめる──→ Playing ⇄ Pause (Esc)
  │                    │
  └─つづきから (セーブがあれば)   ├─ HP 0 / 落下 → GameOver → Title (つづきから可)
                       └─ 扉に到達 → Clear → Title
```

### プレイ内容 (1 ステージの縦切り)

- **マップ**: Tiled (.tmj) 製タイルマップ 1 枚 (目安 128×32 タイル、16px タイル)。地形・トゲ (接触ダメージ)・チェックポイント 2 個・鍵 3 個・扉 (ゴール)。衝突は TileMap の `Sweep`/`QueryAabb` ([18](18-sprite-atlas-tilemap.md))。
- **プレイヤー**: 走る + ジャンプ。HP 3。被ダメージでノックバック + 無敵時間 + カメラシェイク。移動・重力・ジャンプは FixedUpdate (1/60) 駆動 + `InterpolatedTransform` で描画補間 ([14](14-framework-fixedupdate.md))。ジャンプの気持ちよさ調整 (コヨーテタイム/ジャンプバッファ) は任意。
- **敵 2 種**: 巡回歩行 (床の端で反転) と飛行 (サイン波)。接触でダメージ、上から踏むと撃破 (AABB 判定)。**片方の AI は .csx で書き、開発中 hot reload で調整する ([01](01-scripting-scriptsystem-hot-reload.md) のドッグフーディング — 01 未実装なら C# 直書きで開始し、01 実装後に移行)**。
- **収集**: コイン (取得でキラキラ)、鍵 3 個で扉が開く。
- **カメラ**: CameraRig2D — デッドゾーン追従 + マップ端の WorldBounds クランプ + 被ダメージ Shake ([17](17-camera-controller.md))。
- **パーティクル**: ジャンプ砂埃・着地・敵撃破バースト・コイン取得・松明の連続放出 ([16](16-particle-system.md) 2D)。
- **UI は日本語**: タイトル (はじめる/つづきから/せってい)、HUD (HP・コイン数)、ポーズ (音量スライダー・操作説明)。同梱フォント ([13](13-e2e-japanese-font.md)) を exe 側 assets からロード — システムフォント非依存の publish 検証を兼ねる。
- **Audio**: BGM はストリーミング再生 ([10](10-audio-streaming.md)。未実装ならクリップ再生で開始し移行)。SE (ジャンプ/コイン/ダメージ) は既存の PCM クリップ。
- **永続化** ([15](15-save-load-settings.md)): チェックポイント通過で World セーブ (A)、「つづきから」でロード。音量・キーバインドは SettingsStore (B) → %APPDATA%。
- **入力**: キーボード + ゲームパッド両対応 (InputAction 層のドッグフーディング)。

### アセット

- タイル/キャラ/UI: Kenney 等の CC0 (数十 KB、assets/ に配置 + ライセンス表記ファイル)。BGM/SE も CC0。
- golden 用アセットと goldens/ は分離 (既存規約)。

## 実装方針

### 1. 配置とロジック共有 (2026-07-06 決定: samples/ 恒久化)

```
samples/LuxelCavern/
├─ LuxelCavern.Core/   # GameScene 派生シーン + システム群。StoryContext 非依存の純ロジック
└─ LuxelCavern/        # WinExe (net10.0-windows)。LuxelHostBuilder + 実窓の結線だけの薄い層
```

- Gallery が `LuxelCavern.Core` を参照し、`Game/Cavern` ストーリーとしてホストする — **play/golden はストーリー側で維持** (ゲームロジックの e2e はここで担保)。スタンドアロン側は「結線 + publish」のみ。
- Program は Gallery の [Program.cs](../src/Luxel.Gallery/Program.cs) RunApp を手本に: STA スレッド、WindowManager、vk/dx 引数切替、Esc 終了。ウィンドウアイコン/タイトル。Alt+Enter フルスクリーンは任意。
- **DevTools**: `--devtools` 引数で DebugServer を起動 ([21](21-devtools-game-scale.md) の `.WithDevTools()`)。既定 off。ゲーム状態 (HP/コイン/状態機械) は DevStats で発信、カメラのデッドゾーン/タイル衝突は gizmo で可視化 — 開発中の標準装備にする。21 の A/C/E はゲーム組み上げ前に済んでいると楽。
- 参照: Framework/Platform/TwoD/UI/Controls/Ecs/Audio/Input/Particles(+.TwoD) + バックエンド (Vulkan + D3D12)。

### 2. 決定性と e2e

- ゲームロジックは FixedUpdate + 固定シード xorshift (ゲーム性の乱数はほぼ無し — パーティクル演出のみ)。
- play 例: `Key(右)` → `Step(n)` → `Expect(コイン=1)` → `Snap` / チェックポイント → 被ダメ → `Expect(HP)` → Load 復元の play。セーブのファイル IO はインメモリ IFileStore で決定的に ([15](15-save-load-settings.md))。
- スタンドアロン版のシード注入口はストーリー版と分ける (毎回同じにしない場合)。

### 3. publish パイプラインの検証 (本丸)

```powershell
dotnet publish samples/LuxelCavern/LuxelCavern -c Release -r win-x64 --self-contained
```

チェックリスト (それぞれ「動くか」を確認し、動かなければ Luxel 本体側を直す):

1. **shaders**: `$(OutDir)shaders` (Luxel.Shaders.targets の出力) が publish 出力にコピーされるか。乗らなければ targets に `CopyToPublishDirectory` 相当を追加。
2. **アセットパス**: ResourceSystem の assetRoot を `AppContext.BaseDirectory` 基準にする口 (cwd 非依存)。ビルド時にゲームの assets/ を出力へコピー。
3. **フォント**: [13](13-e2e-japanese-font.md) の同梱フォントを exe 側 assets にもコピーして LoadBundled で読む。
4. **ネイティブ DLL**: HarfBuzzSharp / ICU データ等が publish 出力に揃うか。vulkan-1.dll は OS 側 (要 GPU ドライバ) — README に動作要件として明記。
5. **単一ファイル化** (`/p:PublishSingleFile=true`) は第 2 段階 — まずフォルダ配布で通す。
6. **設定/セーブの書き込み先**: 出力フォルダに書かない (%APPDATA% — [15](15-save-load-settings.md) と連動)。
7. **リポジトリ外での起動**: publish フォルダを `C:\Temp\cavern-test` 等へコピーして起動 — cwd 依存・リポジトリ相対パス依存がここで露見する。
8. **両バックエンド**: vk / dx 引数切替で両方起動。
9. 起動時間・exe サイズを記録 (Docs「配布」節に書く)。

### 4. 回帰防止

- publish 検証の自動化は最低限「`dotnet publish` が成功し shaders/assets/フォントが出力に存在する」を確認するスクリプト or CI ステップ (実起動は手動スモーク)。
- Docs/Framework (または新 Docs/Shipping ページ) に「ゲームを配布する」手順を書く — 踏んだ穴と対処がそのままドキュメントになる。

## 進捗

- **ステージ A 完了 (2026-07-06, Q04)**: `samples/LuxelCavern/` に骨組みを作成。
  - `LuxelCavern.Core`: `TitleScreen.Build` (日本語タイトル + はじめる/せってい/おわる)・`GameState` enum・`CavernAssets` (exe 隣 `assets/` を `AppContext.BaseDirectory` 基準で解決)。
  - `LuxelCavern` (WinExe): STA スレッド + backend 引数 (vk/dx) + `AppWindow` で提示。`--frames N` で N フレーム描画して自動終了 (スモーク用)。起動失敗は `cavern-crash.log` に残す。
  - **publish チェックリスト結果**: 1 shaders ✓ / 2 assetパス(cwd 非依存) ✓ / 3 フォント同梱 ✓ / 4 ネイティブ DLL (libHarfBuzzSharp・glfw3 ✓、vulkan-1 は OS 側、ICU は Typography.Icu 非参照で不要) / 7 リポジトリ外 (`C:\Temp\cavern-test`) 起動 ✓ / 8 両バックエンド ✓。
  - **Luxel 本体側の修正**: (a) `shaders/Luxel.Shaders.targets` に `AddCompiledShadersToPublish` を追加 — 生成シェーダはプロジェクト項目でないため publish 出力にコピーされない穴を塞いだ (エンジン全体に効く)。(b) `AppWindow.Close()` を追加 (UI ハンドラから対話ループを終了する口)。
  - **未実施 (Stage B / Q13 で)**: Gallery `Game/Cavern` ストーリー + play/golden、単一ファイル化 (checklist 5)、%APPDATA% セーブ先 (checklist 6, ToDo 15 連動)、起動時間/exe サイズ記録 (checklist 9)、Docs「配布」節、`--devtools` (`WithDevTools`, ToDo 21/Q05)。exe サイズは self-contained で約 117MB。

- **ステージ B セッション 1 (2026-07-06, Q13)**: **ゲームプレイの核** — プレイヤー物理 + タイルマップ + カメラ追従。
  - `LuxelCavern.Core` を **net10.0** へ (依存の UI/Controls/TwoD/Typography は全 net10.0。テスト・Gallery から参照可能に)。
  - `CavernLevel` (アトラス矩形 grass/dirt/wall + タイルセット + コード定義レベル [地面/浮き床/壁柱/段差] + スポーン、純データ GPU 非依存)。
  - `CavernSim` (プレイヤー物理: 走る + 重力 + ジャンプを `TileMap.Sweep` 衝突で解決。**固定 dt で決定的**・GPU 非依存)。
  - Gallery `Game/Cavern` ストーリー (GpuScene で手続きアトラスを焼き、sim を固定 dt で事前実行 → タイル + プレイヤー + 追従カメラを描く。**実時間 GameScene/StoryAppView は wall-clock dt で非決定的なので採らず**、sim を事前実行して golden 決定化)。vk golden。
  - 単体テスト `CavernSimTests` 5 (落下着地/右移動/接地時のみジャンプ・二段不可/壁停止/決定性)。計 734 passed。e2e 65/65 diff 0。
- **ステージ B セッション 2 (2026-07-06, Q13)**: **ゲームコンテンツ層** — 収集/ハザード/敵を `CavernSim` に追加 (純ロジック・決定的)。
  - 収集物 `Pickup` (コイン/鍵) + `CavernSim.Coins`/`Keys`。鍵 3 個で扉 (`DoorOpen`) が開き、開扉に触れると `Result=Cleared`。
  - HP (3) + トゲタイル (id 4=Spike、非 solid だが接触ダメージ) + 巡回敵 `Walker` (接触ダメージ / 上から踏み撃破 + バウンド)。
  - 被弾: 無敵時間 (1s) + ノックバック (横入力を 0.18s 無視) + `ShakeRequested` (カメラシェイクの発火口)。HP 0 / 落下 (`KillY`) で `Result=Dead`。
  - `CavernLevel.CreateSim()` = マップ + コイン6/鍵3/扉/巡回敵/トゲ を配置した完全な sim。アトラス 4 セル目に spike (赤)。
  - Gallery `Game/Cavern` golden をエンティティ描画に更新 (タイル + コイン/鍵/扉/敵/プレイヤーを ContentColors で per-shape 色)。
  - 単体テスト +9 (コイン収集/鍵3で開扉/開扉クリア/トゲダメージ+シェイク/無敵で追加ダメージ無し/敵接触ダメージ/踏み撃破+バウンド/落下死/CreateSim 配置)。計 743 passed。e2e 65/65 diff 0。
- **ステージ B セッション 3 (2026-07-06, Q13)**: **敵 2 種完成 + パーティクル演出** (純ロジック・決定的)。
  - 飛行敵 `Flyer` (Home 中心にサイン波で浮遊、AmpX/AmpY/Freq/Phase)。接触/踏み撃破は `Walker` と共通の `Contact` ヘルパに集約。
  - 演出イベント: `LandedThisStep` (着地) / `PickupsThisStep` / `DefeatsThisStep` (位置) を毎ステップ発信 (Core は Particles 非依存、位置だけ渡す)。`CavernLevel.Torches` (松明位置の純データ)。
  - Gallery 側で `ParticleSystem` を sim と同じ固定 dt で回し、**松明の炎 (連続) + 着地砂埃 / コイン / 撃破バースト** を per-particle tint で出す (パーティクル + tint のドッグフーディング)。golden に炎/コインスパークルが出る。
  - 単体テスト +5 (飛行敵オシレート/飛行接触ダメージ/飛行踏み撃破+イベント/着地イベント/収集イベント)。計 748 passed。e2e 65/65 diff 0。
- **ステージ B セッション 4 (2026-07-06, Q13)**: **チェックポイント + ゲーム進捗のセーブ/ロード** (純ロジック・テスト可能、Q06 永続化のドッグフーディング)。
  - `Checkpoint` (通過で `LastCheckpoint` = 復活位置更新 + `CheckpointThisStep` イベント)。`CavernLevel` に 2 個配置。
  - `CavernSave` DTO (復活位置 + HP/コイン/鍵 + 収集/撃破/通過フラグ配列) + JSON 往復。`CavernSim.Export()`/`ApplySave()` (新規 CreateSim に流し込む「つづきから」。版ずれに耐える bounds-safe Restore)。
  - ファイル IO の場所 (%APPDATA%) は exe の責務として分離 (Core は純データ + 直列化)。golden にチェックポイント旗を描画。
  - 単体テスト +2 (チェックポイント通過で復活位置更新 / セーブ→JSON→ロードで進捗復元)。計 750 passed。e2e 65/65 diff 0。
- **ステージ B セッション 5 (2026-07-06, Q13)**: **HUD + 状態機械 + ポーズ** (純ロジック・テスト可能 / golden)。
  - `GameFlow` (Title ⇄ Playing ⇄ Paused、Playing → GameOver/Clear。CavernSim を保持し Playing 中のみ Step、Result を状態へ反映)。
  - `CavernHud` (HP ハート・コイン・鍵を**カメラにアンカーしてスクリーン空間**へ描く純ロジック。screen px → world 逆算。テキストは DebugTextDrawer 委譲。ポーズオーバーレイも)。exe/ストーリー共用。
  - Gallery `Game/Cavern` golden に HUD (HP×3 / ×3 / 0/3) を描画 (world-anchored でカメラ変換下でも画面左上に固定)。
  - 単体テスト +6 (GameFlow: 開始/ポーズ切替/ポーズ中は進まない/死亡→GameOver/クリア→Clear/つづきから復元)。計 756 passed。e2e 65/65 diff 0。
  - **残 (次セッション以降)**: 片方の敵 AI を .csx (ScriptSystem、実時間 exe で hot reload) / SettingsStore で音量/キーバインドを %APPDATA% へ (Q06 B) + 設定 UI / Audio (BGM ストリーミング + SE) / **実時間 GameScene + 補間 + 入力 (キーボード/パッド) で exe プレイアブル化** (LuxelHostBuilder/GameLoop へ移行、%APPDATA% 実ファイル書込、--frames スモーク) / Tiled (.tmj) レベル化 / WithDevTools + gizmo/DevStats (Q05-E/Q12 合流) / publish 本番 (checklist 5,6,9) + リポジトリ外スモーク / Docs「配布」節。

## 作業ステップ

1. **骨組み + publish 早回し**: タイトル画面 (日本語 1 行 + ボタン) だけの LuxelCavern を作り、publish チェックリスト 1〜4, 7, 8 を先に 1 周 (直すのは Luxel 本体側: targets / assetRoot / フォント)。 ← **済 (Q04)**
2. 機能タスクの実装は各 ToDo (13→14→17→18→16→15→10 推奨順) 側で行い、完成したものからゲームへ統合。未実装機能は仮実装で先へ進んで良い (敵 AI 直書き、BGM クリップ等)。
3. ステージ制作 (Tiled) + プレイヤー/敵/収集/セーブの組み上げ。
4. Gallery ストーリー版の play/golden 整備。
5. publish 本番 (チェックリスト全項目) + リポジトリ外起動スモーク (vk/dx) + Docs「配布」節 + README 動作要件。発見した未解決の穴は ToDo/ に追記。

## 罠・注意

- STA スレッド必須 (実窓/TSF) — Gallery Program.cs と同じ Thread + SetApartmentState(STA) 構成を踏襲。
- GameScene の GPU 資源は「シーンの最初のフレーム内で遅延生成」規約 (起動スレッドから触らない)。
- Luxel.Framework は net10.0-windows — RID は win-x64 のみで良い。
- publish 出力の tools/slang 非依存を確認 (依存が漏れていたらビルド時 targets の問題)。
- Ecs/Framework の `Phase` 名前衝突 — using alias (既知の罠)。
- 新しい [UiComponent] を足すと Reference/Overview golden が変わる → e2e --update 対象。

## スコープ外

- インストーラ/署名/ストア配布、自動アップデート、クロスプラットフォーム、Steam 統合。
- 複数ステージ・ボス・メトロイドヴァニア的な能力解放 (縦切り 1 ステージで完成とする)。
- 03/04/05/09 (3D 物理・skin/morph) の検証 → [20](20-game2-3d-shooting-range.md)。
