using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — 入門章 (GettingStarted / Architecture)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsHome
{
    [Story("Docs/GettingStarted", Order = 0)]   // 章立て: Docs を先頭、入門を最初に
    public static Widget GettingStarted(StoryContext ctx)
    {
        Signal<int> count = ctx.Signal("count", 0, "カウンタの現在値 (± ボタンと連動)");
        ctx.Play(static d => d.Snap());
        Widget counter = HStack(8)[
            Button(_ => { count.Value--; ctx.Log("counter: -1"); }, "-"),
            Text($" {count} ", 20, vAlign: Align.Center),
            Button(_ => { count.Value++; ctx.Log("counter: +1"); }, "+")];

        RichTextEditor doc = Docs(ctx, $$"""
            # Luxel を始める

            Luxel は [Sebastian Aaltonen の *No Graphics API*](https://www.sebastianaaltonen.com/blog/no-graphics-api) の設計を C# で実装した薄いグラフィックエンジンです。最新のバインドレス GPU が備える機能 (64bit ポインタ / bindless / dynamic rendering / stage バリア) の上に、ディスクリプタセットや PSO 爆発のない薄い API を構築し、その上に 2D ベクター・宣言的 UI・アニメーション・レンダーグラフを積み上げています。

            - **バックエンド**: Vulkan 1.3 (一次) + DirectX 12 (二次)。`IGpuBackend` 抽象で切替
            - **シェーダ**: Slang で記述し、SPIR-V (Vulkan) と DXIL (D3D12) に併存コンパイル
            - **核心**: 全パイプライン共通の固定レイアウト = 8B push 定数 (ルート引数の GPU アドレス) + bindless heap。シェーダはルート引数構造体への単一ポインタを受け取る

            ## 必要環境

            - .NET SDK (net10.0)
            - Vulkan 対応 GPU/ドライバ (`vulkan-1.dll`)
            - `slangc` — 本リポジトリは `tools/slang/` に standalone Slang を配置して使用

            ## ビルドと Gallery の起動

            機能の実例・ドキュメント・回帰テストはすべてこの **Gallery** に集約されています:

            ```powershell
            dotnet build
            dotnet run --project src/Luxel.Gallery -- vk           # この Gallery (実ウィンドウ)
            dotnet run --project src/Luxel.Gallery -- vk snap      # スナップショット回帰 (--update で golden 更新)
            dotnet run --project src/Luxel.Gallery -- vk bench "Controls/Button/Counter" 300 --type
            dotnet test                                            # ユニットテスト
            ```

            `vk` を `dx` に変えると DirectX 12 で動きます。両バックエンドは**ピクセル一致**が開発規律です (golden はバックエンド別に保持)。

            ## 最初の UI

            宣言的 UI はベアファクトリ + indexer の DSL で書きます。下のカウンタは本物です — クリックすると右の Log パネルに出て、値も動きます:

            {{counter}}

            ```csharp
            Signal<int> count = ctx.Signal("count", 0);   // knob にもなる状態
            Widget ui = HStack(8)[
                Button(_ => count.Value--, "-"),
                Text($" {count} ", 20, vAlign: Align.Center),
                Button(_ => count.Value++, "+")];
            ```

            `Signal<T>` が状態、`Button(...)` などのファクトリが widget、子は `[...]` で入れ子にします。Signal を読むテキストは変更時に自動で部分更新されます。

            ## ドキュメントの歩き方

            - 全体像とレイヤ構成 → [Docs/Architecture](story:Docs/Architecture)
            - GPU 描画の最初の一歩 (写経用) → [Docs/FirstTriangle](story:Docs/FirstTriangle)
            - コントロールの型見本 (Variant/Intent/API 表) → [Docs/Button](story:Docs/Button)
            - この docs ページ自体の書き方 → [Docs/Authoring](story:Docs/Authoring)
            - サイドバーの **GPU / 2D / 3D / RenderGraph / Animation** 章に、各サブシステムの動くデモが並んでいます。左上の検索欄は docs 本文の全文検索です

            > [!TIP]
            > `Ctrl+D` でライト/ダークテーマを切り替えられます。右パネルの Knobs はストーリーが公開している調整パラメータです — このページのカウンタも編集できます。
            """, toc: true, fences: DocsFences);
        return WithDocFonts(doc);   // 日本語/絵文字フォールバック + ハイライト + mermaid widget
    }

    [Story("Docs/FirstTriangle", Order = 2)]
    public static Widget FirstTriangle(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # はじめての GPU 描画

        UI ([Docs/GettingStarted](story:Docs/GettingStarted)) の次は GPU です。Luxel の描画は常に同じ 4 手 — **確保 (Malloc/CreateRenderTarget) → パイプライン (Slang) → コマンド記録 → Submit** — で、ディスクリプタもレイアウトオブジェクトもありません。回る三角形 1 枚でその全部が出てきます。

        {{StoryRef(ctx, "Demos/3D/Triangle")}}

        ## 1. 頂点バッファ = ただのメモリ

        `device.Malloc(bytes, GpuMemoryKind.HostMapped)` で確保したバッファは `Span<T>` で直接書けます。頂点フォーマット宣言はありません — シェーダが bindless バッファを `SV_VertexID` で読むだけです:

        ```csharp
        GpuBuffer vb = device.Malloc(3 * 32, GpuMemoryKind.HostMapped);   // 3 頂点 × 32B
        Span<Vertex> v = vb.Span<Vertex>(3);
        v[0] = new Vertex { Px = 0, Py = 0.7f, R = 1, A = 1 };   // CPU から直接書く
        ```

        ## 2. シェーダとパイプライン

        シェーダは Slang (`shaders/triangle.slang`) で書き、ビルドが SPIR-V と DXIL の両方へコンパイルします。ルート引数はバッファの **bindless index を積むだけ**の 8B push constants:

        ```csharp
        GpuPipeline pipeline = device.CreateGraphicsPipeline(
            GpuShaderCode.Load("triangle"), GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        ```

        ## 3. 記録して投げる

        コマンドバッファはメソッドチェーンで記録し、`SubmitAndWait` で完結します。バリアは「ステージ → ステージ」の 1 種類だけです:

        ```csharp
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        cmd.BeginRendering(target, null, 0.09f, 0.1f, 0.12f, 1)   // RT 直指定 (render pass オブジェクト無し)
           .SetGraphicsPipeline(pipeline)
           .SetRootArguments(new DrawArgs { VertexBufferIndex = vb.BindlessIndex })
           .Draw(3)
           .EndRendering()
           .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
           .CopyTextureToBuffer(target, readback);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
        ```

        ## 全体を写経する

        上のデモの実ソースがこれです (Gallery の `IGpuScene` 規約 — ctor は保持のみ、確保は Init):

        {{StorySource("Demos/3D/Triangle")}}

        ## 次の一歩

        - 2D を 1 行で描きたい → `Canvas2D(w, h, draw: s => s.FillCircle(...))` ([Demos/2D/Shapes](story:Demos/2D/Shapes))
        - 深度/ブレンドの状態 → [Demos/3D/Depth](story:Demos/3D/Depth) / [Demos/3D/Blend](story:Demos/3D/Blend)
        - GpuDevice の全体像 (bindless/バリア/Slang の設計) → [Docs/GpuDevice](story:Docs/GpuDevice)
        - パスが増えてきたら → [Docs/RenderGraph](story:Docs/RenderGraph)
        """, toc: true, fences: DocsFences));

    [Story("Docs/Architecture", Order = 1)]
    public static Widget Architecture(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # アーキテクチャ

        Luxel は「薄い GPU 抽象の上に、独立したサブシステムを積む」構成です。各レイヤは下のレイヤだけに依存し、横のレイヤ (例: RenderGraph と Resources) は互いを知りません。

        ```mermaid
        flowchart TB
        app[アプリ / Gallery / Framework] --> controls[Luxel.Controls]
        app --> rg[Luxel.RenderGraph]
        app --> ecs[Luxel.Ecs + AssetRuntime]
        controls --> ui[Luxel.UI]
        ui --> anim[Luxel.Animation]
        ui --> typo[Luxel.Typography]
        ui --> twod[Luxel.TwoD]
        rg --> gpu[Luxel — GpuDevice]
        ecs --> rg
        twod --> gpu
        gpu --> vk(Luxel.Vulkan)
        gpu --> dx(Luxel.D3D12)
        ```

        ## GPU 土台

        `Luxel` (コア) が GpuDevice / バッファ / テクスチャ / パイプライン / コマンドの薄い抽象を提供し、`Luxel.Vulkan` と `Luxel.D3D12` が実装します。全パイプラインが **固定レイアウト** (8B push 定数 + bindless heap) を共有するため、ディスクリプタセットの管理も PSO のバリアントも存在しません。シェーダは Slang で 1 回書き、SPIR-V / DXIL の両方へコンパイルされます。

        ## 2D とテキスト

        `Luxel.TwoD` は compute ベースのベクターラスタライザ (三角形分割なし) と保持型キャンバス (RetainedCanvas)。`Luxel.Typography` は HarfBuzz シェーピング + 自前 TextLayout、`Luxel.Typography.Icu` が ICU セグメンタを差し込みます。

        ## UI とコントロール

        `Luxel.UI` が宣言的 DSL (ベアファクトリ + indexer)、Signal/Effect の反応系、単一パスレイアウト。`Luxel.Controls` が Button から RichTextEditor までのコントロール群、`Luxel.UI.Tailwind` が Tailwind カラーパレット (Tw)、`Luxel.Document` + `Luxel.Highlight.TextMate` + `Luxel.Diagram` + `Luxel.MathText` がこの docs ページを支えるドキュメントスタックです。

        ## モーション

        `Luxel.Animation` が Curve × Tween の 2 段分解による中核 IR。ターゲットアダプタ (`.UI` = Signal、`.TwoD` = RetainedCanvas、`.ThreeD` = ECS) が書き込み先を分離します。実例はサイドバーの Animation 章へ ([Demos/Animation/Tween](story:Demos/Animation/Tween) など)。

        ## 3D / レンダーグラフ / リソース

        `Luxel.Ecs` (Friflo ラッパ) + `Luxel.Assets`/`Luxel.AssetRuntime` が 3D シーンと抽出、`Luxel.RenderGraph` が Setup/Compile/Execute 三相の scene-agnostic なパス合成 ([Demos/RenderGraph/Blur](story:Demos/RenderGraph/Blur))。`Luxel.Resources` + `Luxel.Imaging` + `Luxel.Gltf` が (型, uri) キーのリソース DAG を提供します。

        ## ランタイムとツール

        `Luxel.Platform` (Win32 窓 / スワップチェーン / TSF IME / XAudio2)、`Luxel.Input`、`Luxel.Audio`、`Luxel.Framework` (ホストビルダー + シーン遷移)、そして `Luxel.DevTools` (別窓デバッガ + HTTP DebugServer)。この Gallery (`Luxel.Gallery`) 自体が Luxel UI で書かれたドッグフーディングアプリです。

        ## プロジェクト一覧

        | プロジェクト | 役割 |
        | --- | --- |
        | Luxel / Luxel.Vulkan / Luxel.D3D12 | GPU 抽象とバックエンド |
        | Luxel.TwoD | 2D ベクターラスタライザ + 保持型キャンバス |
        | Luxel.Typography (+ .Icu) | テキストレイアウト / シェーピング / ICU |
        | Luxel.UI (+ .Generators, .Tailwind) | 宣言的 UI / signals / ソースジェネレーター |
        | Luxel.Controls | コントロール群 + docs 基盤 (Kit) |
        | Luxel.Document (+ Highlight.TextMate, Diagram, MathText) | ドキュメントモデル / ハイライト / 図 / 数式 |
        | Luxel.Animation (+ .UI, .TwoD, .ThreeD) | アニメーション IR + ターゲットアダプタ |
        | Luxel.Ecs (+ .Signal) | ECS (Friflo) + signal 連携 |
        | Luxel.RenderGraph | パス合成 / transient aliasing / 自動バリア |
        | Luxel.Resources (+ Imaging, Assets, AssetsGpu, AssetRuntime, Gltf) | リソース DAG / 画像 / glTF / 3D 抽出 |
        | Luxel.Platform / Luxel.Input / Luxel.Audio | Win32 / 入力 / 音声 |
        | Luxel.Framework (+ Scene.UI) | アプリ骨格 / シーン遷移 |
        | Luxel.DevTools (+ .App) | デバッガ / HTTP DebugServer |
        | Luxel.Gallery | この Gallery (docs + デモ + snap/bench) |

        ## vk / dx ピクセル一致という規律

        すべての描画機能は Vulkan と DirectX 12 の両方で動き、snap 回帰は**バックエンド別の golden** と比較します (SPIR-V/DXIL のコード生成差で AA の LSB が揺れるため)。新機能はどちらか一方で「たまたま動く」ことを許さない — これが薄い抽象を薄いまま保つための開発規律です。
        """, toc: true, fences: DocsFences));
}
