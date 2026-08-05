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

        {StoryRef(ctx, "Examples/Input/Actions")}

        上のStoryは`FakeInputSource`でW、D、Spaceの押下と解放を1 tickずつ送り、移動ベクトル、Jumpの保持状態、`Triggered`／`Released`の回数を表示します。

        ## アクションを構成する

        | アクション | 値 | 主な用途 |
        |---|---|---|
        | `ButtonAction` | `bool` | ジャンプ、決定、攻撃 |
        | `Axis1DAction` | `-1`から`1` | 左右移動、アクセル、トリガー |
        | `Axis2DAction` | `Vector2` | WASD移動、スティック入力 |

        `InputContext`へアクションを追加し、物理キーや軸を登録します。ここではSpaceをJumpへ、WASDをMoveへ対応付け、Gameplayコンテキストを`InputStack`へ積みます。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-actions-setup")}

        `Axis2DAction`は上下左右を2次元ベクトルへ合成します。WとDを同時に押した斜め入力は、長さが1を超えないよう正規化されます。

        ## 押下と解放のエッジを処理する

        `ButtonAction.Triggered`は未押下から押下へ変わったtick、`Released`は押下から未押下へ変わったtickで1回だけ発火します。押し続けている間は`IsActive`がtrueですが、`Triggered`は繰り返し発火しません。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-actions-edges")}

        `Tick`では最初に`source.Poll(bus)`で差分イベントを集め、次に`stack.Update(bus)`で保持状態とアクション値を更新します。エッジを確認するときは押下と解放を別々のtickで処理します。

        ## コンテキストの優先順位と入力の消費

        {StoryRef(ctx, "Examples/Input/ContextStack")}

        上のStoryはGameplayの上にMenuを積み、同じEnterがどちらへ届くかを表示します。

        ### スタックを構成する

        `InputStack`は最後に`Push`したコンテキストから評価します。Menuを最後に積むことで、MenuがGameplayより上位になります。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-context-setup")}

        ### 上位コンテキストで入力を消費する

        上位コンテキストのactive actionが使用したキーや軸は消費され、下位コンテキストには渡りません。押下tickで結果を読み、解放tickで保持状態を戻します。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-context-routing")}

        ### コンテキストを一時停止する

        `SetSuspended`を使うと、コンテキストをスタックから外さず評価対象から除外できます。Menuを停止すると、同じEnterをGameplayが受け取ります。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-context-suspension")}
        """, toc: true);

    [Story("Learn/Input/BindingsAndRebinding", Order = 2)]
    public static Widget Bindings(StoryContext ctx) => DocNew(ctx, $"""
        # バインディングとキーの再設定

        {RuntimeCourseCatalog.Meta("Learn/Input/BindingsAndRebinding", "Beginner", "Gallery / Settings", "Backend neutral", "アクションとコンテキスト")}

        {StoryRef(ctx, "Examples/Input/Bindings")}

        上のStoryはJumpのバインドをSpaceとEnterで切り替え、表示中のJSONを読み戻してから`InputBindingsApplier`へ反映します。その後、各キーをシミュレートして、新しい設定でJumpが発火するか確認できます。

        ## アクションとテスト用入力を用意する

        ゲームロジックには「Spaceが押されたか」ではなく「Jumpが有効か」を問い合わせます。`Jump`というアクション名を契約として固定し、物理キーとの対応だけを変更します。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-bindings-setup")}

        この分離により、ユーザーごとのキー設定、初期設定へのリセット、設定ファイルへの保存と読み込み、キーボードとゲームパッドの複数バインドを、ゲームロジックを変更せず実装できます。

        ## JSONとして保存する

        `InputBindings.Actions`のキーは論理アクション名です。`InputBindingEntry.Kind`でbutton／axisの種類を示し、`Keys`、`Pairs`、`Quads`、`Axes`へ物理入力名を保存します。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-bindings-json")}

        JSONには`Jump`と`Space`または`Enter`の対応だけが含まれます。ゲーム側はこの物理キー名を直接参照しません。

        ## JSONを読み込み、コンテキストへ反映する

        1. JSONを`InputBindings`へデシリアライズします。
        2. `InputBindingsApplier.Apply(bindings, context)`を呼びます。
        3. Applierがアクション名を照合し、`ButtonAction.Keys`などを更新します。
        4. 以降のtickから、新しい物理キーで同じJumpアクションが有効になります。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-bindings-apply")}

        ## 反映したバインディングを確認する

        新しいキーを押して1 tick進め、Jumpのactive状態を読みます。その後、キーを解放して次のtickへ進めます。保存・読み込みとアクション評価を同じStory内で確認できます。

        {SampleSource("src/Luxel.Gallery.Stories/Stories/InputActionStories.cs", "input-bindings-simulate")}

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
