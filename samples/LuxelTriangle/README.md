# LuxelTriangle

Gallery の `GpuView` / `IGpuScene` に依存しない、最小のスタンドアロン GPU 描画サンプルです。

- Windows: Vulkan (`vk`) / DirectX 12 (`dx`)
- Linux: Vulkan (`vk`)、X11 (`DISPLAY`) が必要
- `--frames N`: N フレーム描画後に自動終了する smoke-test 用オプション
- `--stage triangle|texture|transform|lighting|graph|post`: 直接描画からtexture/MVP/depth/light、1-pass RenderGraph、transient post-processへ段階的に進む
- `--size WIDTHxHEIGHT`: 非alignment幅を含むresize/aspect smoke用の初期client size

```powershell
# Windows / Linux Vulkan
dotnet run --project samples/LuxelTriangle -- vk

# Windows DirectX 12
dotnet run --project samples/LuxelTriangle -- dx

# 自動終了（既定はR1 triangle）
dotnet run --project samples/LuxelTriangle -- vk --frames 3

# R3: texture付きquad → indexed cube + MVP → depth/culling/directional light
dotnet run --project samples/LuxelTriangle -- vk --stage texture --frames 3
dotnet run --project samples/LuxelTriangle -- vk --stage transform --frames 3
dotnet run --project samples/LuxelTriangle -- vk --stage lighting --frames 3
dotnet run --project samples/LuxelTriangle -- vk --stage lighting --size 801x603 --frames 3

# R4: 直接描画を1-pass graphへ移し、次にtransient scene + compute post-processへ拡張
dotnet run --project samples/LuxelTriangle -- vk --stage graph --frames 3
dotnet run --project samples/LuxelTriangle -- vk --stage post --size 801x603 --frames 3
```

## ファイル

- `Program.cs`: window、backend、surface、event loop、resize、shutdown
- `TutorialAbi.cs`: rendererとlayout unit testが共有するC# / Slang ABI（triangleと3D vertex/root args）
- `TriangleRenderer.cs`: stage別mesh、texture/sampler、MVP、depth/culling/light、direct/RenderGraph command recording、readback framebuffer
- `../../shaders/tutorial_triangle.slang`: R1 triangle用のVulkan / D3D12共通Slang shader
- `../../shaders/tutorial_3d.slang`: R3 indexed vertex pulling / texture / MVP / Lambert用Slang shader
- `../../shaders/compute_tutorial_postprocess.slang`: R4 transient scene bufferからpresentation framebufferへ色調整するcompute shader

Luxel の `GpuSurface` は RGBA8 のCPU可視 framebufferをswapchainへ提示します。このサンプルはGPUのrender targetへ描き、
`CopyTextureToBuffer`でframebufferへコピーしてから`Present`します。D3D12のrow pitch要件を満たすため、framebuffer strideだけを64 pixel単位へ揃え、render targetとcamera aspectはvisible widthを使います。

R4のgraphは1 frameごとに作成し、import resourceはrenderer、transient resourceはgraphが所有します。現在の公開queue APIにはsubmission単位のfence/tokenがないため、この教材は意図的に`SubmitAndWait`で同期し、GPU完了後にgraphを破棄します。本番向けframes-in-flightは学習ページで概念として説明します。

リサイズ時はqueueをidleにしてsurfaceとrender target/readback bufferを作り直します。最小化中 (`width == 0 || height == 0`) は描画を休止します。
