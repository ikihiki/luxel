# LuxelTriangle

Gallery の `GpuView` / `IGpuScene` に依存しない、最小のスタンドアロン GPU 描画サンプルです。

- Windows: Vulkan (`vk`) / DirectX 12 (`dx`)
- Linux: Vulkan (`vk`)、X11 (`DISPLAY`) が必要
- `--frames N`: N フレーム描画後に自動終了する smoke-test 用オプション

```powershell
# Windows / Linux Vulkan
dotnet run --project samples/LuxelTriangle -- vk

# Windows DirectX 12
dotnet run --project samples/LuxelTriangle -- dx

# 自動終了
dotnet run --project samples/LuxelTriangle -- vk --frames 3
```

## ファイル

- `Program.cs`: window、backend、surface、event loop、resize、shutdown
- `TutorialAbi.cs`: rendererとlayout unit testが共有するC# / Slang ABI (`Vertex` 32B、`DrawArgs` 4B)
- `TriangleRenderer.cs`: vertex buffer、pipeline、command recording、readback framebuffer
- `../../shaders/tutorial_triangle.slang`: Vulkan / D3D12 共通 Slang shader

Luxel の `GpuSurface` は RGBA8 のCPU可視 framebufferをswapchainへ提示します。このサンプルはGPUのrender targetへ描き、
`CopyTextureToBuffer`でframebufferへコピーしてから`Present`します。D3D12のrow pitch要件を満たすため、strideは64 pixel単位へ揃えます。

リサイズ時はqueueをidleにしてsurfaceとrender target/readback bufferを作り直します。最小化中 (`width == 0 || height == 0`) は描画を休止します。
