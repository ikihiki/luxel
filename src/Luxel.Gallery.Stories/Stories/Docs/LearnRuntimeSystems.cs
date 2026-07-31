using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

internal static class RuntimeCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Input/Overview", "Learn/Input/ActionsAndContexts", "Learn/Input/PlatformsAndTesting",
        "Learn/Audio/Overview", "Learn/Audio/ClipsSourcesAndBuses", "Learn/Audio/SpatialStreamingAndTesting",
        "Learn/Resources/Overview", "Learn/Resources/PipelinesAndDag", "Learn/Resources/ReloadAndLifetime",
    ];
    internal static DocMarkdown Meta(string path, string difficulty, string environment, string backend, string prerequisites)
    {
        int i = Array.IndexOf(Routes, path);
        string? previous = i > 0 ? Routes[i - 1] : null;
        string? next = i >= 0 && i + 1 < Routes.Length ? Routes[i + 1] : null;
        return RenderingMeta(difficulty, environment, backend, prerequisites, previous, next);
    }
}

public static class LearnInput
{
    [Story("Learn/Input/Overview", Order = 0, SampleBundle = "input.actions")]
    public static Widget Overview(StoryContext ctx) => DocNew(ctx, $"""
        # Input overview

        {RuntimeCourseCatalog.Meta("Learn/Input/Overview", "Beginner", "Standalone / Gallery / Headless", "Window / Fake / XInput", "なし")}

        Inputは `IInputSource → InputBus → InputStack → InputAction` の順に流れます。アプリのロジックはplatform eventではなく、JumpやMoveのようなactionを読みます。

        {SampleBundle("input.actions")}
        """, toc: true);

    [Story("Learn/Input/ActionsAndContexts", Order = 1)]
    public static Widget Actions(StoryContext ctx) => DocNew(ctx, $"""
        # Actions and contexts

        {RuntimeCourseCatalog.Meta("Learn/Input/ActionsAndContexts", "Beginner", "Standalone / Headless", "Backend neutral", "Input overview")}

        `ButtonAction`、`Axis1DAction`、`Axis2DAction`はkeyboardとgamepadを同じ論理値へ統合します。`InputContext`を`InputStack`へ積むと、上位contextのactive actionが使用したkey/axisをconsumeします。menuを上に積み、gameplayをsuspendする構成が基本です。

        {SampleSource("samples/LuxelInput/Program.cs", "input-actions")}
        """, toc: true);

    [Story("Learn/Input/PlatformsAndTesting", Order = 2)]
    public static Widget Platforms(StoryContext ctx) => DocNew(ctx, $"""
        # Platform input and deterministic tests

        {RuntimeCourseCatalog.Meta("Learn/Input/PlatformsAndTesting", "Beginner", "Window / CI", "Silk/Win32 + optional XInput", "Actions and contexts")}

        実windowは`WindowInputSource`、Windows gamepadは別assemblyの`XInputSource`です。ゲームロジックのtestは`FakeInputSource`を使い、`source.Poll(bus); stack.Update(bus);`を1 tickとして検証します。同一tickでdown/upする`TapKey`では最終heldがfalseになるため、edgeを検証する場合はpressとreleaseを別tickにします。
        """, toc: true);
}

public static class LearnAudio
{
    [Story("Learn/Audio/Overview", Order = 0, SampleBundle = "audio.tone")]
    public static Widget Overview(StoryContext ctx) => DocNew(ctx, $"""
        # Audio overview

        {RuntimeCourseCatalog.Meta("Learn/Audio/Overview", "Beginner", "Standalone / Headless / Windows device", "Null / XAudio2", "Input track")}

        `AudioClip`はdecode済みPCM、`IAudioVoice`は再生slot、`AudioMixer`はone-shotとvoice poolを管理します。実音出力は現在Windows/XAudio2、CIと非Windowsは`NullAudioBackend`で状態遷移を検証します。

        {SampleBundle("audio.tone")}
        """, toc: true);

    [Story("Learn/Audio/ClipsSourcesAndBuses", Order = 1)]
    public static Widget Clips(StoryContext ctx) => DocNew(ctx, $"""
        # Clips, sources, and buses

        {RuntimeCourseCatalog.Meta("Learn/Audio/ClipsSourcesAndBuses", "Beginner", "Standalone / Game loop", "Backend neutral", "Audio overview")}

        SFXは`AudioClip`を`AudioMixer.PlayOneShot`へ渡します。持続音は`AudioSource`でvolume/pitch/panをSignalとして更新し、master/music/sfxの階層音量は`AudioBus`で掛け合わせます。`AudioMixer.Tick()`とsourceの`Tick()`はframe loopから呼びます。

        {SampleSource("samples/LuxelAudio/Program.cs", "audio-tone")}
        """, toc: true);

    [Story("Learn/Audio/SpatialStreamingAndTesting", Order = 2)]
    public static Widget Spatial(StoryContext ctx) => DocNew(ctx, $"""
        # Spatial audio, streaming, and testing

        {RuntimeCourseCatalog.Meta("Learn/Audio/SpatialStreamingAndTesting", "Intermediate", "Game loop / Windows audio / CI", "XAudio2 / Null", "Clips and buses")}

        `AudioSource3D`はlistenerとの距離減衰と左右panを計算します。HRTF/Dopplerではありません。長い音声は`WavStream`/`LoopingStream`と`StreamingVoice.Pump()`を使います。headless smokeでは実音ではなくInitialized、voice数、queue、playing、submitted bytesをcheckpointにします。

        実音例は [RealWindow/Audio/Tone](story:RealWindow/Audio/Tone) です。
        """, toc: true);
}

public static class LearnResources
{
    [Story("Learn/Resources/Overview", Order = 0, SampleBundle = "resources.pipeline")]
    public static Widget Overview(StoryContext ctx) => DocNew(ctx, $"""
        # Resources overview

        {RuntimeCourseCatalog.Meta("Learn/Resources/Overview", "Beginner", "Standalone / Gallery / Headless", "CPU / optional GPU steps", "Audio track")}

        `ResourceSystem`は `(requested type, URI)` をcache keyにし、sourceとtyped stepを自動合成します。asset利用側は`ResourceHandle<T>`を保持し、不要になったらDisposeします。

        {SampleBundle("resources.pipeline")}
        """, toc: true);

    [Story("Learn/Resources/PipelinesAndDag", Order = 1)]
    public static Widget Pipelines(StoryContext ctx) => DocNew(ctx, $"""
        # Typed pipelines and dependency DAG

        {RuntimeCourseCatalog.Meta("Learn/Resources/PipelinesAndDag", "Intermediate", "Standalone / Headless", "IO / CPU / GPU lanes", "Resources overview")}

        `IResourceSource`がURIからbytesを読み、`IResourceStep<TIn,TOut>`が型を変換します。requested output typeからstepを逆引きするため、`byte[] → decoded → GPU resource`の任意長chainを組めます。step内の`LoadContext.Load`はdependency edgeを作り、共有・reload伝播・eviction順序へ使われます。

        {SampleSource("samples/LuxelResources/Program.cs", "resource-pipeline")}
        """, toc: true);

    [Story("Learn/Resources/ReloadAndLifetime", Order = 2)]
    public static Widget Reload(StoryContext ctx) => DocNew(ctx, $"""
        # Reload, publish, and lifetime

        {RuntimeCourseCatalog.Meta("Learn/Resources/ReloadAndLifetime", "Intermediate", "Game loop / DevTools / CI", "Backend neutral", "Pipeline and DAG")}

        `Watch()`後のfile change、`Republish()`、dependency reloadは非同期に計算され、value swap、`Reloaded`通知、deferred disposeは`Pump()`境界で適用されます。refcountが0でdependentも無いnodeから連鎖evictionされます。GPU値のdispose前にはidle hookを設定できます。
        """, toc: true);
}
