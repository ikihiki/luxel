using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0019-Portable-Gpu-Semantics-WebGPU-Backend", Order = 90)]
    public static Widget Adr0019(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0019 — portable GPU semantics として WebGPU backend を追加する

        - **Status**: Accepted
        - **Date**: 2026-07-28
        - **Deciders**: ikihiki
        - **Amends**: [ADR-0002](story:Internals/ADR/0002-Thin-Bindless-Gpu-Abstraction)

        ## Context

        [ADR-0002](story:Internals/ADR/0002-Thin-Bindless-Gpu-Abstraction) は Vulkan / DirectX 12 の共通部分を、固定 root arguments、bindless resources、vertex pulling、one-shot command recording、stage barrierへ絞ることで、薄い GPU 抽象を成立させました。一方、その決定は GPU virtual address、永続 map、ユーザー書換 descriptor heap、明示 barrierを portable baseline とみなし、WebGPU 系を「最大公約数」の抽象として却下していました。

        native desktop WebGPU を追加するには、それらの native mechanism を sentinel や疑似 GPUVA で模倣するのではなく、上位層が必要とする意味論と backend-specific fast path を分離する必要があります。守るべき *No Graphics API* の原則は、特定 API の命令形ではなく、transient recording、data-oriented な単一 argument block、vertex pulling、明示的な pass、アプリから descriptor 管理を隠すこと、論理依存と tooling-first diagnostics です。

        ## Decision

        `Luxel.Graphics` の共通契約を **portable semantic model** として定義し、その lowering の一つとして native desktop WebGPU backend を追加します。ADR-0002 の薄い抽象という方向性は維持し、native mechanism を共通 API の必須条件とした部分を次のように修正します。

        - **resource** — declared usage を持つ opaque allocation と stable logical ID。`DeviceAddress`、persistent pointer、native bindless index は capability-specific extension とし、portable API で sentinel 値を返さない
        - **arguments** — 最大 192 bytes の単一 typed/raw data block と、manifest で識別できる logical resource references。WebGPU baseline は frame-local uniform/storage argument ring、Immediate Data は capability-gated optimization
        - **memory flow** — `Upload` / `DeviceLocal` / `Readback` を契約とする。WebGPU は CPU shadow/staging + `writeBuffer`/copy と async map に lower し、永続 map を要求しない
        - **pass** — attachments、load/store、resource access set を明示する。portable pipeline state は baked state とし、Vulkan/D3D12 の extended dynamic state は任意最適化
        - **synchronization** — producer/consumer dependency を共通意味論とする。Vulkan は synchronization2 barrier、D3D12 は transition/UAV barrier、WebGPU は pass/encoder segmentation と usage validation に lower する
        - **pipeline / shader** — entry points、portable baked state、specialization values、backend artifacts、argument/resource metadata、required features/limits をまとめた shader package を使う。WebGPU の正式 artifact は WGSL とする
        - **diagnostics** — unsupported feature/limit は backend または pipeline 作成時に fail-fast し、required/actual、adapter、shader/pipeline label、logical ID を含める

        初期対象は native Windows と Linux/X11 の明示 opt-in です。browser/WASM と macOS は初期スコープ外で、既定 backend は変更しません。利用者向けの設計契約は [Reference/Guides/WebGPU](story:Reference/Guides/WebGPU) に記録します。

        ## Alternatives

        - **ADR-0002 を上書きして WebGPU の却下理由を消す** — 当時の制約と判断が失われるため却下。履歴は残し、この ADR で Amends する
        - **GPUVA / bindless index / persistent map を WebGPU 上で疑似再現する** — sentinel、巨大 arena、暗黙 copy が共通 API の意味を曖昧にし、backend の制約が上位へ漏れるため却下
        - **WebGPU 専用の別グラフィック API を上位層へ公開する** — RenderGraph / TwoD / UI が backend 分岐を持つことになり、薄い共通契約を壊すため却下
        - **browser/WASM まで初期対象に含める** — surface、配布、非同期実行、shader/toolchain の検証範囲が増え、native backend の契約確立を妨げるため見送り
        - **WebGPU を既定 backend にする** — Vulkan/D3D12 の既存回帰と配布契約を不要に変えるため却下。初期は明示 opt-in とする

        ## Consequences

        - ✅ 上位コードは logical resources、argument block、pass、dependency の同じ意味論で Vulkan / DirectX 12 / WebGPU を扱える
        - ✅ Vulkan/D3D12 は GPUVA、persistent mapping、bindless heap を fast path として維持できる
        - ✅ shader metadata、logical IDs、labels、required limits が backend をまたぐ診断と tooling の基盤になる
        - ⚠️ 共通 GPU API と shader ABI の移行が必要で、既存 backend の pixel / root argument / one-shot command 回帰を継続して守る必要がある
        - ⚠️ WebGPU では root argument ring、bind-group cache、upload/readback lifecycle、pass split の実装コストを backend が引き受ける
        - ⚠️ WebGPU の feature/limit と採用 native implementation の対応範囲により、pipeline が明示的に利用不能になる場合がある
        - ⚠️ native Windows/Linux 以外、Immediate Data、unbounded runtime resource arrays、永続 CPU/GPU 同時 map は portable baseline に含まれない
        """, toc: true);
}
