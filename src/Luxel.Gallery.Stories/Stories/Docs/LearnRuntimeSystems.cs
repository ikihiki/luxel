using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

internal static class RuntimeCourseCatalog
{
    internal static readonly string[] Routes =
    [
        "Learn/Input/Overview", "Learn/Input/SourcesAndBus", "Learn/Input/ActionsAndContexts",
        "Learn/Input/BindingsAndRebinding", "Learn/Input/PlatformsAndTesting",
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
    [Story("Learn/Input/Overview", Order = 0, SampleBundle = "input.actions", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $"""
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
        """;

    [Story("Learn/Input/SourcesAndBus", Order = 1, Toc = true)]
    public static StoryResult SourcesAndBus(StoryContext ctx) => $"""
        # IInputSourceとInputBus

        {RuntimeCourseCatalog.Meta("Learn/Input/SourcesAndBus", "Beginner", "Gallery / Headless", "Backend neutral", "入力システムの概要")}

        {StoryRef(ctx, "Examples/Input/SourcesAndBus")}

        上のStoryはkeyboard、gamepad、pointerを独立した`IInputSource`として扱い、1 tick分の差分イベントを同じ`InputBus`へ集約します。「次のtickを収集」を押すたびに、キー、軸、ポインターのイベントが一覧へ追加されます。

        ## IInputSourceの役割

        `IInputSource`は物理デバイスやテスト入力を、共通の`InputEvent`列へ変換する境界です。ゲームロジックはWindows、X11、ゲームパッドなどの個別APIを直接参照せず、すべての入力源を`Name`と`Poll(InputBus)`という同じ契約で扱えます。

        | 実装 | 入力元 | 主な用途 |
        |---|---|---|
        | `WindowInputSource` | `Luxel.Platform.Window` | keyboard、pointer button、wheel、pointer position |
        | `XInputSource` | Windows XInput controller | gamepad button、trigger、stick |
        | `FakeInputSource` | コードから予約したイベント | Story、unit test、headless simulation |

        複数のsourceも同じ配列へまとめられます。各sourceは内部にpending eventを保持し、`Poll`が呼ばれたときだけbusへ移します。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-sources-bus-setup")}

        ## InputEventの共通形式

        `InputBus.Events`には次の4種類が並びます。

        | `InputEventKind` | 主なフィールド | 意味 |
        |---|---|---|
        | `KeyDown` | `Key`, `Value = 1` | keyboard、pointer button、gamepad buttonの押下 |
        | `KeyUp` | `Key`, `Value = 0` | buttonの解放 |
        | `AxisChanged` | `Axis`, `Value` | stick、trigger、wheelなどの値 |
        | `PointerMoved` | `Value`, `ValueY` | pointerのX/Y座標 |

        `AxisChanged.Value`の範囲は入力によって異なります。stickは通常`-1`から`1`、triggerは`0`から`1`、wheelはスクロール量です。`PointerMoved`はアクション用の軸ではなく、raw pointer positionとして保持されます。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-sources-bus-events")}

        ## 1 tickの収集順序

        1. tickの開始時に`bus.Clear()`を1回呼びます。
        2. すべてのsourceへ`Poll(bus)`を呼び、同じbusへイベントを追加します。
        3. raw eventを診断表示や記録に使う場合は、この時点で`bus.Events`を読みます。
        4. action layerを使う場合は`stack.Update(bus)`を呼びます。`InputStack.Update`はイベントを保持状態へ反映した後、busをクリアします。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-sources-bus-poll")}

        sourceごとに`bus.Clear()`してはいけません。途中でクリアすると、それ以前のsourceが追加したイベントが失われます。`InputBus`はキーボードやgamepadの保持状態を持つ場所ではなく、そのtickに発生した差分イベントを一時的に集めるqueueです。保持中のキーと軸値は`InputStack`がtickをまたいで管理します。

        ## 押下と解放を別tickにする

        `FakeInputSource.TapKey`は同じtickへ`KeyDown`と`KeyUp`を予約します。raw event列のテストには使えますが、1回の`InputStack.Update`後にはキーが解放済みになるため、`ButtonAction.Triggered`と`Released`の遷移確認には向きません。アクションのedgeを検証するときは、押下をPoll／Updateした後、次のtickで解放をPoll／Updateします。
        """;

    [Story("Learn/Input/ActionsAndContexts", Order = 2, Toc = true)]
    public static StoryResult Actions(StoryContext ctx) => $"""
        # アクションとコンテキスト

        {RuntimeCourseCatalog.Meta("Learn/Input/ActionsAndContexts", "Beginner", "Gallery / Headless", "Backend neutral", "IInputSourceとInputBus")}

        {StoryRef(ctx, "Examples/Input/Actions")}

        上のStoryは`FakeInputSource`でW、D、Spaceの押下と解放を1 tickずつ送り、移動ベクトル、Jumpの保持状態、`Triggered`／`Released`の回数を表示します。

        ## アクションを構成する

        | アクション | 値 | 主な用途 |
        |---|---|---|
        | `ButtonAction` | `bool` | ジャンプ、決定、攻撃 |
        | `Axis1DAction` | `-1`から`1` | 左右移動、アクセル、トリガー |
        | `Axis2DAction` | `Vector2` | WASD移動、スティック入力 |

        `InputContext`へアクションを追加し、物理キーや軸を登録します。ここではSpaceをJumpへ、WASDをMoveへ対応付け、Gameplayコンテキストを`InputStack`へ積みます。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-actions-setup")}

        `Axis2DAction`は上下左右を2次元ベクトルへ合成します。WとDを同時に押した斜め入力は、長さが1を超えないよう正規化されます。

        ## 押下と解放のエッジを処理する

        `ButtonAction.Triggered`は未押下から押下へ変わったtick、`Released`は押下から未押下へ変わったtickで1回だけ発火します。押し続けている間は`IsActive`がtrueですが、`Triggered`は繰り返し発火しません。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-actions-edges")}

        `Tick`では最初に`source.Poll(bus)`で差分イベントを集め、次に`stack.Update(bus)`で保持状態とアクション値を更新します。エッジを確認するときは押下と解放を別々のtickで処理します。

        ## コンテキストの優先順位と入力の消費

        {StoryRef(ctx, "Examples/Input/ContextStack")}

        上のStoryはGameplayの上にMenuを積み、同じEnterがどちらへ届くかを表示します。

        ### スタックを構成する

        `InputStack`は最後に`Push`したコンテキストから評価します。Menuを最後に積むことで、MenuがGameplayより上位になります。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-context-setup")}

        ### 上位コンテキストで入力を消費する

        上位コンテキストのactive actionが使用したキーや軸は消費され、下位コンテキストには渡りません。押下tickで結果を読み、解放tickで保持状態を戻します。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-context-routing")}

        ### コンテキストを一時停止する

        `SetSuspended`を使うと、コンテキストをスタックから外さず評価対象から除外できます。Menuを停止すると、同じEnterをGameplayが受け取ります。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-context-suspension")}
        """;

    [Story("Learn/Input/BindingsAndRebinding", Order = 3, Toc = true)]
    public static StoryResult Bindings(StoryContext ctx) => $"""
        # バインディングとキーの再設定

        {RuntimeCourseCatalog.Meta("Learn/Input/BindingsAndRebinding", "Beginner", "Gallery / Settings", "Backend neutral", "アクションとコンテキスト")}

        {StoryRef(ctx, "Examples/Input/Bindings")}

        上のStoryはJumpのバインドをSpaceとEnterで切り替え、表示中のJSONを読み戻してから`InputBindingsApplier`へ反映します。その後、各キーをシミュレートして、新しい設定でJumpが発火するか確認できます。

        ## アクションとテスト用入力を用意する

        ゲームロジックには「Spaceが押されたか」ではなく「Jumpが有効か」を問い合わせます。`Jump`というアクション名を契約として固定し、物理キーとの対応だけを変更します。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-bindings-setup")}

        この分離により、ユーザーごとのキー設定、初期設定へのリセット、設定ファイルへの保存と読み込み、キーボードとゲームパッドの複数バインドを、ゲームロジックを変更せず実装できます。

        ## JSONとして保存する

        `InputBindings.Actions`のキーは論理アクション名です。`InputBindingEntry.Kind`でbutton／axisの種類を示し、`Keys`、`Pairs`、`Quads`、`Axes`へ物理入力名を保存します。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-bindings-json")}

        JSONには`Jump`と`Space`または`Enter`の対応だけが含まれます。ゲーム側はこの物理キー名を直接参照しません。

        ## JSONを読み込み、コンテキストへ反映する

        1. JSONを`InputBindings`へデシリアライズします。
        2. `InputBindingsApplier.Apply(bindings, context)`を呼びます。
        3. Applierがアクション名を照合し、`ButtonAction.Keys`などを更新します。
        4. 以降のtickから、新しい物理キーで同じJumpアクションが有効になります。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-bindings-apply")}

        ## 反映したバインディングを確認する

        新しいキーを押して1 tick進め、Jumpのactive状態を読みます。その後、キーを解放して次のtickへ進めます。保存・読み込みとアクション評価を同じStory内で確認できます。

        {SampleSource("src/Luxel.Gallery.Stories.CoreUi/Stories/InputActionStories.cs", "input-bindings-simulate")}

        ## 再設定UIの責務

        再設定UIは、ユーザーが次に押した物理キーまたはボタンを取得し、対象アクションのbinding entryを書き換えます。実ウィンドウでは`WindowInputSource.TakePressed()`を使って直近のキー押下を取得できます。

        文字入力やIME compositionはキー設定ではなく`Luxel.UI`のテキスト入力経路で扱います。表示文字と物理キーを混同しないことが重要です。
        """;

    [Story("Learn/Input/PlatformsAndTesting", Order = 4, Toc = true)]
    public static StoryResult Platforms(StoryContext ctx) => $"""
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

public static class LearnResources
{
    [Story("Learn/Resources/Overview", Order = 0, SampleBundle = "resources.pipeline", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # Resources overview

        {RuntimeCourseCatalog.Meta("Learn/Resources/Overview", "Beginner", "Standalone / Gallery / Headless", "CPU / optional GPU steps", "Audio track")}

        `ResourceSystem`は `(requested type, URI)` をcache keyにし、sourceとtyped stepを自動合成します。asset利用側は`ResourceHandle<T>`を保持し、不要になったらDisposeします。

        {SampleBundle("resources.pipeline")}
        """;

    [Story("Learn/Resources/PipelinesAndDag", Order = 1, Toc = true)]
    public static StoryResult Pipelines(StoryContext ctx) => $"""
        # Typed pipelines and dependency DAG

        {RuntimeCourseCatalog.Meta("Learn/Resources/PipelinesAndDag", "Intermediate", "Standalone / Headless", "IO / CPU / GPU lanes", "Resources overview")}

        `IResourceSource`がURIからbytesを読み、`IResourceStep<TIn,TOut>`が型を変換します。requested output typeからstepを逆引きするため、`byte[] → decoded → GPU resource`の任意長chainを組めます。step内の`LoadContext.Load`はdependency edgeを作り、共有・reload伝播・eviction順序へ使われます。

        {SampleSource("samples/LuxelResources/Program.cs", "resource-pipeline")}
        """;

    [Story("Learn/Resources/ReloadAndLifetime", Order = 2, Toc = true)]
    public static StoryResult Reload(StoryContext ctx) => $"""
        # Reload, publish, and lifetime

        {RuntimeCourseCatalog.Meta("Learn/Resources/ReloadAndLifetime", "Intermediate", "Game loop / DevTools / CI", "Backend neutral", "Pipeline and DAG")}

        `Watch()`後のfile change、`Republish()`、dependency reloadは非同期に計算され、value swap、`Reloaded`通知、deferred disposeは`Pump()`境界で適用されます。refcountが0でdependentも無いnodeから連鎖evictionされます。GPU値のdispose前にはidle hookを設定できます。
        """;
}
