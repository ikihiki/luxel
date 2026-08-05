using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsGpu
{
    [Story("Internals/Gpu/Synchronization", Order = 69)]
    public static Widget GpuSynchronizationInternals(StoryContext ctx) => DocNew(ctx, $$"""
        # GPU同期の内部実装

        公開APIの`GpuCommandBuffer.Barrier`と`GpuQueue`は、Vulkan、DirectX 12、native WebGPU、browser WebGPUの異なる同期機構へlowerされます。利用側の説明は[同期](story:Learn/Grapics/Synchronization)を参照してください。このページでは現在の実装が実際にどのnative operationを使うかを説明します。

        ## 公開APIとbackend interface

        `GpuQueue`はbackendの`IGpuBackendQueue`へ委譲します。backend interfaceが公開する完了操作は現在`Submit`と`WaitIdle`だけです。

        ```text
        GpuQueue.Submit(command)
          → IGpuBackendQueue.Submit(command.Backend)

        GpuQueue.SubmitAndWait(command)
          → backend.Submit(command)
          → backend.WaitIdle()
        ```

        `SubmitAsync`と`WaitIdleAsync`はbackendが`IAsyncGpuBackendQueue`を実装していれば非同期経路へ委譲し、それ以外では同期的なsubmit + idle waitへfallbackします。publicなper-submit fence handleやtimeline valueはまだありません。

        ## Vulkan

        ### Barrier

        `VulkanCommandBuffer.Barrier`は`GpuStage`を`PipelineStageFlags2`へ変換し、`MemoryBarrier2`を`vkCmdPipelineBarrier2`へ記録します。

        ```text
        GpuStage.ComputeShader → VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT
        GpuStage.PixelShader   → VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT
        GpuStage.ColorOutput   → VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT
        GpuStage.Copy          → VK_PIPELINE_STAGE_2_ALL_TRANSFER_BIT
        GpuStage.All           → VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT
        ```

        現在のgeneric memory barrierはsource/destinationともmemory read/write accessを指定します。`GpuHazard.IndirectArguments`ではdestinationへindirect command readも加えます。textureのlayout変更はgeneric barrierとは別にimage memory barrierで追跡します。

        ### SubmitとFence

        main queueの`Submit`は`vkQueueSubmit`へcommand bufferを渡し、fence handleにはnullを指定します。`WaitIdle`は`vkQueueWaitIdle`でqueue全体を待ちます。したがって現在のmain queueでは、個々のsubmitをpublic fenceへ関連付けていません。

        Vulkan backend内でもfenceを使う箇所はあります。初期uploadなどのone-shot submitは一時的な`VkFence`を作成し、`vkQueueSubmit(..., fence)`、`vkWaitForFences`、destroyの順で完了を待ちます。window surfaceもpresent用の内部fenceを所有し、前回処理を待ってresetしてから次のsubmitへ再利用します。これらはbackend内部の実装詳細で、`GpuQueue`利用者へhandleを公開しません。

        ## DirectX 12

        ### Barrier

        `D3D12CommandList.Barrier`は現在、globalなUAV barrierとして`ResourceBarrierUnorderedAccessView(null)`を記録します。`GpuStage source/destination`はD3D12 barrier sync maskへ細かくlowerされていません。

        render target、copy sourceなどtexture stateは、各operationの前に個別のtransition barrierを記録し、`D3D12Texture.CurrentState`を更新します。generic `Barrier`とtexture transitionは別経路です。

        ### SubmitとFence

        `Submit`は`ID3D12CommandQueue.ExecuteCommandList`を呼びます。queueは内部に1個の`ID3D12Fence`と単調増加する`_fenceValue`を所有します。

        `WaitIdle`はqueue lock内で値をincrementし、その値を`ID3D12CommandQueue.Signal`します。lockを解放した後、`CompletedValue`がtarget以上になるまで待ちます。

        ```text
        target = ++fenceValue
        commandQueue.Signal(fence, target)
        while fence.CompletedValue < target:
            Thread.Yield()
        ```

        signalは同じcommand queueへ積まれるため、それ以前のcommand listがすべて完了するとtargetへ到達します。現在はOS eventではなく`Thread.Yield`によるpollingで、per-submit valueをpublic APIへ返しません。

        ## Native WebGPU

        ### Barrier

        WebGPUはVulkanのような明示pipeline barrierを公開しません。`WebGpuCommandBuffer.Barrier`は現在のcompute/render pass encoderを終了し、usage scopeを分割します。resource transitionとpass境界の同期はWebGPU implementationとvalidationへ委ねます。

        そのため、`GpuStage`と`GpuHazard`をnative WebGPU flagへ直接変換するのではなく、「ここでpassを分割し、後段usageへ移る」という意味へlowerします。

        ### Submitと完了待ち

        `Submit`はhost-mapped shadowのuploadとroot argument uploadを行い、`wgpuQueueSubmit`を呼びます。HostCached readbackがある場合はcopy用bufferを作り、map completionをdevice pollingで待ってCPU shadowへcopyします。

        `WaitIdle`はwgpu-native extensionの`DevicePoll(device, wait: true)`を使用します。native WebGPUの現在の完了境界はfence objectではなくdevice pollingです。backend dispose時も同じpollingで未完了処理を待ちます。

        ## Browser WebGPU

        ### Barrier

        browser commandの`Barrier`はrendering中ならrender passを終了し、JavaScript側のcommand barrier operationへ渡します。実際のresource transitionはWebGPUがencoder/pass usageから管理します。

        ### SerialとPromise

        browserではJavaScript Promiseの完了をmanaged threadから同期blockできません。`Submit`はJavaScript側からsubmission serialを受け取り、最後のserialとして保持します。

        `SubmitAsync`はそのserialに対応する`CompleteAsync` Promiseをawaitします。`WaitIdleAsync`はqueue全体の完了Promiseをawaitし、必要なreadback dataをmanaged shadowへ適用します。同期版`WaitIdle`は`PlatformNotSupportedException`を投げます。

        ```text
        Submit        → serialを取得して即時return
        SubmitAsync   → Submit → CompleteAsync(serial)をawait
        WaitIdleAsync → queue completion Promiseをawait
        WaitIdle      → unsupported
        ```

        browser経路のserialはD3D12 fence valueに似た役割を持ちますが、現在はbackend内部のJavaScript interop protocolであり、共通public fence APIではありません。

        ## Backend間の対応表

        | backend | `Barrier`のlowering | `Submit` | idle/completion | Fenceの扱い |
        | --- | --- | --- | --- | --- |
        | Vulkan | `vkCmdPipelineBarrier2` + texture image barrier | `vkQueueSubmit` | `vkQueueWaitIdle` | main queue submitはfenceなし。one-shot/surfaceで内部fence |
        | DirectX 12 | UAV barrier + texture transition | `ExecuteCommandList` | queue signal + `CompletedValue` polling | queue内部に単調増加fence |
        | native WebGPU | pass encoder終了 | `wgpuQueueSubmit` | `DevicePoll(wait=true)` | fence objectを公開せずpolling |
        | browser WebGPU | pass終了 + interop barrier | JS queue submit | Promise / submission serial | serialはbackend内部のみ |

        ## 現在の制約

        - public APIにはper-submit fence、timeline value、completion tokenがない。
        - Vulkan main queueは`WaitIdle`でqueue全体を待つ。
        - DirectX 12は内部fenceを使うが、target valueを呼び出し側へ返さない。
        - native WebGPUはdevice polling、browser WebGPUはPromiseを完了境界にする。
        - `GpuStage`の精密なloweringはVulkanが中心で、D3D12/WebGPUはより粗い同期になる。

        将来per-submit completionを公開する場合も、Vulkan fence/timeline semaphore、D3D12 fence value、WebGPU Promise/serialを直接露出せず、resource再利用に必要なbackend-neutral completion tokenとして設計する必要があります。
        """, toc: true);
}
