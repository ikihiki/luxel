using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsGpu
{
    [Story("Reference/Guides/WebGPU", Order = 17, SampleBundle = "rendering.webgpu-headless")]
    public static Widget WebGpu(StoryContext ctx) => DocNew(ctx, $$"""
        # WebGPU backend 設計ガイド

        このページは native WebGPU backend が満たす**設計契約と対応範囲**を定義します。特定の build や platform が利用可能かどうかは、adapter/device 作成、required feature/limit 検査、代表 story の回帰ゲートで判定します。この文書だけを根拠に「対応済み」または「実装進行中」とは扱いません。

        背景となる決定は [ADR-0019](story:Internals/ADR/0019-Portable-Gpu-Semantics-WebGPU-Backend)、共通 API の入口は [Reference/Guides/GpuDevice](story:Reference/Guides/GpuDevice)、pass 間依存は [Reference/Guides/RenderGraph](story:Reference/Guides/RenderGraph) を参照してください。

        ## 対象と backend 選択

        | 項目 | 契約 |
        | --- | --- |
        | 対象 | native desktop の Windows と Linux。現在はheadless/offscreenのみ |
        | 選択 | `WebGpuBackend.Create()`を明示して`GpuDevice`へ渡すopt-in。既存windowed hostのselectorには未接続 |
        | 既定 | Windows / Linux の既定 backend は変更しない |
        | surface | 未実装。`CreateSurface`は`PlatformNotSupportedException` |
        | 初期スコープ外 | surface/present、browser/WASM、macOS、未検証 RID、未検証 Native AOT |
        | 対応判定 | required feature/limit と shader package を検査し、代表headless/offscreen回帰が通ること |

        WebGPU は「どの環境でも動く互換モード」ではありません。adapter が必要な limit を満たさない、native runtime と binding の ABI が一致しない、対象 shader artifact が無い、といった場合は backend 作成または pipeline 作成で明示的に失敗します。

        `samples/LuxelWebGpuHeadless`は公開`GpuDevice` APIでWGSL compute、storage arenaからのvertex pulling、sampled checkerboardを使うoffscreen triangle、`HostCached` readbackを実行して結果を自己検証します。windowed `LuxelTriangle`へ未実装のselectorを追加せず、現在利用可能なheadless経路だけを示します。

        **現時点の実装制限:** sampled resourceは固定portable ABIです。sampled textureとsamplerは各16個、textureはfilterableなRGBA8/BGRA8・2D・1 mipに限定されます。shader package全体のbinding metadata移行とunbounded runtime arrayは未実装なので、shaderは下記の固定bindingとlogical index loweringを明示する必要があります。`Examples/3D/TexturedQuad`全体をWebGPU対応済みとはまだ扱いません。

        {{SampleBundle("rendering.webgpu-headless")}}

        ## No Graphics API 原則との対応

        WebGPU 上で GPUVA API を字義どおり再現するのではなく、上位層が必要とする意味論を backend ごとに lower します。

        | 原則 / 共通意味論 | Vulkan / DirectX 12 | WebGPU lowering |
        | --- | --- | --- |
        | transient / one-shot command recording | native command buffer/list | command encoder と pass encoder。`Finish` 後は再記録しない |
        | 単一 root argument block | push constants / root 32-bit constants | frame-local uniform/storage argument ring |
        | logical resource reference | descriptor indexing、BDA / descriptor heap、GPUVA | manifest に従う fixed bindings と bind-group cache |
        | vertex pulling | bindless buffer / address 参照 | storage buffer binding と logical ref decode |
        | 明示 render pass | dynamic rendering / render target commands | render-pass descriptor、attachments、load/store |
        | producer/consumer dependency | synchronization2 / transition・UAV barrier | pass/encoder segmentation と usage validation |
        | descriptor 管理を隠す | global bindless heap | backend が bind groups を構築・再利用 |
        | tooling-first | labels、markers、native validation | object labels、error scopes、logical IDs、shader metadata |

        ### portable baseline にしないもの

        WebGPU にない機構を sentinel や暗黙の巨大 emulation layer で見せかけません。次は Vulkan / DirectX 12 の capability-specific fast path であり、portable API の必須条件ではありません。

        - GPU virtual address / buffer device address
        - CPU と GPU が同時利用する永続 map と生 pointer
        - ユーザーが直接書き換える unbounded descriptor heap
        - アプリが発行する native stage/resource barrier
        - unbounded runtime resource arrays
        - extended dynamic blend/depth state

        ## root arguments の fallback

        呼び出し側は最大 **192 bytes** の typed/raw root argument block を `SetRootArguments<T>` 相当の操作で渡す、という意味論を維持します。plain data と resource reference の位置・型・alignment は pipeline の argument manifest が定義します。

        WebGPU baseline は block を frame-local の aligned argument ring へ copyし、dynamic offset または対応する binding から参照する方式です。WebGPU Immediate Data は、仕様、採用 native implementation、binding の三層すべてが保証する範囲でのみ capability-gated optimization として使用します。利用不能でも argument ring 経路で同じ結果にならなければなりません。

        ## logical resource references

        portable resource は opaque allocation、declared usage、stable logical ID と generation で識別します。root arguments 内には native pointer や backend の descriptor index ではなく logical ref を格納し、shader package の manifest が field offset、resource kind、access を記録します。

        - Vulkan: descriptor indexing / BDA へ lower
        - DirectX 12: descriptor heap / GPUVA へ lower
        - WebGPU: pipeline layout に対応する bind group と buffer subrange へ lower

        generation が古い ref、破棄済み resource、宣言 usage と shader access の不一致は submit 後の不定動作にせず、可能な限り pipeline/pass 構築時に診断します。

        ### 現行の固定 WebGPU ABI

        すべてのcompute/graphics pipelineは同じ2 bind-group layoutを使います。

        | group / binding | 内容 |
        | --- | --- |
        | group 0 / binding 0 | 64 MiB buffer arena。computeはread-write、graphicsはread-only |
        | group 0 / binding 1 | 256-byte dynamic root uniform |
        | group 1 / binding 0..15 | filterable `texture_2d<f32>` sampled texture slot |
        | group 1 / binding 16..31 | filtering sampler slot 0..15 |

        `GpuTexture.BindlessIndex`と`GpuSampler.BindlessIndex`はそれぞれ独立したstable logical index `0..15`です。17個目のlive resourceは暗黙にevictせず作成時に失敗し、dispose済みslotはrecorded/in-flight bind groupが参照しなくなってから再利用します。空slotにはbind groupを完全にする内部fallback resourceを置きますが、範囲外logical indexをfallback sampleへ黙って変換してはいけません。

        現行baselineはnon-uniform indexing featureを要求せず、WGSL生成時に固定`switch`へlowerします。`switch`のdefaultは明示的なdiagnostic結果にし、headless sampleは範囲外indexがmagenta sentinelになることを検証します。adapter/device作成時には`maxBindGroups >= 2`、group当たり32 binding、stage当たりsampled texture/sampler各16を検査します。

        ## upload / readback

        共通契約は map 方法ではなくデータフローを表します。

        - **Upload** — CPU から GPU へ。WebGPU は CPU shadow/staging と `writeBuffer` または copy に lower する
        - **DeviceLocal** — GPU の作業領域。CPU pointer を portable contract として公開しない
        - **Readback** — GPU 完了後に CPU が読む。WebGPU は copy 後の async map と scoped lifetime に lower する

        mapped resource を submit しないこと、dirty range、copy alignment、completion 前の readback 禁止は backend が検証します。永続 `Span<T>` を前提にしたコードは portable path ではありません。

        ## 同期 lowering

        共通 API と RenderGraph が表すのは `Barrier(srcStage, dstStage)` という native 命令ではなく、resource access set に基づく **producer/consumer dependency** です。RenderGraph compile は read/write usage の互換性と必要な pass split を決め、backend に lowering plan を渡します。

        WebGPU では同一 usage scope で許されない read/write combination を別 pass または encoder に分割し、WebGPU validation が扱える usage へ変換します。明示 barrier が無いことは同期が不要という意味ではありません。Vulkan / DirectX 12 では同じ dependency がそれぞれ native barrier に変換されます。

        ## shader package

        shader は backend ごとの byte列だけでなく、次をまとめた package として扱います。

        - entry point と stage
        - SPIR-V / DXIL / portable WGSL artifact
        - bind-group/binding、resource kind、access
        - root struct の size/alignment と logical-ref field offsets
        - specialization / override constants
        - required WebGPU features/limits

        WebGPU の正式 artifact は portable WGSL です。undocumented な SPIR-V ingestion には依存しません。C# root struct と WGSL layout の size/alignment が一致しない package、required artifact が欠けた package、adapter limit を超える package は pipeline 作成時に拒否します。

        ## 制限と diagnostics

        | 制限 | 動作 |
        | --- | --- |
        | required feature/limit 不足 | feature/limit 名、required/actual、adapter を含めて fail-fast |
        | shader/layout 不一致 | shader・entry・pipeline label、期待/実値を報告 |
        | stale logical ref / usage 不一致 | logical ID・generation・resource label・pass を報告 |
        | WebGPU usage scope 違反 | 競合 access と必要な pass split を報告 |
        | uncaptured error / device lost | backend、adapter、最後の operation/object label を保持して公開 |
        | native runtime / binding 不一致 | runtime/version/RID と不足 symbol を起動時に報告 |
        | sampled texture / sampler | RGBA8/BGRA8 2D textureとfiltering samplerを各16 slot。17個目、unsupported format、invalid dataは作成時に明示失敗 |

        backend 作成時に adapter 名、backend type、driver/runtime version、limits を記録し、GPU object と pass/pipeline には label を付けます。「device creation failed」の一文だけで終わらせず、どの契約を満たせなかったかを診断の中心にします。

        ## 関連する backend-neutral stories

        WebGPU 専用に描画デモを複製せず、同じ story を backend ごとの回帰に使います。対応判定の代表入口は次です。

        - [Examples/3D/Triangle](story:Examples/3D/Triangle) — root arguments、vertex pulling、render-to-texture
        - [Examples/3D/TexturedQuad](story:Examples/3D/TexturedQuad) — sampled texture と sampler
        - [Examples/3D/Depth](story:Examples/3D/Depth) / [Examples/3D/Blend](story:Examples/3D/Blend) — baked pipeline state
        - [Examples/RenderGraph/Blur](story:Examples/RenderGraph/Blur) — dependency と pass segmentation
        - [Reference/Guides/TwoD](story:Reference/Guides/TwoD) — compute raster、text、logical buffer/texture refs

        これらの story が選択した環境で通ることと、required tests/goldens が揃うことを、backend 対応を表明するためのゲートにします。
        """, toc: true);
}
