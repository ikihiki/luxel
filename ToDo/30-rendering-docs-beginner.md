# 30 — 初心者向けレンダリング学習ドキュメント

## 目的

Luxel.Gallery のレンダリング関連 Docs / Demos を、機能カタログ・設計資料だけでなく、初心者が空のプロジェクトから実用的な描画まで段階的に習得できる学習経路へ整理する。

完了時の読者像:

- Vulkan / D3D12 の前提とバックエンド選択を説明できる。
- Gallery 専用ハーネスと通常アプリの違いを理解している。
- ウィンドウ、GPU device、surface/swapchain、command、submit、present の責務を理解している。
- コピー可能な最小アプリで Clear、三角形、テクスチャ、MVP + depth を実装できる。
- Slang シェーダと Git 管理キャッシュを更新できる。
- 2D / 3D / RenderGraph / glTF のどこへ進むべきか判断できる。
- 真っ黒な画面、古いシェーダ、行列・depth・texture・resize の典型問題を調査できる。

## 現状と問題

既存ページ:

- `Docs/GettingStarted`, `Docs/Architecture`, `Docs/FirstTriangle`
- `Docs/GpuDevice`, `Docs/TwoD`, `Docs/RenderGraph`, `Docs/ThreeD`, `Docs/Assets`
- `Docs/Resources`, `Docs/Platform`, `Docs/Framework`
- API reference (`Reference/Core`, `Reference/TwoD`, `Reference/ThreeD`, `Reference/Runtime`)

既存デモは GPU、2D、3D、RenderGraph、glTF まで広いが、Gallery 内部の `GpuView` / `IGpuScene` / private scene 実装を前提にしている。Story source generator は `[Story]` メソッド本体だけを表示するため、`Docs/FirstTriangle` から参照する `Demos/3D/Triangle` は完全な実装を提示できない。

主な不足:

1. Gallery 外で動く最小レンダリングアプリ。
2. Clear → Triangle → Texture → MVP/depth の段階的カリキュラム。
3. ウィンドウ・surface・frame loop・resize・shutdown の説明。
4. Slang ABI、C# 構造体、bindless、キャッシュ更新の専用ページ。
5. 2D / 3D / RenderGraph / glTF への選択ガイド。
6. バックエンド差、座標系、色空間、同期、トラブルシューティング。
7. Docs 内コードと実サンプルの乖離を防ぐ自動検証。

## 情報設計

初心者向けページは `Learn/Rendering/*` に集約し、既存 `Docs/*` は設計・サブシステム解説、`Demos/*` は機能カタログと回帰テストとして残す。

推奨順:

1. `Learn/Rendering/Overview`
2. `Learn/Rendering/Environment`
3. `Learn/Rendering/ClearColor`
4. `Learn/Rendering/FirstTriangle`
5. `Learn/Rendering/BuffersAndBindings`
6. `Learn/Rendering/Shaders`
7. `Learn/Rendering/Textures`
8. `Learn/Rendering/TransformsAndCamera`
9. `Learn/Rendering/DepthCullingLighting`
10. `Learn/Rendering/FrameLoopAndSynchronization`
11. `Learn/Rendering/First2DScene`
12. `Learn/Rendering/FirstRenderGraph`
13. `Learn/Rendering/StaticGltf`
14. `Learn/Rendering/Debugging`
15. `Learn/Rendering/Shipping`

すべての学習ページに次を明記する:

- 前提ページと次のページ。
- 難易度、対象 backend、Gallery / standalone の実行環境。
- 完全な実行コマンドと期待結果。
- リソース所有者、寿命、破棄順。
- Vulkan / D3D12 差があればその差。
- 典型的な失敗例。
- 実際にビルドされるサンプルへの参照。

## ステージ

### R1 — 導線、整合性、最小サンプル (完了: 2026-07-26)

#### 実装

- `samples/LuxelTriangle/` を追加する。
  - `.csproj`, `Program.cs`, renderer、専用 Slang shader、README。
  - Gallery の `GpuView` / `IGpuScene` に依存しない。
  - Window → backend → surface → clear → triangle → present → resize → shutdown を最小構成で示す。
  - Vulkan / D3D12 を引数で選択できる。
- `Learn/Rendering/Overview`, `Environment`, `ClearColor`, `FirstTriangle` を追加する。
- `Docs/FirstTriangle` は新しい学習経路へ移すか、互換リンクだけを残す。
- Gallery 固有コードには「通常アプリでは使わない」旨を表示する。
- 次の既知不整合を修正する。
  - Slang/DXC を毎回手動導入する古い説明。
  - `shaders/triangle.slang` の Vulkan-only コメント。
  - Framework loop の 6 / 7 phase 表記。
  - `snap` / `e2e` の古いコマンド表記。

#### 完了条件

- 新規 checkout 相当でコンパイル済み shader cache を使ってサンプルがビルドできる。
- Windows Vulkan / D3D12 で triangle を表示できる。
- Linux では少なくとも Vulkan build、可能なら表示まで確認する。
- Docs のコードが private Gallery helper を前提にしない。
- vk / dx の Story play + golden がある。

### R2 — バッファ、バインディング、シェーダ (完了: 2026-07-26)

#### 実装

- `BuffersAndBindings`, `Shaders` を追加する。
- Vertex/index/storage/upload/readback/device-local の用途を比較する。
- C# struct と Slang struct のサイズ、alignment、padding、matrix transpose を実コードで示す。
- root arguments と bindless index の対応を図示する。
- `.slang` の追加、SPIR-V/DXIL、`CompileLuxelShaderCache`、publish を説明する。
- shader compile error と cache mismatch の例を掲載する。
- compute `main` と graphics `vsMain` / `psMain` の命名規約を明記する。

#### 完了条件

- 読者が新しい graphics shader を追加して両 backend 用 cache を生成できる。
- コード例の struct size を単体テストで固定する。
- shader cache の説明が root README と矛盾しない。

### R3 — テクスチャと最小3D (完了: 2026-07-26)

#### 実装

- `Textures`, `TransformsAndCamera`, `DepthCullingLighting` を追加する。
- サンプルを段階化する。
  - Textured quad。
  - Indexed cube。
  - Model/View/Projection。
  - Resize 時の aspect 更新。
  - Depth、front face、culling。
  - 最小 directional light。
- sRGB / linear、alpha、UV 原点、filter、address mode、row pitch、upload lifetime を説明する。
- Luxel の3D規約を固定する。
  - 軸、handedness、単位、clip/depth range、winding、matrix multiplication order、normal transform。

#### 完了条件

- C# と Slang の完全な対応例がある。
- vk / dx で期待画像が一致するか、許容差と理由を明記する。
- resize と aspect ratio を play で検証する。

### R4 — フレーム同期とRenderGraph (完了: 2026-07-26)

#### 実装

- `FrameLoopAndSynchronization`, `FirstRenderGraph` を追加する。
- `SubmitAndWait` が入門用であり、本番フレームで stall になることを説明する。
- fence、frame-in-flight、resource reuse、present、vsync/frame pacing の責務を説明する。
- 直接描画の1パスを RenderGraph へ移し、次に transient texture と post-process を追加する。
- external / transient、pass dependency、culling、aliasing、barrier、resize、所有権を説明する。
- DevTools の graph 表示を使ったデバッグ手順を追加する。

#### 完了条件

- 最初の RenderGraph 例が未定義 helper なしで読める。
- 1 pass → post-process の差分が追える。
- validation error の代表例がある。

### R5 — 2D、glTF、デバッグ、出荷

#### 実装

- `First2DScene` を追加し、`Scene2D`, `RetainedCanvas`, UI `Canvas2D`, Skia の選択表を掲載する。
- `Demos/2D` と `Demos/TwoD` を互換リンクを保ちながら一方へ統一する。
- `StaticGltf` を追加し、静的1モデルから始めて ECS / animation / skin / morph へのリンクを分離する。
- `Debugging` に次を含める。
  - backend/device/queue が見つからない。
  - `.spv` / `.dxil` 欠落または stale cache。
  - 真っ黒、裏面、depth逆、matrix転置、texture上下反転、sRGB、row alignment。
  - resize 後の停止、asset URI、publish 後の欠落、GPU待ちによるstall。
- `Shipping` に publish、shader/assets 同梱、backend別 smoke test を記載する。
- `samples/LuxelRange/README.md` を追加し、3D capstone への導線を作る。

#### 完了条件

- 初心者経路の全ページに前後リンクがある。
- 静的 glTF 例はアニメーション/ECSなしでも理解できる。
- publish 出力を別ディレクトリから起動する smoke test がある。

### R6 — ドキュメント品質の自動検証

#### 実装

- Story source の制約を明文化し、private helper を「完全なソース」として表示しない。
- 学習ページのコードは、可能な限り `samples/` の実ファイルを単一の正として表示する仕組みにする。
- internal `story:` link、見出しリンク、前後リンクをテストする。
- 対象サンプルを solution / CI 相当のビルドへ含める。
- ページ metadata として difficulty、environment、backend、prerequisites を導入するか、少なくとも統一表示コンポーネントを作る。
- 巨大な `DocsGpu.cs` を学習単位へ分割する。

#### 完了条件

- リンク切れとサンプルコンパイル失敗が自動テストで検出される。
- Docs とサンプルでコードの二重管理をしない。
- Gallery search から triangle、texture、camera、render graph、glTF、blank screen など初心者語彙で到達できる。

## 非目標

- 新しいレンダリング機能そのものの追加。
- deferred rendering、SSAO、TAA、IBL など高度な機能の実装。
- 既存 Demos をすべてチュートリアル形式へ書き換えること。
- macOS backend の追加。

不足機能がチュートリアルを成立させるために必要と判明した場合は、このタスクへ無制限に含めず、別タスクとして起票する。

## 検証

各ステージ共通:

```powershell
dotnet build Luxel.slnx --no-restore
dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --no-build
dotnet run --project src/Luxel.Gallery -- vk e2e
dotnet run --project src/Luxel.Gallery -- dx e2e
```

追加確認:

- `samples/LuxelTriangle` の vk / dx 起動。
- shader cache がない・古い・一部欠落した場合のエラーメッセージ。
- resize、minimize/restore、終了時 dispose。
- publish 出力に shader と tutorial asset が同梱されること。
- `git diff --name-only -- goldens` で意図した golden 以外が変わっていないこと。

## 実装時の注意

- 初心者ページでは説明のための省略コードを「完全なコード」と呼ばない。
- Gallery harness と standalone app の責務を混ぜない。
- API名・CLI・shader cache 手順は実リポジトリで検証してから記載する。
- backend差を隠さず、共通部分と固有部分を分ける。
- Docsは現在形で書き、完了後はこのタスクMDを削除する。
