# Luxel — 「No Graphics API」C# 実装

## 静的Gallery / GitHub Pages

ネイティブGalleryを操作可能な正としつつ、同じ`StoryRegistry.All`からGitHub Pages向けの静的HTML版を生成できる。
Story preview、`StoryRef`、通常のWidget埋め込み、Mermaid、数式はoffscreen VulkanでPNG化され、静的版では操作できないことを明示する。

```bash
# Linux/CIではMesa lavapipe（mesa-vulkan-drivers）を用意する
dotnet run --project src/Luxel.Gallery.Site -- artifacts/gallery-site

# 対象を絞って確認する場合
dotnet run --project src/Luxel.Gallery.Site -- artifacts/gallery-site --filter Controls/Button
```

生成物は相対URLだけを使い、`.nojekyll`、hash routing、sidebar、全文検索、light/dark themeを含む。
既存Vulkan goldenを代表previewとして優先し、不足画像はVulkanで決定的に生成する。`RealWindowOnly`や描画失敗は
黙って省略せず、明示的な状態cardとして出力する。`luxel-ui` placeholder、local参照切れ、root absolute URLはexport時に検証する。

生成先の`artifacts/`はGitへcommitしない。`.github/workflows/deploy-pages.yml`は`main`へのpushまたは手動実行で
Ubuntu/lavapipe上のtestとexportを行い、公式GitHub Pages actionsでdeployする。初回のみrepositoryの
**Settings → Pages → Source**を**GitHub Actions**へ設定する。

[Sebastian Aaltonen の *No Graphics API*](https://www.sebastianaaltonen.com/blog/no-graphics-api)
の設計を C# で提供する薄いグラフィックエンジン。最新のバインドレス GPU が備える機能
(64bit ポインタ / bindless / dynamic rendering / stage バリア) の上に、ディスクリプタセットや
PSO 爆発のない薄い API を構築する。

- **バックエンド:** Vulkan 1.3 (一次) + DirectX 12 (二次)。`IGpuBackend` 抽象で切替。
- **シェーダ:** Slang で記述し、SPIR-V (Vulkan) と DXIL (D3D12) に併存コンパイル。
- **核心:** 全パイプライン共通の固定レイアウト = 最大 192B のルート引数 (4B単位のraw bytes) + bindless heap。
- **規律:** すべての描画機能は vk/dx の両バックエンドでピクセル一致を検証する。

## 必要環境

- .NET SDK (net10.0)
- Vulkan 対応 GPU/ドライバ (`vulkan-1.dll`)

通常ビルドは Git 管理されたコンパイル済みシェーダを使うため、Slang/DXC のローカル導入は不要。
シェーダを変更してキャッシュを再生成するときだけ、下記の MSBuild ターゲットがツールを自動取得する。

## Linux リモートデスクトップ開発

Coder/Mux の Linux workspace では、Xvfb + openbox + noVNC の開発用 desktop を用意できる。
Luxel の Silk.NET/X11 backend と Vulkan WSI を開発・検証でき、環境自体の baseline は
`vkcube` でも確認できる。

```bash
eng/desktop/install.sh
eng/desktop/start.sh
eng/desktop/run-vkcube.sh
eng/desktop/url.sh
```

出力された URL は Coder の認証付き preview。VNC は TCP port を開かず private Unix socket を使い、
追加 password は使用しない。運用、healthcheck、screenshot、停止方法は `eng/desktop/README.md` を参照。

## ビルドと実行

ドキュメント・機能の実例 (デモストーリー)・回帰テストはすべて **Gallery** に集約されている:

```powershell
dotnet build
dotnet run --project src/Luxel.Gallery -- vk            # Gallery (実ウィンドウ。dx も可)
dotnet run --project src/Luxel.Gallery -- vk e2e        # play + golden 回帰 (--update で更新)
dotnet run --project src/Luxel.Gallery -- vk bench "Button/Counter" 300 --type
dotnet test                                             # ユニットテスト
```

Gallery のサイドバー **Docs 章**が本体ドキュメント (入門 → アーキテクチャ → サブシステム別 →
貢献者向け)。**GPU / 2D / 3D / RenderGraph / Animation 章**が動くデモ。左上の検索欄で
docs 本文を全文検索できる。

### Linux headless Vulkan

Linux では `VulkanBackend.Create()` が既定で WSI/swapchain extensions を読み込まない headless mode を
選ぶ。offscreen GPU処理を明示する場合は次の options を利用する。

```csharp
using var backend = VulkanBackend.Create(new VulkanBackendOptions
{
    Presentation = VulkanPresentationMode.Disabled,
});
```

lavapipe を使った最小回帰テスト:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
  dotnet test tests/Luxel.Vulkan.Tests/Luxel.Vulkan.Tests.csproj
```

## 機能ハイライト

各節の詳細と実例は Gallery 内 Docs 章の該当ページへ。

- **GPU 抽象** — 固定レイアウト + bindless、Slang 統一シェーダ、stage バリアのみの同期、
  深度/ブレンド/テクスチャ (→ Docs/GpuDevice)
- **2D ベクター** — compute ラスタライザ (三角形分割なし)、EvenOdd/ストローク/日本語ベクター
  テキスト、Camera2D スムーズズーム、保持型キャンバスの増分更新 (→ Docs/TwoD)
- **レンダーグラフ** — Setup/Compile/Execute 三相、transient aliasing、デッドパスカリング、
  自動バリア。scene-agnostic (→ Docs/RenderGraph)
- **3D + ECS** — Friflo ECS + Transform 伝播 + IRenderExtractor、forward/bloom/shadow map/
  world-space UI (→ Docs/ThreeD)
- **宣言的 UI** — ベアファクトリ + indexer の DSL、signals 細粒度更新、単一パスレイアウト、
  エラー境界 (→ Docs/UI)。コントロール 40 超 + CompositeControl (→ Docs/Controls)、
  StateStyle/Tailwind utility (→ Docs/Styling)
- **テキストとエディタ** — HarfBuzz + 自前 TextLayout (禁則/Justify/ICU 差し込み)、
  RichDocument + Markdig の WYSIWYG hybrid エディタ、埋め込みブロック (→ Docs/Typography,
  Docs/Editor)
- **アニメーション** — 3 層 IR (Clip/Track/Player) + UI/2D/3D アダプタ、コード DSL、
  CSS @keyframes、Graph/StateMachine、CSS transition 相当の暗黙補間 (→ Docs/Animation,
  Docs/Transitions)
- **ランタイム** — (型,uri) リソース DAG、Win32 窓 + TSF IME、XAudio2、
  LuxelHostBuilder + 7 フェーズループ + UiSurface、ネイティブ DevTools + HTTP DebugServer
  (→ Docs/Resources, Docs/Platform, Docs/Audio, Docs/Framework, Docs/DevTools)

## プロジェクト構成

| プロジェクト | 役割 |
| --- | --- |
| Luxel.Graphics / Luxel.Graphics.Vulkan / Luxel.Graphics.DirectX12 | GPU 抽象とバックエンド |
| Luxel.Graphics.TwoD | 2D ベクターラスタライザ + 保持型キャンバス |
| Luxel.Typography (+ .Icu) | テキストレイアウト / シェーピング / ICU |
| Luxel.UI (+ .Generators, .Tailwind) | 宣言的 UI / signals / ソースジェネレーター |
| Luxel.Controls | コントロール群 + docs 基盤 (Kit) |
| Luxel.Document (+ Highlight.TextMate, Diagram, MathText) | 文書モデル / ハイライト / 図 / 数式 |
| Luxel.Animation (+ .UI, .TwoD, .ThreeD) | アニメーション IR + ターゲットアダプタ |
| Luxel.Ecs (+ .Signal) | ECS (Friflo) + signal 連携 |
| Luxel.RenderGraph | パス合成 / transient aliasing / 自動バリア |
| Luxel.Resources (+ Imaging, Assets, AssetsGpu, AssetRuntime, Gltf) | リソース DAG / 画像 / glTF / 3D 抽出 |
| Luxel.Platform (+ .Windows, .Silk) | ウィンドウ / クリップボード / IME / 低レベル入力 |
| Luxel.Input (+ .XInput) | アクションマップ / リバインド / Windowsゲームパッド入力 |
| Luxel.Audio (+ .Windows) | 音声API / ミキサ / XAudio2バックエンド |
| Luxel.Framework (+ Scene.UI) | アプリ骨格 / シーン遷移 / UiSurface |
| Luxel.DevTools (+ .App) | デバッガ / HTTP DebugServer / ネイティブ DevTools |
| Luxel.Gallery | ドキュメント + デモ + e2e/bench (このリポジトリの玄関) |

初心者向けGPU経路は Gallery の `Learn/Rendering/Overview` から始まり、`FirstTriangle` →
`BuffersAndBindings` → `Shaders` の順で、standalone sample、bindless buffer ABI、shader cache更新を扱う。
`GpuMemoryKind` はCPU write向け`HostMapped`、GPU専用`DeviceLocal`、CPU readback向け`HostCached`の3種類。

## Slang シェーダキャッシュ

`shaders/compiled/` の SPIR-V/DXIL は Git 管理し、通常の `dotnet build` では入力ハッシュを検証して
各プロジェクトの出力へコピーする。同じシェーダをプロジェクトごと・ビルドごとに再コンパイルしない。
`.slang`、コンパイラ版、プロファイルのいずれかがキャッシュと違う場合、通常ビルドは説明付きで失敗する。

filenameが`compute*.slang`または`raster2d_*.slang`ならcompute entry `main`として分類し、
`<name>.spv` + `<name>.dxil`を生成する。それ以外はgraphics entry `vsMain` / `psMain`として、
`<name>.spv` + `<name>.vs.dxil` + `<name>.ps.dxil`を生成する。

シェーダ変更後はリポジトリルートで次を1回実行し、ソースと `shaders/compiled/` を一緒にコミットする:

```powershell
dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
```

このターゲットは固定バージョンの Slang と DXC を公式リリースから `tools/` へ自動取得し、SHA-256 を
検証してから SPIR-V と DXIL を更新する。2回目以降は MSBuild の `Inputs` / `Outputs` により変更のない
シェーダをスキップする。`tools/` は大きなローカルツールキャッシュなので Git には含めない。

自動取得の対応環境は `linux-x64`、`win-x64`、`win-arm64`。独自配置を使う場合は
`/p:SlangcPath=...` を指定できるが、DXIL 生成用 DXC も利用可能にする必要がある。

## 共有フォント

Gallery、LuxelCavern、Linux / CI テストは、Git 管理された `assets/fonts/` の BIZ UDGothic Regular/Bold と
UDEV Gothic Regular を共有する。`assets/Luxel.FontAssets.targets` が Gallery とテストの出力・publishへフォントとOFLをコピーし、
LuxelCavernは同じRegularをCore.dllへ埋め込む。`VectorFont.LoadSystem*` はシステムフォントを優先し、存在しない場合だけ出力の
`fonts/BIZUDGothic-Regular.ttf`へフォールバックする。Linux用HarfBuzzSharp / SkiaSharp native assetsも対象プロジェクトから条件付き参照する。

## Khronos サンプルアセット

LuxelRangeが使用する`Fox.glb`と静的Galleryが使用する`Box.gltf` / `Box0.bin`は、初回ビルド時にKhronosGroupの公式
`glTF-Sample-Assets`リポジトリの固定コミットから`tools/khronos-samples/`へ自動取得する。
取得ファイルは SHA-256 を毎ビルド検証し、2回目以降はローカルキャッシュを再利用する。
`tools/` を削除すれば次回ビルド時に再取得される。
モデルのライセンスも同じ固定コミットから取得してSHA-256を検証する。Foxは`licenses/Fox-LICENSE.md`としてbuild/publishへ、
Boxは静的Galleryの`licenses/Box-LICENSE.md`としてPages成果物へ含める。

## 外部アセットと Git submodule

GLB とフォントには Git submodule を使わない。`glTF-Sample-Assets` やフォントの上流リポジトリは、必要な数ファイルに対して全体が非常に大きく、
submodule はファイル単位の依存や clone 時の sparse checkout を保証しない。また `git submodule update --init`、CI設定、更新手順という別の運用が増える。
小さいレビュー済みフォントは `assets/fonts/` へ直接Git管理し、Fox.glbのような外部サンプルは固定コミットURL + SHA-256 + ライセンスで
`tools/`へキャッシュする。上流ソースツリー自体を開発・検証する必要が生じた場合に限り、submoduleを再検討する。
