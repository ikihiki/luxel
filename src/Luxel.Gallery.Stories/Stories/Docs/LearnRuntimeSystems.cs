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
    [Story("Learn/Input/Overview", Order = 0, SampleBundle = "input.actions", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Input overview

        {RuntimeCourseCatalog.Meta("Learn/Input/Overview", "Beginner", "Standalone / Gallery / Headless", "Window / Fake / XInput", "なし")}

        Inputは `IInputSource → InputBus → InputStack → InputAction` の順に流れます。アプリのロジックはplatform eventではなく、JumpやMoveのようなactionを読みます。

        {SampleBundle("input.actions")}
        """;

    [Story("Learn/Input/ActionsAndContexts", Order = 1, Toc = true)]
    public static StoryResult Actions(StoryContext ctx) => $"""
        # Actions and contexts

        {RuntimeCourseCatalog.Meta("Learn/Input/ActionsAndContexts", "Beginner", "Standalone / Headless", "Backend neutral", "Input overview")}

        `ButtonAction`、`Axis1DAction`、`Axis2DAction`はkeyboardとgamepadを同じ論理値へ統合します。`InputContext`を`InputStack`へ積むと、上位contextのactive actionが使用したkey/axisをconsumeします。menuを上に積み、gameplayをsuspendする構成が基本です。

        {SampleSource("samples/LuxelInput/Program.cs", "input-actions")}
        """;

    [Story("Learn/Input/PlatformsAndTesting", Order = 2, Toc = true)]
    public static StoryResult Platforms(StoryContext ctx) => $"""
        # Platform input and deterministic tests

        {RuntimeCourseCatalog.Meta("Learn/Input/PlatformsAndTesting", "Beginner", "Window / CI", "Silk/Win32 + optional XInput", "Actions and contexts")}

        実windowは`WindowInputSource`、Windows gamepadは別assemblyの`XInputSource`です。ゲームロジックのtestは`FakeInputSource`を使い、`source.Poll(bus); stack.Update(bus);`を1 tickとして検証します。同一tickでdown/upする`TapKey`では最終heldがfalseになるため、edgeを検証する場合はpressとreleaseを別tickにします。
        """;
}

public static class LearnAudio
{
    [Story("Learn/Audio/Overview", Order = 0, SampleBundle = "audio.tone", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Audio overview

        {RuntimeCourseCatalog.Meta("Learn/Audio/Overview", "Beginner", "Standalone / Headless / Windows device", "Null / XAudio2", "Input track")}

        `AudioClip`はdecode済みPCM、`IAudioVoice`は再生slot、`AudioMixer`はone-shotとvoice poolを管理します。実音出力は現在Windows/XAudio2、CIと非Windowsは`NullAudioBackend`で状態遷移を検証します。

        {SampleBundle("audio.tone")}
        """;

    [Story("Learn/Audio/ClipsSourcesAndBuses", Order = 1, Toc = true)]
    public static StoryResult Clips(StoryContext ctx) => $"""
        # Clips, sources, and buses

        {RuntimeCourseCatalog.Meta("Learn/Audio/ClipsSourcesAndBuses", "Beginner", "Standalone / Game loop", "Backend neutral", "Audio overview")}

        SFXは`AudioClip`を`AudioMixer.PlayOneShot`へ渡します。持続音は`AudioSource`でvolume/pitch/panをSignalとして更新し、master/music/sfxの階層音量は`AudioBus`で掛け合わせます。`AudioMixer.Tick()`とsourceの`Tick()`はframe loopから呼びます。

        {SampleSource("samples/LuxelAudio/Program.cs", "audio-tone")}
        """;

    [Story("Learn/Audio/SpatialStreamingAndTesting", Order = 2, Toc = true)]
    public static StoryResult Spatial(StoryContext ctx) => $"""
        # Spatial audio, streaming, and testing

        {RuntimeCourseCatalog.Meta("Learn/Audio/SpatialStreamingAndTesting", "Intermediate", "Game loop / Windows audio / CI", "XAudio2 / Null", "Clips and buses")}

        `AudioSource3D`はlistenerとの距離減衰と左右panを計算します。HRTF/Dopplerではありません。長い音声は`WavStream`/`LoopingStream`と`StreamingVoice.Pump()`を使います。headless smokeでは実音ではなくInitialized、voice数、queue、playing、submitted bytesをcheckpointにします。

        実音例は [RealWindow/Audio/Tone](story:RealWindow/Audio/Tone) です。
        """;
}
