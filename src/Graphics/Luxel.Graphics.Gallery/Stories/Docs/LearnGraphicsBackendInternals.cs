namespace Luxel.Gallery.Stories;

/// <summary>GpuDevice の共通 API が各 graphics backend へ変換される過程を、resource 単位で説明する。</summary>
public static class LearnGraphicsBackendInternals
{
    [Story("Learn/Graphics/Internal/DirectX12", Order = 9, Toc = true)]
    public static StoryResult DirectX12() => $$"""
        # DirectX 12 backend の resource 実装

        > [!WARNING]
        > このページは `Luxel.Graphics.DirectX12` の内部実装を説明します。アプリケーションからは backend 型を直接操作せず、原則として `GpuDevice`、`GpuBuffer`、`GpuTexture`、`GpuPipeline`、`GpuCommandBuffer` を使います。

        {{RenderingCourseCatalog.Meta("Learn/Graphics/Internal/DirectX12", "Advanced", "Windows / native", "Direct3D 12", "Buffers / Textures / Shaders / PipelineState / Synchronization")}}

        ## 共通 API から backend への入口

        `GpuDevice` は resource 作成を `IGpuBackend` へ委譲します。DirectX 12 では `D3D12Backend` が native device、descriptor heap、root signature を所有し、公開 wrapper の背後へ `D3D12Buffer`、`D3D12Texture`、`D3D12Sampler`、`D3D12Pipeline` を格納します。

        ```text
        GpuDevice
          └─ IGpuBackend
               └─ D3D12Backend
                    ├─ D3D12Buffer / D3D12Texture / D3D12Sampler
                    ├─ D3D12Pipeline
                    ├─ D3D12CommandList
                    ├─ D3D12Queue
                    └─ D3D12Surface
        ```

        Luxel は公開 API に descriptor table や root signature を露出しません。buffer、sampled texture、sampler は作成時に descriptor slot を受け取り、shader には `BindlessIndex` を渡します。

        ## Buffer と memory

        `D3D12Backend.CreateBuffer` は `GpuMemoryKind` を heap type へ変換して committed resource を作ります。

        | `GpuMemoryKind` | 主な heap | 用途 |
        | --- | --- | --- |
        | `DeviceLocal` | default | GPU が頻繁に読む storage、indirect、copy resource |
        | `HostMapped` | GPU upload | CPU から GPU へ渡す data |
        | `HostCached` | readback | GPU の結果を CPU で読む data |

        Buffer descriptor は resource descriptor heap の raw UAV として登録されます。`D3D12Buffer.BindlessIndex` はその slot であり、Dispose 時には native resource と slot の両方を解放します。host-visible resource の map は wrapper が保持し、`GpuBuffer.Write` や readback helper が共通 API として使用します。

        ## Texture、render target、depth target

        Texture の役割ごとに descriptor の種類が異なります。

        - sampled texture: shader resource view を resource descriptor heap へ置く
        - render target: render target view を RTV allocator から取得する
        - depth target: depth stencil view を DSV allocator から取得する
        - copy source / destination: command 記録時に resource state を遷移する

        `D3D12Texture` は resource、format、extent、現在の resource state、descriptor slot を保持します。sampled texture の `BindlessIndex` は SRV slot ですが、render target と depth target は描画 attachment 用 descriptor を別に持ちます。

        ## Texture upload

        DirectX 12 の texture row pitch は 256 byte alignment を必要とします。backend は `GetCopyableFootprints` で必要な footprint と総 byte 数を取得し、upload buffer を作り、各 row を aligned pitch へコピーしてから `CopyTextureRegion` を記録します。

        ```text
        CPU pixels
          → upload heap buffer (256-byte aligned row pitch)
          → CopyTextureRegion
          → sampled texture
        ```

        Upload 前後には copy destination と shader resource の state transition が必要です。一時 upload resource は one-shot command の完了後に破棄します。

        ## Sampler

        `CreateSampler` は filter と address mode を `D3D12_SAMPLER_DESC` へ変換し、sampler 専用 descriptor heap に書き込みます。`GpuSampler.BindlessIndex` は resource heap ではなく sampler heap の slot です。shader ABI は texture index と sampler index を別々に受け取ります。

        ## Shader と pipeline

        DirectX 12 backend は `GpuShaderCode` から DXIL を選択します。compute pipeline は compute shader と共通 root signature から PSO を作ります。

        Graphics pipeline は logical state と native PSO を分離しています。`D3D12Pipeline` は shader と fixed state を保持し、render target format、depth format、blend、rasterizer、topology などの組み合わせごとに native PSO variant を cache します。これにより attachment format が確定する `BeginRendering` まで、必要な native variant の作成を遅延できます。

        ## Descriptor と root argument

        共通 root signature は unbounded な resource table と sampler table を持ちます。`GpuCommandBuffer.SetRootArguments` で渡した小さな値は root constants になり、その中に各 resource の `BindlessIndex` を格納します。

        ```text
        Root constants: draw/dispatch 固有の値と BindlessIndex
        Resource heap: buffer UAV と texture SRV
        Sampler heap: sampler descriptor
        RTV/DSV heaps: render attachment
        ```

        ## Command recording と barrier

        `D3D12Queue.StartCommandRecording` は command allocator と graphics command list を作り、`D3D12CommandList` が共通 command API を native call へ変換します。

        - `BeginRendering`: RTV/DSV を設定して clear する
        - `SetPipeline`: descriptor heap、root signature、PSO variant を bind する
        - `Draw` / `Dispatch`: graphics または compute command を記録する
        - copy: footprint と state transition を使って buffer / texture 間を転送する
        - `Barrier`: shader write の順序には UAV barrier を記録する

        Generic な `Barrier` は現在 UAV barrier が中心です。一方、render target、copy、present に必要な texture state は各 operation が明示的な transition barrier として記録します。同期 API の意味は [GPU synchronization](story:Learn/Graphics/Synchronization) も参照してください。

        ## Queue、fence、完了待ち

        `D3D12Queue` は command list を閉じて command queue へ execute します。同期的な helper は fence value を signal し、`ID3D12Fence` の完了値または event を待ってから一時 resource を解放します。通常の frame loop では毎 command を待たず、複数 frame の lifetime を fence value で管理する必要があります。

        ## Surface と presentation

        `D3D12Surface` は `IDXGISwapChain3` と backbuffer を所有します。Luxel の描画 target から swapchain image へ copy する前後で、target を copy source、backbuffer を copy destination / present へ遷移し、command を submit して `Present` を呼びます。resize 時は GPU の利用完了を確認して backbuffer view を作り直します。

        ## Lifetime と実装を追う順序

        Resource wrapper の Dispose は native object だけでなく descriptor slot の再利用にも関係します。実装を読むときは次の順序で追うと、公開 API との対応が分かります。

        1. `src/Graphics/Luxel.Graphics/GpuDevice.cs`
        2. `src/Graphics/Luxel.Graphics.DirectX12/D3D12Backend.cs`
        3. `D3D12Buffer.cs` / `D3D12Texture.cs` / `D3D12Sampler.cs`
        4. `D3D12Pipeline.cs` / `D3D12CommandList.cs`
        5. `D3D12Queue.cs` / `D3D12Surface.cs`
        """;

    [Story("Learn/Graphics/Internal/Vulkan", Order = 10, Toc = true)]
    public static StoryResult Vulkan() => $$"""
        # Vulkan backend の resource 実装

        > [!WARNING]
        > このページは `Luxel.Graphics.Vulkan` の内部実装を説明します。公開 API の利用方法ではなく、共通 resource が Vulkan object と synchronization へ変換される過程が対象です。

        {{RenderingCourseCatalog.Meta("Learn/Graphics/Internal/Vulkan", "Advanced", "Windows / Linux native", "Vulkan", "Buffers / Textures / Shaders / PipelineState / Synchronization")}}

        ## 共通 API から backend への入口

        `VulkanBackend` は instance、physical device、logical device、queue、descriptor set layout、pipeline layout を初期化し、`IGpuBackend` の resource factory を実装します。

        ```text
        GpuDevice
          └─ VulkanBackend
               ├─ VulkanBuffer / VulkanTexture / VulkanSampler
               ├─ VulkanPipeline
               ├─ VulkanCommandBuffer
               ├─ VulkanQueue
               └─ VulkanSurface
        ```

        公開 API に Vulkan の descriptor set は現れません。backend は buffer、sampled image、sampler の配列を持つ bindless descriptor set を作り、各 wrapper の `BindlessIndex` を配列 index として shader へ渡します。

        ## Buffer と memory

        `CreateBuffer` は storage、transfer、indirect、device address など Luxel が必要とする usage flag を組み合わせて `VkBuffer` を作ります。その後 memory requirements を取得し、`GpuMemoryKind` に合う memory type を選び、allocate と bind を行います。

        Host-visible memory は必要に応じて map し、CPU write / readback に使います。Device-local buffer へ初期 data を渡す場合は staging resource と copy command を使います。作成後は storage buffer descriptor array の `BindlessIndex` 位置を `vkUpdateDescriptorSets` で更新します。

        ## Texture、render target、depth target

        `VulkanTexture` は `VkImage`、memory、image view、format、extent、layout を保持します。

        - render target: color attachment と transfer 用 usage を持つ image
        - depth target: depth/stencil attachment 用 image と aspect mask
        - sampled texture: sampled と transfer destination 用 image、および sampled-image descriptor
        - presentation: swapchain image は `VulkanSurface` が所有し、通常の texture wrapper とは lifetime が異なる

        Luxel の rendering は dynamic rendering を使うため、固定 render pass object を page ごとに作りません。`BeginRendering` が color / depth attachment info を組み立て、現在の image layout を attachment 向けに遷移します。

        ## Texture upload

        Sampled texture の upload は staging buffer、image layout transition、buffer-to-image copy の順です。

        ```text
        CPU pixels
          → mapped staging buffer
          → TRANSFER_DST_OPTIMAL
          → vkCmdCopyBufferToImage
          → SHADER_READ_ONLY_OPTIMAL
        ```

        row pitch と format から copy region を構築し、one-shot command を queue へ submit します。完了後に staging buffer を破棄し、sampled-image descriptor array を更新します。

        ## Sampler

        `CreateSampler` は filter、mipmap mode、address mode を `VkSamplerCreateInfo` へ変換して `VkSampler` を作ります。sampler descriptor array の slot が `GpuSampler.BindlessIndex` です。Texture と sampler は別配列なので、shader 側でも別 index として扱います。

        ## Shader と pipeline

        Vulkan backend は `GpuShaderCode` から SPIR-V を選び、`VkShaderModule` を作ります。Compute pipeline は shader stage と共通 pipeline layout から作成します。

        Graphics pipeline は shader stage、vertex 入力方針、input assembly、rasterization、multisample、depth/stencil、blend、dynamic state、dynamic-rendering format を `vkCreateGraphicsPipelines` へ渡します。viewport と scissor など command ごとに変わる値は dynamic state として記録します。

        ## Descriptor と push constants

        Backend が作る共通 pipeline layout は bindless descriptor set layout と push constant range を接続します。

        | 共通 API | Vulkan 実装 |
        | --- | --- |
        | `BindlessIndex` | descriptor array element |
        | root arguments | push constants |
        | buffer / texture / sampler bind | 共通 descriptor set の bind |
        | graphics state | pipeline object と dynamic state |

        Resource を作成・破棄するときは descriptor slot allocator と descriptor write の順序が重要です。GPU が古い descriptor を参照中に slot を再利用しないことが lifetime の前提です。

        ## Command recording と barrier

        `VulkanQueue` が command pool と command buffer を用意し、`VulkanCommandBuffer` が共通 command を Vulkan command へ変換します。

        - `SetRootArguments`: `vkCmdPushConstants`
        - `SetPipeline`: pipeline と bindless descriptor set を bind
        - `BeginRendering` / `EndRendering`: dynamic rendering を開始・終了
        - `Draw` / `Dispatch`: graphics / compute command
        - copy: buffer、image、layout を組み合わせた transfer command
        - `Barrier`: `GpuStage` を synchronization2 の stage/access mask へ変換

        Barrier は `VkDependencyInfo` と `vkCmdPipelineBarrier2` を使います。Buffer の producer / consumer 順序に加え、image では old layout、new layout、aspect を tracking して image memory barrier を作ります。考え方は [GPU synchronization](story:Learn/Graphics/Synchronization) と対応します。

        ## Queue submission と完了待ち

        Command buffer を end した後、`VulkanQueue` は `vkQueueSubmit` で実行します。同期 helper は `vkQueueWaitIdle` を使うため理解しやすい一方、frame ごとの hot path では fence や semaphore で必要な依存だけを表現する方が効率的です。

        ## Surface、swapchain、presentation

        `VulkanPresentationSource` は platform が必要とする instance extension と surface 作成 callback を backend へ渡します。`VulkanSurface` は surface format、present mode、extent を選んで swapchain と image view を作ります。

        Present の流れは次のとおりです。

        1. `vkAcquireNextImageKHR` で swapchain image を取得する
        2. semaphore / fence で image 利用可能を待つ
        3. Luxel の target と swapchain image を transfer layout へ遷移する
        4. target の内容を swapchain image へ copy する
        5. swapchain image を present layout へ遷移する
        6. `vkQueuePresentKHR` で表示する

        Out-of-date または resize が発生した場合は、利用中の resource を待って swapchain を再作成します。

        ## Lifetime と実装を追う順序

        Vulkan では object 破棄順、device memory の unmap/free、descriptor slot、command pool、swapchain image の所有者を区別する必要があります。

        1. `src/Graphics/Luxel.Graphics/GpuDevice.cs`
        2. `src/Graphics/Luxel.Graphics.Vulkan/VulkanBackend.cs`
        3. `VulkanBuffer.cs` / `VulkanTexture.cs` / `VulkanSampler.cs`
        4. `VulkanPipeline.cs` / `VulkanCommandBuffer.cs`
        5. `VulkanQueue.cs` / `VulkanSurface.cs` / `VulkanPresentationSource.cs`
        """;

    [Story("Learn/Graphics/Internal/WebGPU", Order = 11, Toc = true)]
    public static StoryResult WebGpu() => $$"""
        # WebGPU backend の resource 実装

        > [!WARNING]
        > WebGPU には native (`Luxel.Graphics.WebGPU`) と browser (`Luxel.Graphics.WebGPU.Browser`) の2実装があります。共通 API は同じですが、native handle を直接呼ぶ経路と JavaScript interop を通る経路では lifetime と完了通知が異なります。

        {{RenderingCourseCatalog.Meta("Learn/Graphics/Internal/WebGPU", "Advanced", "Windows / Linux native + Browser WASM", "WebGPU", "Buffers / Textures / Shaders / PipelineState / Synchronization")}}

        ## 2つの backend への入口

        Native 版は wgpu-native の handle を C# から呼び、browser 版は C# object を lightweight handle として保持して JavaScript の WebGPU object を操作します。

        ```text
        GpuDevice
          ├─ WebGpuBackend
          │    ├─ WebGpuResources
          │    ├─ WebGpuCommandBuffer / WebGpuQueue
          │    └─ WebGpuSurface
          └─ BrowserWebGpuBackend
               ├─ BrowserWebGpuResources
               ├─ BrowserWebGpuCommandBuffer / BrowserWebGpuQueue
               ├─ BrowserWebGpuInterop
               └─ luxel-webgpu-browser.js
        ```

        WebGPU は Vulkan や DirectX 12 のような unbounded descriptor array を標準的な同じ形では公開しません。Luxel は固定 bind group layout と storage arena を組み合わせ、共通 shader ABI の `BindlessIndex` を維持します。

        ## Buffer と storage arena

        Buffer は小さな resource ごとに独立 bind group を増やすのではなく、aligned storage arena から suballocate します。`BindlessIndex` は arena 内の slot、すなわち offset を固定 stride で割った値として扱われます。

        ```text
        shared storage buffer
        ├─ slot 0: resource A
        ├─ slot 1: resource B
        └─ slot N: resource N
        ```

        Native の `WebGpuBuffer` は native buffer と offset / size を保持します。Browser の `BrowserWebGpuBuffer.BindlessIndex` も `Offset / BufferStride` から計算されます。host write は queue write または mapped/staging path、readback は copy 後の map / async completion を使います。

        ## Texture と render/depth target

        Texture wrapper は texture、view、format、extent と binding slot を保持します。

        - render target: render attachment と copy source として使用
        - depth target: depth/stencil attachment view を保持
        - sampled texture: texture view を固定 bind group の binding slot へ接続
        - browser texture: JavaScript 側 object table の handle を C# wrapper が保持

        WebGPU は resource usage を作成時に宣言するため、将来必要になる render、sample、copy usage を backend が resource 種別ごとに組み合わせます。Native と browser のどちらも明示的な image layout 値は公開しません。

        ## Texture upload

        Native 版は `QueueWriteTexture` または staging copy を使い、format と row pitch に合う image data layout を構築します。Browser 版は pixel data と metadata を `BrowserWebGpuInterop` から JavaScript へ渡し、JS 側の `GPUQueue.writeTexture` 相当の処理で upload します。

        WebGPU でも bytes-per-row の alignment と extent の一致は必要です。DirectX 12 の resource state や Vulkan の image layout を利用者が指定する代わりに、usage と pass 境界から実装が検証・同期します。

        ## Sampler

        Filter と address mode を `GPUSamplerDescriptor` 相当へ変換して sampler を作ります。Native 版は sampler handle、browser 版は JavaScript object handle を wrapper が保持し、固定 sampler binding の slot を `BindlessIndex` として公開します。

        ## Shader と pipeline

        WebGPU backend は `GpuShaderCode` から WGSL を選び、shader module を作ります。Compute pipeline は compute entry point と pipeline layout、graphics pipeline は vertex / fragment entry point、color target、blend、primitive、depth/stencil、multisample state を組み立てます。

        Native 版は wgpu-native API を直接呼びます。Browser 版は WGSL と pipeline descriptor を JavaScript へ marshal し、JS 側で `GPUShaderModule`、`GPUComputePipeline`、`GPURenderPipeline` を作成します。

        ## Bind group と root argument

        Native backend は固定 bind group layout と、resource 用および command 固有 data 用の bind group を作ります。Root argument は draw / dispatch ごとの storage または uniform data へ書き込み、shader はそこから buffer slot、texture slot、sampler slot を取得します。

        | 共通概念 | WebGPU での lowering |
        | --- | --- |
        | buffer `BindlessIndex` | storage arena の slot |
        | texture / sampler `BindlessIndex` | 固定 binding table の slot |
        | root arguments | command 固有 buffer / bind group |
        | descriptor bind | bind group の設定 |

        Browser 版も同じ ABI を保ちますが、C# は WebGPU object 自体を持たず、interop handle と operation data を JavaScript へ送ります。

        ## Command recording と pass 境界

        `WebGpuCommandBuffer` と `BrowserWebGpuCommandBuffer` は共通 command API を command encoder へ変換します。

        - `BeginRendering`: render pass encoder を開始する
        - `SetPipeline` / root arguments: pipeline と bind group を設定する
        - `Draw` / `Dispatch`: active pass encoder へ記録する
        - copy: pass 外で buffer / texture copy を encoder へ記録する
        - `Finish`: active pass を閉じて command buffer を完成する

        WebGPU には公開 API から直接指定する Vulkan 型 pipeline barrier がありません。Luxel の `Barrier` は必要に応じて compute / render pass を終了または分割し、次の pass や copy operation との境界を作ります。Resource usage の衝突は WebGPU の validation model に従います。

        ## Queue submission と completion

        Native 版の `WebGpuQueue` は command buffer を queue submit し、同期 helper では wgpu-native の `DevicePoll` extension を使って callback と map request の完了を進めます。

        Browser 版は submit ごとに submission serial を割り当てます。JavaScript 側は queue 完了の Promise と一時 resource を serial に対応付け、C# 側の async completion が Promise を待った後で readback と cleanup を行います。Browser では UI thread を同期 block せず、非同期 API を使うことが前提です。

        詳細な同期モデルは [GPU synchronization](story:Learn/Graphics/Synchronization) を参照してください。

        ## Surface と presentation

        Native の `WebGpuSurface` は platform surface を configure し、current surface texture を取得します。Luxel の target を presentation 用 texture へ render / blit し、command を submit して surface present を行います。

        Browser の surface は canvas selector と JavaScript の `GPUCanvasContext` に接続されます。Canvas configure、current texture の取得、描画、browser compositor への提示は `luxel-webgpu-browser.js` 側が担当し、C# 側は resize と frame operation を interop で指示します。

        ## Lifetime と実装を追う順序

        Native handle の release と browser object table の削除は同じ Dispose API に集約されています。ただし browser では Promise が参照する一時 buffer を完了前に破棄できないため、submission serial ごとの cleanup が重要です。

        1. `src/Graphics/Luxel.Graphics.WebGPU/WebGpuBackend.cs` と `WebGpuResources.cs`
        2. `WebGpuCommandBuffer.cs` / `WebGpuQueue.cs` / `WebGpuSurface.cs`
        3. `src/Graphics/Luxel.Graphics.WebGPU.Browser/BrowserWebGpuBackend.cs`
        4. `BrowserWebGpuResources.cs` / `BrowserWebGpuCommandBuffer.cs` / `BrowserWebGpuQueue.cs`
        5. `BrowserWebGpuInterop.cs` / `wwwroot/luxel-webgpu-browser.js`
        """;
}
