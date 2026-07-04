# Luxel — 「No Graphics API」C# 実装

[Sebastian Aaltonen の *No Graphics API*](https://www.sebastianaaltonen.com/blog/no-graphics-api)
の設計を C# で提供する薄いグラフィックエンジン。最新のバインドレス GPU が備える機能
(64bit ポインタ / bindless / dynamic rendering / stage バリア) の上に、ディスクリプタセットや
PSO 爆発のない薄い API を構築する。

- **バックエンド:** Vulkan 1.3 (一次) + DirectX 12 (二次)。`IGpuBackend` 抽象で切替。
- **シェーダ:** Slang で記述し、SPIR-V (Vulkan) と DXIL (D3D12) に併存コンパイル。
- **核心:** 全パイプライン共通の固定レイアウト = 8B push 定数 (ルート引数) + bindless heap。
- **規律:** すべての描画機能は vk/dx の両バックエンドでピクセル一致を検証する。

## 必要環境

- .NET SDK (net10.0)
- Vulkan 対応 GPU/ドライバ (`vulkan-1.dll`)
- `slangc` — `tools/slang/` に standalone Slang を展開して使用 (下記セットアップ参照)

## ビルドと実行

ドキュメント・機能の実例 (デモストーリー)・回帰テストはすべて **Gallery** に集約されている:

```powershell
dotnet build
dotnet run --project src/Luxel.Gallery -- vk            # Gallery (実ウィンドウ。dx も可)
dotnet run --project src/Luxel.Gallery -- vk snap       # スナップショット回帰 (--update で golden 更新)
dotnet run --project src/Luxel.Gallery -- vk bench "Button/Counter" 300 --type
dotnet test                                             # ユニットテスト
```

Gallery のサイドバー **Docs 章**が本体ドキュメント (入門 → アーキテクチャ → サブシステム別 →
貢献者向け)。**GPU / 2D / 3D / RenderGraph / Animation 章**が動くデモ。左上の検索欄で
docs 本文を全文検索できる。

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
  LuxelHostBuilder + 6 フェーズループ + UiSurface、ネイティブ DevTools + HTTP DebugServer
  (→ Docs/Resources, Docs/Platform, Docs/Audio, Docs/Framework, Docs/DevTools)

## プロジェクト構成

| プロジェクト | 役割 |
| --- | --- |
| Luxel / Luxel.Vulkan / Luxel.D3D12 | GPU 抽象とバックエンド |
| Luxel.TwoD | 2D ベクターラスタライザ + 保持型キャンバス |
| Luxel.Typography (+ .Icu) | テキストレイアウト / シェーピング / ICU |
| Luxel.UI (+ .Generators, .Tailwind) | 宣言的 UI / signals / ソースジェネレーター |
| Luxel.Controls | コントロール群 + docs 基盤 (Kit) |
| Luxel.Document (+ Highlight.TextMate, Diagram, MathText) | 文書モデル / ハイライト / 図 / 数式 |
| Luxel.Animation (+ .UI, .TwoD, .ThreeD) | アニメーション IR + ターゲットアダプタ |
| Luxel.Ecs (+ .Signal) | ECS (Friflo) + signal 連携 |
| Luxel.RenderGraph | パス合成 / transient aliasing / 自動バリア |
| Luxel.Resources (+ Imaging, Assets, AssetsGpu, AssetRuntime, Gltf) | リソース DAG / 画像 / glTF / 3D 抽出 |
| Luxel.Platform / Luxel.Input / Luxel.Audio | Win32 + IME / 入力 / 音声 |
| Luxel.Framework (+ Scene.UI) | アプリ骨格 / シーン遷移 / UiSurface |
| Luxel.DevTools (+ .App) | デバッガ / HTTP DebugServer / ネイティブ DevTools |
| Luxel.Gallery | ドキュメント + デモ + snap/bench (このリポジトリの玄関) |

## セットアップ: tools/ (リポジトリ非含)

シェーダビルドに standalone Slang + DXC が必要 (`slang-llvm.dll` が GitHub の 100MB 制限を
超えるためリポジトリには含めていない)。`tools/slang/` に Slang リリースを展開し、DXIL 出力用に
`dxcompiler.dll` / `dxil.dll` を `tools/slang/bin/` へコピーする。
