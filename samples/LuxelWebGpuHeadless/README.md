# LuxelWebGpuHeadless

ウィンドウやsurfaceを作らず、公開`GpuDevice` APIだけでWebGPUのWGSL compute、storage arenaからのvertex pulling、sampled checkerboardを使うoffscreen triangle、`GpuMemoryKind.HostCached` readbackを実行・検証するconsole sampleです。root argumentsのtexture/sampler logical indexをportableな固定`switch`で選択し、範囲外indexがmagenta sentinelになることも検証します。

```bash
dotnet run --project samples/LuxelWebGpuHeadless -c Release
```

LinuxでMesa lavapipeを明示する例:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json \
WGPU_BACKEND=vulkan LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER=1 \
  dotnet run --project samples/LuxelWebGpuHeadless -c Release
```

成功時はdevice名、compute値、checkerboardからsampleしたtriangle中央pixel、`status=pass`を1行で出力します。wgpu-native runtimeまたは
adapterが無い場合は非zeroで終了します。このsample自体はheadless/offscreen検証用です。windowed WebGPU plumbingは`Luxel.WebGPU.Present.Tests`（X11 present/resize）と各appの`webgpu|wgpu`明示selectorで検証します。既存LuxelTriangle/UI rasterizerの全WGSL shader-cache移植は別作業です。
