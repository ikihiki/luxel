# 19 — ゲーム出荷の垂直スライス: スタンドアロン publish 検証

## 概要

**「Gallery の外で動くゲームを 1 本、dotnet publish して他のマシン (リポジトリ外のパス) で起動する」**を最後まで通す。エンジンとしての完成の定義そのものであり、アセット同梱・シェーダ配置・フォント・保存先・起動時間の穴が全部ここで見つかる。個別機能タスク (14〜18) の検証場でもあるため、**まず最小構成で 1 回通し、見つかった穴を Issue 化する**のがこのタスクの成果物。

## 背景と現状

- **素材**: BreakoutStory ([src/Luxel.Gallery/Stories/BreakoutStory.cs](../src/Luxel.Gallery/Stories/BreakoutStory.cs)) — ECS + フェーズ system + 2D 描画のショーケースが既にある。ただし story (StoryContext/Gallery ホスト前提) なので、LuxelHostBuilder + 実窓の実アプリへの移植が必要。
- **実アプリの骨格は実装済み**: LuxelHostBuilder (DI) → GameLoop → GameScene、WindowSystem/WindowManager (Win32 実窓 + swapchain present + TSF IME)、XAudio2、XInput。Gallery の Program.cs ([src/Luxel.Gallery/Program.cs](../src/Luxel.Gallery/Program.cs)) の RunApp が実窓結線の完全な手本。
- **シェーダは事前コンパイル**: Luxel.Shaders.targets がビルド時に slangc で SPIR-V/DXIL を `$(OutDir)shaders` へ出力 — **実行時に slangc は不要** (ビルドマシンには tools/slang が必要)。publish でこの shaders フォルダが出力に含まれるかは未検証。
- **アセット解決**: ResourceSystem は assetRoot 相対 (Gallery は cwd = リポジトリルート前提)。**exe 配置で動かすには AppContext.BaseDirectory 基準に切り替える必要がある**可能性が高い。
- **配置の方針論**: リポジトリは「サンプルは Gallery に一本化 (Luxel.Samples は削除済み)」の決定がある。本タスクの成果物は**ドキュメント用サンプルではなく出荷検証**なので別枠と考えるが、恒久プロジェクトとして残すか検証後に削るかは**ユーザーに確認する** (推奨: `samples/Breakout/` をトップレベルに置き「出荷テンプレート」として恒久化 — 新規ゲームの雛形になる)。

## 実装方針

### 1. スタンドアロン Breakout プロジェクト

- 新プロジェクト (例 `samples/Breakout/Breakout.csproj`、net10.0-windows、`OutputType=WinExe`)。参照は Framework/Platform/TwoD/UI/Controls/Ecs/Audio/Input + バックエンド (Vulkan + D3D12)。
- Program: LuxelHostBuilder で DI 構築 → WindowManager で実窓 → BreakoutScene (GameScene 派生 — BreakoutStory のロジックを StoryContext 非依存に移植。タイトル/プレイ/ゲームオーバーの 3 状態くらいまで)。
- 入力はゲームパッド + キーボード両対応 (InputAction 層のドッグフーディング)。
- ウィンドウアイコン/タイトル、Esc で終了、Alt+Enter フルスクリーンは任意 (あると出荷感が出る)。

### 2. publish パイプラインの検証 (本丸)

```powershell
dotnet publish samples/Breakout -c Release -r win-x64 --self-contained
```

チェックリスト (それぞれ「動くか」を確認し、動かなければ直す):

1. **shaders**: `$(OutDir)shaders` が publish 出力にコピーされるか (Luxel.Shaders.targets の出力が Content として publish に乗るか。乗らなければ targets に `CopyToPublishDirectory` 相当を追加)。
2. **アセットパス**: ResourceSystem の assetRoot を `AppContext.BaseDirectory` 基準にする口 (cwd 非依存)。ビルド時にゲームの assets/ を出力へコピー。
3. **フォント**: システムフォント依存をなくす — [13](13-e2e-japanese-font.md) の同梱フォントを exe 側 assets にもコピーして LoadBundled で読む。
4. **ネイティブ DLL**: HarfBuzzSharp / SkiaSharp (使うなら) / ICU データ等が publish 出力に揃うか。vulkan-1.dll は OS 側 (要 GPU ドライバ) — README に動作要件として明記。
5. **単一ファイル化** (`/p:PublishSingleFile=true`) は**第 2 段階** — shaders/assets は Content のままバンドル外に置く構成が現実的。まずフォルダ配布で通す。
6. **設定/セーブの書き込み先**: 出力フォルダに書かない (%APPDATA% — [15](15-save-load-settings.md) と連動。15 が未実装ならこの検証はスキップ可)。
7. **リポジトリ外での起動**: publish フォルダを `C:\Temp\breakout-test` 等へコピーして起動 — cwd 依存・リポジトリ相対パス依存があればここで露見する。
8. **両バックエンド**: vk / dx 引数切替で両方起動。
9. 起動時間・exe サイズを記録 (README/Docs に「配布」節として書く)。

### 3. 回帰防止

- ゲームロジック部は可能な範囲で Gallery ストーリー (既存 Game/Breakout) と共有し、e2e play を維持 — スタンドアロン側は「結線 + publish」の薄い層に保つ。
- publish 検証は自動化しにくい (フル publish は遅い) — 最低限「`dotnet publish` が成功し shaders/assets が出力に存在する」ことを確認するスクリプト or CI ステップを用意 (実起動は手動スモーク)。
- Docs/Framework (または新 Docs/Shipping ページ) に「ゲームを配布する」手順を書く — 本タスクで踏んだ穴と対処がそのままドキュメントになる。

## 作業ステップ

1. 配置場所をユーザーに確認 (推奨: samples/Breakout 恒久化)。
2. BreakoutScene の実アプリ移植 (実窓で遊べるところまで)。
3. publish チェックリストを上から潰す (直すのは Luxel 本体側: targets / assetRoot / フォント)。
4. リポジトリ外起動の実機スモーク (vk/dx)。
5. Docs「配布」節 + README 動作要件。発見した未解決の穴は ToDo/ に追記。

## 罠・注意

- STA スレッド必須 (実窓/TSF) — Gallery Program.cs と同じ Thread + SetApartmentState(STA) 構成を踏襲。
- GameScene の GPU 資源は「シーンの最初のフレーム内で遅延生成」規約 (起動スレッドから触らない)。
- Breakout の乱数/初期状態は snap 決定性のため固定シード — スタンドアロン版で「毎回同じ」にしないならシード注入口を分ける (Gallery 側 play は固定のまま)。
- Luxel.Framework は net10.0-windows — RID は win-x64 のみで良い (クロスプラットフォームは Tier 3 の別議論)。
- publish 出力の tools/slang 非依存を確認 (依存が漏れていたらビルド時 targets の問題)。

## スコープ外

- インストーラ/署名/ストア配布、自動アップデート、クロスプラットフォーム、Steam 統合。
