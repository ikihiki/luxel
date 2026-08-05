using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0002-Thin-Bindless-Gpu-Abstraction", Order = 73, Toc = true)]
    public static StoryResult Adr0002(StoryContext ctx) => $$"""
        # ADR-0002 — 3D グラフィック API は「薄い bindless 抽象」を自作する

        - **Status**: Accepted
        - **Date**: 2026-07-08 (記録日 — 決定自体はプロジェクト開始時)
        - **Deciders**: ikihiki

        > [!NOTE]
        > この決定の薄い抽象という方向性を維持しつつ、native mechanism を portable semantics と backend fast path に分離する方針を [ADR-0019](story:Internals/ADR/0019-Portable-Gpu-Semantics-WebGPU-Backend) が **Amends** しています。

        ## Context

        Luxel は 2D ベクター・宣言的 UI・アニメーション・レンダーグラフを積み上げる土台として、C#/.NET から使える 3D グラフィック API を必要としていました。要件と力学は次のとおりです:

        - **マルチバックエンド** — Vulkan と DirectX 12 の両方で動かしたい (Windows 主軸だが GPU API に縛られない)
        - **抽象の薄さ** — Vulkan/D3D12 を素で書くとディスクリプタセット・レイアウトオブジェクト・リソース状態遷移・PSO バリアント管理が肥大化し、抽象の *中* が複雑さの置き場になる
        - **現代 GPU 前提でよい** — 対象は bindless / 64bit ポインタ / dynamic rendering / enhanced barriers を備えた最新世代。旧ハードの互換性は要件にない
        - 参照設計として [Sebastian Aaltonen の *No Graphics API*](https://www.sebastianaaltonen.com/blog/no-graphics-api) がある — 「最新 GPU の共通機能に絞れば、グラフィック API 抽象はほぼ消せる」という主張の実証も兼ねる

        ## Decision

        既存のグラフィック抽象ライブラリを使わず、*No Graphics API* 設計の**薄い bindless 抽象を C# で自作**します (`Luxel.Graphics` + `Luxel.Graphics.Vulkan` / `Luxel.Graphics.DirectX12`)。核心は次の 5 点:

        - **固定パイプラインレイアウト** — 全 PSO が「最大192Bのルート引数 (4B単位のraw bytes) + グローバル bindless heap」の 1 レイアウトを共有。ディスクリプタセットも PSO バリアントも存在しない
        - **bindless 一本** — バッファ/テクスチャは作成時に `BindlessIndex` を持ち、シェーダは `g_buffers[index]` / `g_textures[index]` で参照。「どのリソースを使うか」はルート引数内の index が全て
        - **メモリはただのポインタ** — `device.Malloc(bytes, kind)` で確保し、HostMapped は `Span<T>` で直接読み書き (頂点フォーマット宣言なし、頂点プル)
        - **同期は stage バリアのみ** — `Barrier(srcStage, dstStage)` の 1 種類。リソース個別の状態遷移管理を持たない
        - **Slang 統一シェーダ** — 1 ソースを SPIR-V (Vulkan) と DXIL (D3D12) へ併存コンパイル。シェーダはバックエンド非依存

        現在の姿の詳細は [Reference/Guides/GpuDevice](story:Reference/Guides/GpuDevice) と [Internals/Architecture](story:Internals/Architecture) へ。

        ## Alternatives

        - **Vulkan (または D3D12) を素で 1 本** — マルチバックエンド要件を満たさない。また素の API は旧世代互換の概念 (ディスクリプタ管理・render pass・細粒度バリア) を強制し、コードベース全体にその複雑さが漏れる → 却下
        - **既存の抽象ライブラリ (Veldrid / bgfx / WebGPU 系)** — いずれも「最大公約数」設計で、旧世代 GPU に合わせたディスクリプタ/バインディングモデルを保持している。bindless・64bit ポインタ・push 定数直渡しを前提にした薄さは得られず、抽象の上にさらに抽象を重ねることになる。C# ネイティブでない (bgfx/WebGPU) 相互運用コストも負う → 却下
        - **OpenGL** — レガシー。bindless 世代の設計検証という目的自体と矛盾 → 却下
        - **ディスクリプタセットを持つ自作抽象 (従来型レイヤ)** — 自作でも従来型のバインディングモデルを写すと、素の API と同じ複雑さを自分で保守することになる。固定レイアウト 1 本に比べ PSO/レイアウトの組み合わせ管理が復活する → 却下

        ## Consequences

        - ✅ アプリコードはバックエンド分岐なしの 1 本 — 確保 (Malloc) → パイプライン (Slang) → 記録 → Submit の 4 手で描画が完結し、ディスクリプタ管理コードが存在しない
        - ✅ PSO 爆発が構造的に起きない (レイアウトが 1 つ、状態は GpuRasterDesc の宣言のみ)
        - ✅ 抽象が薄いので、その上の RenderGraph / TwoD / UI が GPU の実挙動に素直に載る
        - ⚠️ **最新 GPU 専用** — Vulkan 1.3 + bindless 必須。旧ハード・モバイル・macOS (Metal) は対象外。対応するなら別バックエンド追加という大工事になる
        - ⚠️ 抽象を自作した以上、**vk / dx ピクセル一致**を自分で保証し続ける必要がある — snap 回帰をバックエンド別 golden で持ち、新機能は両方で検証してから完了とする開発規律が生まれた
        - ⚠️ バックエンド固有の罠は自分で吸収する (例: D3D12 の CopyTextureToBuffer 行 256B 整列 → RGBA8 はターゲット幅 64 の倍数、DXIL は validator 1.7 指定)
        - ⚠️ stage バリアのみの単純化は「単純な使用パターン」が前提 — 複雑な並行アクセスパターンが将来必要になったら見直し対象
        """;
}
