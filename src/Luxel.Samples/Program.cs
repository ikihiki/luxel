using Luxel;
using Luxel.Samples;

// 使い方: dotnet run --project src/Luxel.Samples -- <backend> <sample>
//   backend: vk | dx   (既定 vk)
//   sample : 01         (compute hello-world)
string backend = (args.Length > 0 ? args[0] : "vk").ToLowerInvariant();
string sample = args.Length > 1 ? args[1] : "01";

GpuDevice CreateDevice() => backend switch
{
    "vk" or "vulkan" => new GpuDevice(Luxel.Vulkan.VulkanBackend.Create()),
    "dx" or "d3d12" => new GpuDevice(Luxel.D3D12.D3D12Backend.Create()),
    _ => throw new ArgumentException($"未知のバックエンド: {backend} (vk / dx)"),
};

Console.WriteLine($"=== Luxel sample {sample} on backend '{backend}' ===");
int exit = sample switch
{
    "01" => Sample01ComputeHelloWorld.Run(CreateDevice),
    "02" => Sample02Triangle.Run(CreateDevice),
    "03" => Sample03TexturedQuad.Run(CreateDevice),
    "04" => Sample04Depth.Run(CreateDevice),
    "05" => Sample05Blend.Run(CreateDevice),
    "06" => Sample06Vector2D.Run(CreateDevice),
    "07" => Sample07Map.Run(CreateDevice),
    "08" => Sample08Gui.Run(CreateDevice),
    "09" => Sample09RetainedUi.Run(CreateDevice),
    "10" => Sample10DeclarativeUi.Run(CreateDevice),
    // 11-19 (古い Luxel.Controls 系 UI サンプル), 20 (DevTools): indexer 統一 refactor で旧 API 廃止のため削除
    "21" => SampleResources.Run(CreateDevice),
    "22" => Sample22BlurComposite.Run(CreateDevice),
    "23" => Sample23BloomChain.Run(CreateDevice),
    "24" => Sample24RenderGraphDevTools.Run(CreateDevice),
    "25" => Sample25Ecs3D.Run(CreateDevice),
    "26" => Sample26Bloom3D.Run(CreateDevice),
    "27" => Sample27WorldSpaceUI.Run(CreateDevice),
    "28" => Sample28ShadowMap.Run(CreateDevice),
    "29" => Sample29TextureAliasing.Run(CreateDevice),
    "30" => Sample30DynamicResolution.Run(CreateDevice),
    "31" => Sample31AnimationCore.Run(CreateDevice),
    "32" => Sample32SequenceParallel.Run(CreateDevice),
    "33" => Sample33EcsClipPlayback.Run(CreateDevice),
    "34" => Sample34RetainedCanvasAnim.Run(CreateDevice),
    "35" => Sample35AnimationGraph.Run(CreateDevice),
    "36" => Sample36CssKeyframes.Run(CreateDevice),
    "37" => Sample37StateMachine.Run(CreateDevice),
    "38" => Sample38TransitionHover.Run(CreateDevice),
    "39" => Sample39PTransition.Run(CreateDevice),
    "40" => Sample40StateStyleButton.Run(CreateDevice),
    "41" => Sample41StateStyleTransitions.Run(CreateDevice),
    "42" => Sample42AppTheme.Run(CreateDevice),
    "43" => Sample43Tailwind.Run(CreateDevice),
    "44" => Sample44TailwindText.Run(CreateDevice),
    "45" => Sample45TailwindCheckBox.Run(CreateDevice),
    "46" => Sample46TailwindSwitch.Run(CreateDevice),
    "47" => Sample47TailwindWidgets.Run(CreateDevice),
    "50" => Sample50NewDsl.Run(CreateDevice),
    "51" => Sample51SliderSlot.Run(CreateDevice),
    "60" => Sample60FrifloEcs.Run(CreateDevice),
    "61" => Sample61TagsRelations.Run(CreateDevice),
    // Sample 70-86 は Asset* 再構成 (direct-ref) 途上のため一時無効化。csproj の <Compile Remove/> を外し個別移行する。
    "74" => Sample74PlaybackState.Run(CreateDevice),
    "87" => Sample87Input.Run(CreateDevice),
    "88" => Sample88Rebind.Run(CreateDevice),
    "89" => Sample89Audio.Run(CreateDevice),
    "90" => Sample90Audio3D.Run(CreateDevice),
    "91" => Sample91Framework.Run(CreateDevice),
    "92" => Sample92MultiScene.Run(CreateDevice),
    "93" => Sample93MultiSurface.Run(CreateDevice),
    "94" => Sample94DevToolsPreview.Run(CreateDevice,
                port:    args.Length > 2 && int.TryParse(args[2], out int p) ? p : 5173,
                seconds: args.Length > 3 && int.TryParse(args[3], out int s) ? s : 60,
                nativeApp: args.Contains("app")),
    "96" => Sample96Image.Run(CreateDevice),
    "97" => Sample97SurfaceView.Run(CreateDevice),
    "95" => Sample95MultiWindow.Run(CreateDevice,
                port:    args.Length > 2 && int.TryParse(args[2], out int p95) ? p95 : 5175,
                seconds: args.Length > 3 && int.TryParse(args[3], out int s95) ? s95 : 60,
                tsfTest: args.Contains("tsf")),
    _ => Fail($"未知のサンプル: {sample}"),
};
return exit;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}
