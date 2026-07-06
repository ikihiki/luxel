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

## 作業ステップ

1. **骨組み + publish 早回し**: タイトル画面 (日本語 1 行 + ボタン) だけの LuxelCavern を作り、publish チェックリスト 1〜4, 7, 8 を先に 1 周 (直すのは Luxel 本体側: targets / assetRoot / フォント)。
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
