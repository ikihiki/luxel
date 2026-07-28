# LuxelWebGpuHeadless

ウィンドウやsurfaceを作らず、公開`GpuDevice` APIだけでWebGPUのWGSL compute、storage arenaからのvertex pulling、offscreen triangle、`GpuMemoryKind.HostCached` readbackを実行・検証するconsole sampleです。sampled texture/samplerは現在の固定ABIでは未対応です。

```bash
dotnet run --project samples/LuxelWebGpuHeadless -c Release
```

LinuxでMesa lavapipeを明示する例:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.x86_64.json \
WGPU_BACKEND=vulkan LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER=1 \
  dotnet run --project samples/LuxelWebGpuHeadless -c Release
```

成功時はdevice名、compute値、triangle中央pixel、`status=pass`を1行で出力します。wgpu-native runtimeまたは
adapterが無い場合は非zeroで終了します。これはheadless/offscreen専用であり、windowed sampleのbackend selectorではありません。
