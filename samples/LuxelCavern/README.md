# Luxel Cavern — capstone ① (2D プラットフォーマー)

Luxel エンジンで作ったスタンドアロンの 2D 探索アクション。エンジンの主要機能
(タイルマップ + Sweep 衝突・カメラ追従・パーティクル・FixedUpdate 物理・入力・セーブ・
ScriptSystem・実時間 GameLoop) を「無いとゲームが成立しない形」で使う検証場。

## 構成

- **`LuxelCavern.Core`** (net10.0, 純ロジック): `CavernSim` (プレイヤー物理・収集・敵・トゲ・HP・
  チェックポイント)、`CavernLevel` (レベル/アトラス)、`GameFlow` (状態機械)、`CavernHud`、`CavernSave`。
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

操作: **A/D** または **←→** 移動、**Space/W/↑** ジャンプ、**Esc** ポーズ、**Enter** リトライ。

## 配布 (publish)

self-contained のフォルダ配布 (推奨):

```powershell
dotnet publish samples/LuxelCavern/LuxelCavern -c Release -r win-x64 --self-contained -o publish
```

出力 (約 120 MB) に **`shaders/`** (Slang → SPIR-V/DXIL、`Luxel.Shaders.targets` が publish へコピー)・
**`assets/fonts/`** (同梱 BIZ UDGothic)・ネイティブ DLL (glfw3 / HarfBuzzSharp / Silk.NET.*) が揃う。
アセット/フォント/シェーダは `AppContext.BaseDirectory` (exe の隣) から読むので **cwd 非依存** —
publish フォルダをリポジトリ外の任意パスへコピーして起動できる (検証: `C:\`, `%TEMP%` から vk/dx とも exit 0)。

### 動作要件

- Windows x64 + **Vulkan または D3D12 対応 GPU ドライバ**。`vulkan-1.dll` は OS/ドライバ側 (同梱しない)。
- .NET ランタイム不要 (self-contained)。

### 既知の制限

- **単一ファイル publish** (`-p:PublishSingleFile=true`) は、同梱フォント (Content) が単一ファイルへ
  バンドルされ `BaseDirectory` から見つからず起動失敗する。フォルダ配布を推奨。
  (Content を loose に保つ設定 = single-file 対応は将来課題。)
