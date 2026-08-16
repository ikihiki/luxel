using static Luxel.Gallery.Story;
namespace Luxel.Gallery.Stories;

/// <summary>Luxel のレイヤ構成とプロジェクト全体像。</summary>
[StoryMeta("Internals")]
public static class DocsHome
{
    [Story]
    public static StoryResult Architecture(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());   // 新スタック: 全体像 mermaid 図の描画 golden (ライブ埋め込み無し=安全)
        return $$"""
        # アーキテクチャ

        {{Toc()}}

        Luxel は「薄い GPU 抽象の上に、独立したサブシステムを積む」構成です。各レイヤは下のレイヤだけに依存し、横のレイヤ (例: RenderGraph と Resources) は互いを知りません。下図は native WebGPU を明示 opt-in lowering として加える目標構成を含みます。

        ```mermaid
        flowchart TB
        app[アプリ / Gallery / Framework] --> controls[Luxel.Controls]
        app --> rg[Luxel.Graphics.RenderGraph]
        app --> ecs[Luxel.Ecs + AssetRuntime]
        app --> terminalui[Luxel.Terminal.UI]
        controls --> ui[Luxel.UI]
        terminalui --> ui
        terminalui --> terminal[Luxel.Terminal]
        terminal --> pty[ConPTY / Unix PTY]
        ui --> anim[Luxel.Animation]
        ui --> typotwod[Luxel.Typography.TwoD]
        typotwod --> typo[Luxel.Typography]
        typotwod --> twod[Luxel.Graphics.TwoD]
        rg --> gpu[Luxel.Graphics — GpuDevice]
        ecs --> rg
        twod --> gpu
        gpu --> vk(Luxel.Graphics.Vulkan)
        gpu --> dx(Luxel.Graphics.DirectX12)
        gpu --> wgpu[Luxel.Graphics.WebGPU — native opt-in target]
        ```

        ## GPU 土台

        `Luxel.Graphics` が GpuDevice / resource / argument block / pass / dependency の portable semantics を提供し、各 backend が native mechanism へ lower します。Vulkan と DirectX 12 は bindless heap、GPUVA、push/root constants、native barrier を fast path として維持し、native WebGPU は logical refs、fixed bind groups、frame-local argument ring、pass/encoder segmentation を使う明示 opt-in backend として加える方針です。shader package は entry/metadata と SPIR-V / DXIL / portable WGSL artifact を束ねます。native WebGPU は明示 opt-in backend として扱います。

        ## 2D とテキスト

        `Luxel.Graphics.TwoD`はbackend-neutralな2D契約、computeベースのGPUベクターラスタライザ、保持型キャンバスを提供します。`Luxel.Typography`はGPU非依存のHarfBuzzシェーピング + 自前TextLayout、`Luxel.Typography.TwoD`はScene2D描画adapter、`Luxel.Typography.Icu`はICUセグメンタを提供します。

        ## UI とコントロール

        `Luxel.UI` が宣言的 DSL (ベアファクトリ + indexer)、Signal/Effect の反応系、単一パスレイアウト。`Luxel.Controls` が Button から RichTextEditor までのコントロール群、`Luxel.UI.Tailwind` が Tailwind カラーパレット (Tw)、`Luxel.Document` + `Luxel.Highlight.TextMate` + `Luxel.Diagram` + `Luxel.MathText` がこの docs ページを支えるドキュメントスタックです。

        ## 端末

        `Luxel.Terminal`がUI非依存のVT/ANSI parser、screen、scrollback、入力encode、sessionを提供し、`.Windows`のConPTYと`.Linux`のUnix PTYを`ITerminalPty`の後ろへ分離します。`Luxel.Terminal.UI`の`TerminalView`は`Luxel.Controls`に依存せず、font fallback、selection、clipboard、IME、resize reflowを担当します。導入と調整方法は [Controls/Terminal/Overview](story:Controls/Terminal/Overview) へ。

        ## モーション

        `Luxel.Animation` が Curve × Tween の 2 段分解による中核 IR。ターゲットアダプタ (`.UI` = Signal、`.TwoD` = RetainedCanvas、`.ThreeD` = ECS) が書き込み先を分離します。実例はサイドバーの Animation 章へ ([TweenSample](story:Learn/Animation/TweenSample) など)。

        ## 3D / レンダーグラフ / リソース

        `Luxel.Ecs` (Friflo ラッパ) + `Luxel.Assets`/`Luxel.AssetRuntime` が 3D シーンと抽出、`Luxel.Graphics.RenderGraph` が Setup/Compile/Execute 三相の scene-agnostic なパス合成 ([BlurSample](story:Learn/Graphics/RenderGraph/BlurSample))。`Luxel.Resources` + `Luxel.Imaging` + `Luxel.Assets.Gltf` が (型, uri) キーのリソース DAG を提供します。

        ## ランタイムとツール

        `Luxel.Platform` + `.Windows` / `.Silk` (窓 / クリップボード / IME / 低レベル入力)、`Luxel.Input` + `.XInput`、`Luxel.Audio` + `.Windows`、`Luxel.Framework.Game` (ホストビルダー + シーン遷移)、そして `Luxel.DevTools` (別窓デバッガ + HTTP DebugServer)。この Gallery (`Luxel.Gallery`) 自体が Luxel UI で書かれたドッグフーディングアプリです。

        ## プロジェクト一覧

        | プロジェクト | 役割 |
        | --- | --- |
        | Luxel.Graphics / Luxel.Graphics.Vulkan / Luxel.Graphics.DirectX12 / Luxel.Graphics.WebGPU (追加方針) | portable GPU semantics と backend lowering |
        | Luxel.Graphics.TwoD | 2D ベクターラスタライザ + 保持型キャンバス |
        | Luxel.Typography (+ .Icu) / Luxel.Typography.TwoD | GPU非依存レイアウト・シェーピング・ICU / Scene2D描画adapter |
        | Luxel.UI (+ .Generators, .Tailwind) | 宣言的 UI / signals / ソースジェネレーター |
        | Luxel.Controls | コントロール群 + docs 基盤 (Kit) |
        | Luxel.Terminal (+ .UI, .Windows, .Linux) | VT/ANSI端末コア / UI widget / ConPTY・Unix PTY backend |
        | Luxel.Document (+ Highlight.TextMate, Diagram, MathText) | ドキュメントモデル / ハイライト / 図 / 数式 |
        | Luxel.Animation (+ .UI, .TwoD, .ThreeD) | アニメーション IR + ターゲットアダプタ |
        | Luxel.Ecs (+ .Signal) | ECS (Friflo) + signal 連携 |
        | Luxel.Graphics.RenderGraph | パス合成 / transient aliasing / 自動バリア |
        | Luxel.Resources (+ Imaging, Assets, AssetsGpu, AssetRuntime, Gltf) | リソース DAG / 画像 / glTF / 3D 抽出 |
        | Luxel.Platform (+ .Windows, .Silk) | ウィンドウ / クリップボード / IME / 低レベル入力 |
        | Luxel.Input (+ .XInput) | アクションマップ / リバインド / Windowsゲームパッド入力 |
        | Luxel.Audio (+ .Windows) | 音声API / XAudio2バックエンド |
        | Luxel.Framework.Game (+ Scene.UI) | アプリ骨格 / シーン遷移 |
        | Luxel.DevTools (+ .App) | デバッガ / HTTP DebugServer |
        | Luxel.Gallery | この Gallery (docs + デモ + e2e/bench) |

        ## backend 回帰という規律

        既存の描画機能は Vulkan と DirectX 12 の両方で動き、e2e回帰は**バックエンド別の golden** と比較します (SPIR-V/DXIL のコード生成差で AA の LSB が揺れるため)。WebGPU 対応を表明する機能は同じ backend-neutral story を WGSL/manifest、limits、diagnostics を含む専用ゲートで検証します。どれか一つの backend で「たまたま動く」ことを許さず、差を lowering と capability に閉じ込めることが、薄い抽象を保つ開発規律です。
        """;
    }
}
