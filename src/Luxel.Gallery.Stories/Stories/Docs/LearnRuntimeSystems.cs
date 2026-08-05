using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

internal static class RuntimeCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Input/Overview", "Learn/Input/ActionsAndContexts", "Learn/Input/BindingsAndRebinding",
        "Learn/Input/BrowserWasm", "Learn/Input/PlatformsAndTesting",
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

        {RuntimeCourseCatalog.Meta("Learn/Input/Overview", "Beginner", "Gallery / Browser / Headless", "Window / Web / Fake / XInput", "なし")}

        Luxel.Inputは物理device eventをgameplayの語彙へ変換します。game codeは`WindowKey.W`やDOM eventではなく、`Move`、`Jump`、`Fire`のようなactionだけを読みます。

        ```mermaid
        flowchart LR
          A[Platform / Fake source] --> B[InputBus]
          B --> C[InputStack]
          C --> D[InputContext]
          D --> E[Button / Axis actions]
          E --> F[Game logic]
        ```

        | 用語 | 役割 |
        |---|---|
        | `IInputSource` | window、controller、test fixtureからraw eventを収集する |
        | `InputBus` | 1 tick分の差分eventをまとめる |
        | `InputStack` | held stateを保持し、context priorityとconsumptionを適用する |
        | `InputAction` | button、1D axis、2D axisとして論理値とedgeを公開する |
        | `InputBindings` | action名と物理key/axisの対応をdataとして保持する |

        まず同じstoryをnative GalleryまたはexportされたBrowser/WASM Galleryで操作してください。

        {StoryRef(ctx, "Examples/Input/WindowActions")}

        TextFieldやIMEの文字入力は`Luxel.UI`の責務です。`Luxel.Input`は物理操作とgame/action mappingを扱い、入力文字列は扱いません。

        Copy/paste可能なheadless最小例は次のbundleです。

        {SampleBundle("input.actions")}
        """, toc: true);

    [Story("Learn/Input/ActionsAndContexts", Order = 1)]
    public static Widget Actions(StoryContext ctx) => DocNew(ctx, $"""
        # Actions and contexts

        {RuntimeCourseCatalog.Meta("Learn/Input/ActionsAndContexts", "Beginner", "Gallery / Headless", "Backend neutral", "Input overview")}

        `ButtonAction`はbool、`Axis1DAction`は`-1..1`、`Axis2DAction`は正規化された`Vector2`を公開します。`Triggered`と`Released`はactive stateの立ち上がり／立ち下がりで一度だけ発火します。

        `InputStack`は最後にpushしたcontextから評価します。上位contextのactive actionが使用したkey/axisは下位contextへ渡りません。menuをgameplayより上に置き、text editing中はgameplay contextをsuspendする構成が基本です。

        {StoryRef(ctx, "Examples/Input/ContextStack")}

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-context-stack")}
        """, toc: true);

    [Story("Learn/Input/BindingsAndRebinding", Order = 2)]
    public static Widget Bindings(StoryContext ctx) => DocNew(ctx, $"""
        # Bindings and rebinding

        {RuntimeCourseCatalog.Meta("Learn/Input/BindingsAndRebinding", "Beginner", "Gallery / Settings", "Backend neutral", "Actions and contexts")}

        Bindingはcode branchではなくdataです。`InputBindings`をJSONへ保存し、`InputBindingsApplier.Apply`で既存の`InputContext`へ反映します。action名を安定した契約にすると、platformやuser preferenceが変わってもgame codeは変わりません。

        {StoryRef(ctx, "Examples/Input/Bindings")}

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-bindings-story")}

        Rebind UIが待つのは物理key/buttonです。文字入力、IME composition、keyboard layoutに依存するtextはUI layerへ渡してください。
        """, toc: true);

    [Story("Learn/Input/BrowserWasm", Order = 3)]
    public static Widget BrowserWasm(StoryContext ctx) => DocNew(ctx, $"""
        # Browser / WASM input

        {RuntimeCourseCatalog.Meta("Learn/Input/BrowserWasm", "Intermediate", "Gallery static site / Browser", "Luxel.Platform.Web + webgpu-browser-v1", "Bindings and rebinding")}

        Browser版は専用sampleを複製せず、native Galleryと同じ`Examples/Input/WindowActions` storyを既存の`webgpu-browser-v1` runtimeで実行します。static exportでは`StoryRef`がruntime iframeへ変換されます。

        ```mermaid
        flowchart LR
          DOM[DOM keyboard / pointer] --> WEB[Luxel.Platform.Web]
          WEB --> WIN[portable Window events]
          WIN --> SRC[WindowInputSource]
          SRC --> HOST[IStoryInputRuntime]
          HOST --> STORY[CoreUi input story]
        ```

        {StoryRef(ctx, "Examples/Input/WindowActions")}

        - canvasをクリックしてfocusしてからkeyboardを使います。
        - window/canvasのblur時は保持中key/buttonをreleaseし、stuck actionを防ぎます。
        - DOM Pointer Eventsは受信しますが、pointer id/type/pressureはaction modelへ保持しません。
        - Web Gamepad API、multitouch/gesture、mouse movement axisは未対応です。
        - text/composition eventは`UiHost`へ渡され、action layerには入りません。
        """, toc: true);

    [Story("Learn/Input/PlatformsAndTesting", Order = 4)]
    public static Widget Platforms(StoryContext ctx) => DocNew(ctx, $"""
        # Platform input and deterministic tests

        {RuntimeCourseCatalog.Meta("Learn/Input/PlatformsAndTesting", "Beginner", "Window / Browser / CI", "Win32 / Silk X11 / Web / Fake / XInput", "Browser / WASM input")}

        | Platform | Window keyboard/pointer | Text / IME | Gamepad | 検証済み範囲 |
        |---|---|---|---|---|
        | Windows | Win32 + `WindowInputSource` | UI layerで対応 | optional `Luxel.Input.XInput` | real window、action、XInput stories/tests |
        | Linux | Silk backend (X11) | UI layerで対応 | 未提供 | X11 backend tests。Waylandは未対応 |
        | Browser/WASM | `Luxel.Platform.Web` + canonical CoreUi story | UI layerでcomposition対応 | Web Gamepad未対応 | keyboard、pointer button、blur release |
        | macOS | real window backend未提供 | 未提供 | 未提供 | `FakeInputSource`によるportable/headless logicのみ |
        | Mobile | backend未提供 | 未提供 | 未提供 | touch/multitouch対応を意味しない |

        game logicのtestは`FakeInputSource`を使い、`source.Poll(bus); stack.Update(bus);`を1 tickとして検証します。edgeを検証するときはpress tickとrelease tickを分けます。同一tickでdown/upする`TapKey`では、`InputStack.Update`時点の最終held stateはfalseです。

        Platform adapterのtestはraw window eventの変換とfocus lossを担当し、gameplay testはwindowを作らずactions/contexts/bindingsだけを検証します。

        {SampleSource("samples/LuxelInput/Program.cs", "input-actions")}
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
