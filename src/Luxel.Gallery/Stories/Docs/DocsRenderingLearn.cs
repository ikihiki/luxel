using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けレンダリング学習経路。実行可能な正は samples/LuxelTriangle。</summary>
public static class DocsRenderingLearn
{
    [Story("Learn/Rendering/Overview", Order = 0)]
    public static Widget Overview(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Rendering 学習ガイド

        **難易度:** Beginner　 **実行環境:** Standalone + Gallery　 **Backend:** Vulkan / DirectX 12

        この章は、Gallery のデモを見るだけでなく、自分のウィンドウへ描画できるところまでを順番に進みます。最小アプリの実装はリポジトリの `samples/LuxelTriangle/` が単一の正です。

        ## 最初の4ステップ

        1. [Environment](story:Learn/Rendering/Environment) — OS、GPU、backend、shader cacheを確認
        2. [ClearColor](story:Learn/Rendering/ClearColor) — window / surface / event loop / resizeを理解
        3. [FirstTriangle](story:Learn/Rendering/FirstTriangle) — vertex buffer / shader / pipeline / commandを接続
        4. [Demos/3D/Triangle](story:Demos/3D/Triangle) — Galleryのoffscreen実例を操作

        > [!IMPORTANT]
        > `GpuView` と `IGpuScene` はGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`NativeWindow`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        R1では三角形の表示までです。buffer ABI、texture、camera、depth、RenderGraph、glTFは後続ページで段階的に追加します。最初からECSやRenderGraphを使う必要はありません。
        """, toc: true);
    }

    [Story("Learn/Rendering/Environment", Order = 1)]
    public static Widget Environment(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # レンダリング環境を確認する

        **難易度:** Beginner　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [Overview](story:Learn/Rendering/Overview)　 **次:** [ClearColor](story:Learn/Rendering/ClearColor)

        ## 対応backend

        | OS | Window backend | GPU backend | 実行引数 |
        | --- | --- | --- | --- |
        | Windows | Win32 | Vulkan | `vk` |
        | Windows | Win32 | DirectX 12 | `dx` |
        | Linux | Silk.NET GLFW / X11 | Vulkan | `vk` |

        Linuxの実ウィンドウにはX11の `DISPLAY` が必要です。Wayland nativeは未対応です。headless環境では `eng/desktop/start.sh` または `xvfb-run` でX serverを用意します。

        ## ビルド

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet run --project samples/LuxelTriangle -- vk
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx
        ```

        成功すると暗い背景に赤・緑・青の三角形が表示されます。CIやsmoke testでは `--frames 3` を付けると自動終了します。

        ## Shader cache

        通常ビルドはGit管理済みの `shaders/compiled/` を使うため、Slang/DXCの事前導入は不要です。`.slang`を変更したときだけ次を実行します。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        ```

        生成物と `inputs.sha256` をshader sourceと一緒にコミットします。

        ## 典型的な失敗

        - Linuxで `DISPLAY` がない → X11 serverを起動する
        - Linuxで `dx` を指定 → Vulkanの `vk` を使う
        - shader cache mismatch → 上記 `CompileLuxelShaderCache` を実行する
        - Vulkan deviceがない → Vulkan driverまたはlavapipeの導入を確認する
        """, toc: true);
    }

    [Story("Learn/Rendering/ClearColor", Order = 2)]
    public static Widget ClearColor(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # ウィンドウとClear Color

        **難易度:** Beginner　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [Environment](story:Learn/Rendering/Environment)　 **次:** [FirstTriangle](story:Learn/Rendering/FirstTriangle)

        `samples/LuxelTriangle/Program.cs` がstandaloneアプリの外枠です。責務は次の順です。

        ```text
        WindowSystem → NativeWindow → GpuDevice → GpuSurface
                     → event loop → Render → Present
        ```

        `GpuSurface`へrender targetを直接渡すのではなく、RGBA8のCPU可視framebufferを渡します。サンプルはGPU render targetをclearし、三角形を描き、`CopyTextureToBuffer`でframebufferへコピーします。三角形を外してもclear colorだけは表示されるため、window / surface / presentの切り分けに使えます。

        ```csharp
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer);
        command.Finish();
        device.MainQueue.SubmitAndWait(command);
        surface.Present(framebuffer, stridePixels, width, height);
        ```

        ## Resize

        resize callbackでは即座にGPU resourceを破棄せず、次のevent-loop iterationでqueueをidleにしてからsurface、render target、framebufferを作り直します。最小化中の0×0では描画を休止します。

        D3D12のtexture readback row pitchは256 byte単位なので、RGBA8のstrideは64 pixel単位へ揃えます。`Present`には実際のwidthと、揃えたstrideの両方を渡します。
        """, toc: true);
    }

    [Story("Learn/Rendering/FirstTriangle", Order = 3)]
    public static Widget FirstTriangle(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめての三角形

        **難易度:** Beginner　 **実行環境:** Standalone + Gallery　 **Backend:** Vulkan / DirectX 12

        **前提:** [ClearColor](story:Learn/Rendering/ClearColor)　 **次:** [Docs/GpuDevice](story:Docs/GpuDevice)

        {{StoryRef(ctx, "Demos/3D/Triangle")}}

        上の表示はGalleryのoffscreenデモです。コピーして動かす完全なstandalone実装は次の4ファイルです。

        - `samples/LuxelTriangle/LuxelTriangle.csproj`
        - `samples/LuxelTriangle/Program.cs`
        - `samples/LuxelTriangle/TriangleRenderer.cs`
        - `shaders/tutorial_triangle.slang`

        ## 描画の4段階

        1. `Malloc(..., HostMapped)`で3頂点を確保し、`Span<Vertex>`へ書く
        2. `GpuShaderCode.Load("tutorial_triangle")`からgraphics pipelineを作る
        3. commandへpipeline、root arguments、`Draw(3)`を記録する
        4. submit後、readback framebufferをsurfaceへpresentする

        ```csharp
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(new DrawArgs { VertexBufferIndex = vertices.BindlessIndex })
            .Draw(3)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer);
        ```

        Slang側は `SV_VertexID` を使ってbindless bufferから頂点を読むため、vertex-input layout objectはありません。C#の `Vertex` とSlangの `Vertex` はどちらも `float4 position + float4 color = 32 byte` です。

        > [!NOTE]
        > 入門サンプルは処理順を明確にするため毎フレーム `SubmitAndWait` します。複数frame-in-flightとfenceによる本番向け同期は後続のFrame Loopページで扱います。

        ## 典型的な失敗

        - clear colorだけ見える → pipeline、shader名、root arguments、`Draw(3)`を確認
        - 三角形が崩れる → C# / Slangのstruct sizeとoffsetを確認
        - resize後だけ壊れる → queue idle後にtarget/framebufferを再生成したか確認
        """, toc: true);
    }
}
