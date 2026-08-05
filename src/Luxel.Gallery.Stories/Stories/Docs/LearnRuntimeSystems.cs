using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

internal static class RuntimeCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Input/Overview", "Learn/Input/ActionsAndContexts", "Learn/Input/BindingsAndRebinding",
        "Learn/Input/PlatformsAndTesting",
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
        # 入力システムの概要

        {RuntimeCourseCatalog.Meta("Learn/Input/Overview", "Beginner", "Gallery / Headless", "Window / Fake / XInput", "なし")}

        `Luxel.Input`は、キーボードやゲームパッドの物理入力を、`Move`や`Jump`のようなゲーム内の意味へ変換します。ゲームロジックが特定のキーを直接読むのではなく、論理アクションを読むようにすることで、入力機器やキー設定を変更してもゲーム側のコードを保てます。

        ```mermaid
        flowchart LR
          A[IInputSource] --> B[InputBus]
          B --> C[InputStack]
          C --> D[InputContext]
          D --> E[InputAction]
          E --> F[ゲームロジック]
        ```

        | 要素 | 役割 |
        |---|---|
        | `IInputSource` | 実ウィンドウ、ゲームパッド、テスト用入力からイベントを収集する |
        | `InputBus` | 1 tickで発生した入力イベントを一時的に保持する |
        | `InputStack` | 押下状態を保持し、コンテキストの優先順位と消費を適用する |
        | `InputContext` | Gameplay、Menuなど、同時に有効にするアクションをまとめる |
        | `InputAction` | Button、1D軸、2D軸としてゲームロジックへ値を公開する |
        | `InputBindings` | アクションと物理キー／軸の対応をデータとして保持する |

        最初にアクションのStoryを操作し、押下、保持、解放がtickごとにどう変化するか確認してください。

        {StoryRef(ctx, "Examples/Input/Actions")}

        TextFieldやIMEによる文字入力は`Luxel.UI`の責務です。`Luxel.Input`はゲーム操作のための物理入力と論理アクションを扱います。

        {SampleBundle("input.actions")}
        """, toc: true);

    [Story("Learn/Input/ActionsAndContexts", Order = 1)]
    public static Widget Actions(StoryContext ctx) => DocNew(ctx, $"""
        # アクションとコンテキスト

        {RuntimeCourseCatalog.Meta("Learn/Input/ActionsAndContexts", "Beginner", "Gallery / Headless", "Backend neutral", "入力システムの概要")}

        ## アクションの種類

        | アクション | 値 | 主な用途 |
        |---|---|---|
        | `ButtonAction` | `bool` | ジャンプ、決定、攻撃 |
        | `Axis1DAction` | `-1`から`1` | 左右移動、アクセル、トリガー |
        | `Axis2DAction` | `Vector2` | WASD移動、スティック入力 |

        `ButtonAction`の`Triggered`は未押下から押下へ変わったtick、`Released`は押下から未押下へ変わったtickで1回だけ発火します。押し続けている間は`IsActive`がtrueですが、`Triggered`は繰り返し発火しません。

        `Axis2DAction`へWASDを登録すると、左右と上下のボタンを2次元ベクトルへ合成します。斜め入力は長さが1を超えないように正規化されます。

        {StoryRef(ctx, "Examples/Input/Actions")}

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-actions-story")}

        ## 1 tickの処理順

        1. `IInputSource.Poll(bus)`で、そのtickの入力イベントを`InputBus`へ送ります。
        2. `InputStack.Update(bus)`で押下状態を更新し、各アクションの値を計算します。
        3. ゲームロジックがアクションの値や`Triggered`／`Released`を読みます。

        押下と解放のエッジを確認するときは、押下と解放を別々のtickで処理します。

        ## コンテキストの優先順位と入力の消費

        `InputContext`は、ある場面で有効なアクションをまとめます。たとえばGameplayとMenuを別コンテキストにすると、メニュー表示中だけMenuを優先できます。

        `InputStack`は最後に`Push`したコンテキストから評価します。上位コンテキストで有効になったアクションが使用するキーや軸は消費され、下位コンテキストには渡りません。また、`SetSuspended`を使うとコンテキストをスタックから外さず一時停止できます。

        {StoryRef(ctx, "Examples/Input/ContextStack")}

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-context-stack")}
        """, toc: true);

    [Story("Learn/Input/BindingsAndRebinding", Order = 2)]
    public static Widget Bindings(StoryContext ctx) => DocNew(ctx, $"""
        # バインディングとキーの再設定

        {RuntimeCourseCatalog.Meta("Learn/Input/BindingsAndRebinding", "Beginner", "Gallery / Settings", "Backend neutral", "アクションとコンテキスト")}

        ## バインディングをデータにする理由

        ゲームロジックには「Spaceが押されたか」ではなく「Jumpが有効か」を問い合わせます。`Jump`というアクション名をゲーム側の契約として固定し、SpaceやEnterとの対応を`InputBindings`へ分離します。

        この分離により、次の処理をゲームロジックを変更せず実装できます。

        - ユーザーごとのキー設定
        - 初期設定へのリセット
        - 設定ファイルへの保存と読み込み
        - キーボードとゲームパッドの複数バインド

        ## 読み込みと反映

        1. JSONを`InputBindings`へデシリアライズします。
        2. `InputBindingsApplier.Apply(bindings, context)`を呼びます。
        3. Applierがアクション名を照合し、`ButtonAction.Keys`、`Axis1DAction.ButtonPairs`、`Axis2DAction.ButtonQuads`などを更新します。
        4. 以降のtickから、新しい物理キーで同じ論理アクションが有効になります。

        次のStoryでは、JumpのバインドをSpaceとEnterで切り替えた後、それぞれのキーをシミュレートして結果を確認できます。表示しているJSONを読み戻してから`InputBindingsApplier`へ渡しているため、保存・読み込みと同じ境界を通ります。

        {StoryRef(ctx, "Examples/Input/Bindings")}

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-bindings-story")}

        ## 再設定UIの責務

        再設定UIは、ユーザーが次に押した物理キーまたはボタンを取得し、対象アクションのbinding entryを書き換えます。実ウィンドウでは`WindowInputSource.TakePressed()`を使って直近のキー押下を取得できます。

        文字入力やIME compositionはキー設定ではなく`Luxel.UI`のテキスト入力経路で扱います。表示文字と物理キーを混同しないことが重要です。
        """, toc: true);

    [Story("Learn/Input/PlatformsAndTesting", Order = 3)]
    public static Widget Platforms(StoryContext ctx) => DocNew(ctx, $"""
        # プラットフォーム入力と決定的テスト

        {RuntimeCourseCatalog.Meta("Learn/Input/PlatformsAndTesting", "Beginner", "Window / CI", "Win32 / Silk X11 / Fake / XInput", "バインディングとキーの再設定")}

        | Platform | Window keyboard/pointer | Gamepad | 検証範囲 |
        |---|---|---|---|
        | Windows | Win32 + `WindowInputSource` | optional `Luxel.Input.XInput` | 実ウィンドウ入力、アクション、XInput |
        | Linux | Silk backend（X11） | 未提供 | X11 keyboard／pointer。Waylandは未対応 |
        | macOS | 実ウィンドウbackend未提供 | 未提供 | `FakeInputSource`によるheadlessロジックのみ |
        | Mobile | backend未提供 | 未提供 | touch／multitouchは未対応 |

        ゲームロジックのテストでは`FakeInputSource`を使い、ウィンドウや実機器なしで同じ`InputBus`と`InputStack`を更新します。押下と解放を別tickにすれば、`Triggered`と`Released`も決定的に検証できます。

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
