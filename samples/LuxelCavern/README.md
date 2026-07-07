# Luxel Cavern — capstone ① (2D プラットフォーマー)

Luxel エンジンで作ったスタンドアロンの 2D 探索アクション。エンジンの主要機能
(タイルマップ + Sweep 衝突・カメラ追従・パーティクル・FixedUpdate 物理・入力・セーブ・
オーディオ (BGM/SE)・ScriptSystem・実時間 GameLoop) を「無いとゲームが成立しない形」で使う検証場。

## 構成

- **`LuxelCavern.Core`** (net10.0, 純ロジック): `CavernSim` (プレイヤー物理・収集・敵・トゲ・HP・
  チェックポイント)、`CavernTiled` (Tiled .tmj レベル読み込み)、`CavernLevel` (アトラス/タイルセット)、
  `GameFlow` (状態機械)、`CavernHud`、`CavernSave`、`CavernSettings`、`CavernAudio`。
  GPU/実窓に非依存で**決定的** — Gallery の `Game/Cavern` ストーリーが play/golden を維持し、
  単体テスト (`tests/Luxel.Tests/CavernSimTests` 他) が守る。
- **`LuxelCavern`** (net10.0-windows, WinExe): `CavernRealtimeScene : GameScene` を
  `LuxelHostBuilder` + `GameLoop` で駆動し、`WindowSystem`/`GpuSurface` へ提示する薄い層。

## 実行

```powershell
dotnet run --project samples/LuxelCavern/LuxelCavern -- vk        # Vulkan (既定)
dotnet run --project samples/LuxelCavern/LuxelCavern -- dx        # D3D12
dotnet run --project samples/LuxelCavern/LuxelCavern -- vk --frames 30   # 30 フレームで自動終了 (スモーク)
```

起動するとタイトル画面が出る。**Space/Enter** で「はじめる」、セーブがあれば **C** で「つづきから」、**S** で「せってい」、**Esc** で終了。
設定画面では **↑↓** で項目選択・**←→** で音量調整・**Esc** で戻る (変更は即 %APPDATA% へ保存)。

ゲーム中の操作: **A/D** または **←→** 移動、**Space/W/↑** ジャンプ、**Esc** ポーズ、**Enter** リトライ (死亡時は直近チェックポイントから復活)。

### セーブ

チェックポイント通過で **`%APPDATA%\LuxelCavern\cavern-save.json`** へオートセーブ (HP/コイン/鍵/収集・撃破・通過フラグ + 復活位置)。
次回起動時にタイトルへ「つづきから」が出る。クリアするとセーブは消える。永続化は `IFileStore` 抽象 (`Luxel.Settings`) 上で
行い、exe は `PhysicalFileStore` を %APPDATA% に向ける — ロジックはインメモリ実装で単体テスト済み (`CavernPersistenceTests`)。

### オーディオ

ループ BGM + イベント SE (ジャンプ/着地/コイン/鍵/撃破/被弾/チェックポイント/クリア)。音は CPU 合成で外部アセット不要
(`CavernSfxBank`、決定的)。`CavernSfxDetector` が sim の出来事を SE に変換し、`AudioBus` 階層 (Master → Music / Sfx) で
音量を分ける。バックエンドは exe が XAudio2 (`UseAudio`)、テストは `NullAudioBackend`。

### 設定

タイトルの「せってい」で音量 (Master / BGM / SE) を調整。値は `SettingsStore` (`Luxel.Settings`) 上の `Signal<float>` で、
`AutoSave` により変更が即 **`%APPDATA%\LuxelCavern\cavern-settings.json`** へ書き戻る。`CavernAudio` が設定値を
`AudioBus.Volume` へ束ね、次フレームから発音に効く。破損ファイルは既定値で起動 + `.bak` 退避 (SettingsStore が担保)。

## 配布 (publish)

self-contained のフォルダ配布 (推奨):

```powershell
dotnet publish samples/LuxelCavern/LuxelCavern -c Release -r win-x64 --self-contained -o publish
```

出力 (約 120 MB) に **`shaders/`** (Slang → SPIR-V/DXIL、`Luxel.Shaders.targets` が publish へコピー)・
**`assets/fonts/`** (同梱 BIZ UDGothic)・ネイティブ DLL (glfw3 / HarfBuzzSharp / Silk.NET.*) が揃う。
アセット/フォント/シェーダは `AppContext.BaseDirectory` (exe の隣) から読むので **cwd 非依存** —
publish フォルダをリポジトリ外の任意パスへコピーして起動できる (検証: `C:\`, `%TEMP%` から vk/dx とも exit 0)。

### レベル (Tiled)

ステージは **Tiled (.tmj = JSON)** で外部化し、`LuxelCavern.Core/levels/cavern1.tmj` を Core.dll に埋め込む
(exe/Gallery/テストが同一レベルを持ち、publish の追加コピー不要)。

読み込みは **`ResourceSystem` 経由で管理**する: `CavernLevelLoader` が `res://levels/cavern1.tmj` を
`EmbeddedResourceSource` (スキーム `res://` の `IResourceSource`) 越しに `byte[]` ノードとしてロードし、
キャッシュ/型付きノード/(将来の) リロードをリソースシステムに任せる。パースは `CavernTiled` (純ロジック) —
タイル層は `TileMap.FromTiledJson`、オブジェクト層 (`objectgroup`) は本ゲーム固有のエンティティ
(coin/key/door/walker/flyer/checkpoint/torch) として `CavernSim` に流し込む。Tiled で編集すればレベルを差し替えられる。

### 動作要件

- Windows x64 + **Vulkan または D3D12 対応 GPU ドライバ**。`vulkan-1.dll` は OS/ドライバ側 (同梱しない)。
- .NET ランタイム不要 (self-contained)。

### 既知の制限

- **単一ファイル publish** (`-p:PublishSingleFile=true`) は、同梱フォント (Content) が単一ファイルへ
  バンドルされ `BaseDirectory` から見つからず起動失敗する。フォルダ配布を推奨。
  (Content を loose に保つ設定 = single-file 対応は将来課題。)
