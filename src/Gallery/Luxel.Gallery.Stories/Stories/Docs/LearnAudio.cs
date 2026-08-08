using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnAudio
{
    [Story("Learn/Audio/Overview", Order = 0, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Audio overview

        {{AudioCourseCatalog.Meta("Learn/Audio/Overview", "Beginner", "Gallery / Headless / Windows / Linux / macOS / Browser WASM", "Null / XAudio2 / Silk OpenAL / Web Audio", "なし")}}

        ## このコースで作るメンタルモデル

        Luxelのaudio経路は、**PCMの意味を記述するformat**、**全展開されたclip**、**backendが所有するvoice**、**OSのdevice**の4段です。`AudioMixer`、`AudioSource`、`AudioSource3D`、`StreamingVoice`は、この低水準経路を用途別に安全に駆動します。

        ```mermaid
        flowchart LR
          PCM[PCM bytes] --> Clip[AudioClip + AudioFormat]
          Clip --> Voice[IAudioVoice]
          Voice --> Backend[IAudioBackend]
          Backend --> Device[device / no-op observer]
        ```

        | 用途 | 最初に選ぶAPI | 所有・毎frame処理 |
        |---|---|---|
        | 短い効果音 | `AudioMixer.PlayOneShot` | mixerを保持し`Tick()` |
        | loopするBGMや持続音 | `AudioSource` | sourceを保持し`Tick()`、最後に`Dispose()` |
        | 位置付き音 | `AudioSource3D` | listener更新後に`Update(listener)` |
        | 長尺WAV | `WavStream` + `StreamingVoice` | 毎frame `Pump()` |

        ## 最小の実行経路

        {{SampleSource("samples/LuxelAudio/Program.cs", "audio-tone")}}

        {{StoryRef(ctx, "Examples/Audio/WaveformAndVoice")}}

        `NullAudioBackend`は音を出さず、initialized、voice数、`BuffersQueued`、`IsPlaying`、volume/pitch/panを決定的に観測できます。Windowsの`XAudio2Backend`、Linux/macOSの`Luxel.Audio.Silk.OpenAlAudioBackend`は実deviceへ出力し、browser WASMでは`Luxel.Audio.Browser.BrowserAudioBackend`がWeb AudioへPCM16 clipとqueueを送ります。`Luxel.Audio.Silk`はSilk.NET経由でOpenAL Softを利用するクロスプラットフォームbackendです。LinuxではOpenAL SoftからPipeWire/PulseAudio/ALSAへ出力できます。browserでは作成後もcontextがsuspendedの場合があるため、click/tapから`ResumeAsync()`を呼んでから可聴状態として扱います。

        ## ライフサイクルの原則

        1. backendを作り`Initialize()`する。
        2. backendより短い寿命でmixer/source/streaming voiceを作る。
        3. game loopで必要な`Tick()`、`Update()`、`Pump()`を呼ぶ。
        4. voice所有者を先に、backendを最後に`Dispose()`する。

        ## 失敗の入口

        無音なら、初期化、format、queue、`Play()`、volume/bus、frame駆動の順に確認します。実音だけを最初のoracleにせず、まずheadless状態を検証してください。

        """;

    [Story("Learn/Audio/EnvironmentAndBackends", Order = 1, Toc = true)]
    public static StoryResult Environment(StoryContext ctx) => $$"""
        # Audio environment and backends

        {{AudioCourseCatalog.Meta("Learn/Audio/EnvironmentAndBackends", "Beginner", "Standalone / Framework / CI / Linux / macOS / Browser WASM", "Null / XAudio2 / Silk OpenAL / Web Audio", "Audio overview")}}

        ## 境界と責務

        `IAudioBackend`はdevice/master volume/voice生成を、`IAudioVoice`はbuffer queueとplayback stateを抽象化します。formatごとにvoiceを作り、PCMのdecodeやgame側のbus treeはbackendへ押し込みません。

        ```csharp
        using IAudioBackend backend = new NullAudioBackend();
        backend.Initialize();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono48k);
        voice.SubmitBuffer(pcm);
        voice.Play();
        ```

        ## 実行環境の選択

        | 環境 | backend | 何を確認するか |
        |---|---|---|
        | unit test / CI / deviceなし | `NullAudioBackend` | state、queue、parameter、所有権 |
        | Windows実音 | `Luxel.Audio.Windows.XAudio2Backend` | device、speaker、実時間でのqueue drain |
        | Linux/macOS実音 | `Luxel.Audio.Silk.OpenAlAudioBackend` | OpenAL Soft、voice queue、loopback |
        | Linux CI | 同じSilk backend + Pulse null sink | 実出力captureと波形解析 |
        | Framework native | `LuxelHostBuilder.Create().UseAudio()` | `Luxel.Framework.Game.Native`がWindows=XAudio2、Linux/macOS=OpenALを選択 |
        | Browser WASM | `Luxel.Audio.Browser.BrowserAudioBackend` | Web Audio、autoplay unlock、browser lifecycle |

        portableな`Luxel.Framework.Game`では`UseAudio(factory)`でbackendを注入します。desktopでは`Luxel.Framework.Game.Native`を参照するとparameterlessな`UseAudio()`を利用できます。Windows実音の統合確認は [RealWindow/Audio/Tone](story:RealWindow/Audio/Tone)、browser実音は`samples/LuxelAudioBrowser`を使います。

        ## Web Audioの非同期ライフサイクル

        ```csharp
        using BrowserAudioBackend backend = await BrowserAudioBackend.CreateAsync();
        // click / tap のevent handlerから呼ぶ
        await backend.ResumeAsync();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono48k);
        ```

        {{StoryRef(ctx, "Examples/Audio/BackendLifecycle")}}

        `AudioContext.resume()`は非同期かつuser gesture/autoplay policyの影響を受けます。`CreateAsync()`はcontextを作成しますが、`BrowserAudioState.Running`になるまで可聴準備完了とはみなしません。`AudioBufferSourceNode`は一度だけstartできるため、backendはpause/resume時にnodeを再生成してoffsetを復元します。完了queueは`onended`で論理管理し、Web Audioのequal-power pan lawはXAudio2のlinear matrixと完全一致しません。

        現行browser backendは`AudioBufferSourceNode`によるclipと基本queueを実装しています。AudioWorkletによる長時間・低遅延streamingは未実装です。AudioWorklet自体はsecure contextを必要とし、将来SharedArrayBuffer高速経路を選ぶ場合は追加でcross-origin isolationが必要です。

        ## 所有権と失敗

        backendより先にvoiceを破棄します。`Initialize()`忘れ、別formatのPCM投入、backend dispose後のvoice利用、browserで`ResumeAsync()`前から音が出ると仮定することが典型的な失敗です。OpenAL runtimeが見つからない場合はOSに対応するOpenAL Soft libraryを確認します。Linux CIでは`LUXEL_DESKTOP_AUDIO=null eng/desktop/audio-start.sh`で48 kHz stereoの仮想sinkを用意できます。

        """;

    [Story("Learn/Audio/FormatsClipsAndLoading", Order = 2, Toc = true)]
    public static StoryResult Formats(StoryContext ctx) => $$"""
        # Formats, clips, and loading

        {{AudioCourseCatalog.Meta("Learn/Audio/FormatsClipsAndLoading", "Beginner", "Standalone / Resources / Browser WASM", "Backend neutral PCM16 / Web Audio", "Environment and backends")}}

        ## sample、frame、byte

        `AudioFormat(48_000, 2, 16)`では、1 channelの値がsample、同時刻の左右2 sampleがframeです。1 sampleは2 bytes、1 frameは4 bytes、1秒は192,000 bytesです。`AudioFormat.BytesPerSample`は名前に反して全channelを含む1 frame分で、`AudioClip.SampleCount`もper-channel frame数です。

        {{SampleSource("samples/LuxelAudio/AudioConceptSamples.cs", "audio-format-clip")}}

        {{StoryRef(ctx, "Examples/Audio/WaveformAndVoice")}}

        ## clipを作る3経路

        1. procedural PCMを作り`new AudioClip(format, bytes, name)`する。
        2. `AudioClipLoader.Load(stream, ".wav" | ".ogg")`で16-bit PCMへ全展開する。
        3. Resourcesへ`AudioClipStep`を登録し、`resources.Load<AudioClip>(...)`でtyped loadする。

        ```csharp
        AudioClip clip = AudioClipLoader.Load(fileBytes, ".wav", "hit");
        // AudioClipStep.RunAsync also delegates to AudioClipLoader using uri.Extension.
        ```

        `AudioClipLoader`はWAV/OGGを扱いますが、`WavStream`は長尺向けの別経路で、RIFF PCM16またはfloat32を逐次decodeします。clipはbytesを所有し、loaderへ渡したstreamの寿命とは切り離されます。

        ## 形式不一致と失敗

        PCM bytesだけではsample rate/channels/bit depthを復元できません。channelsを間違えると速度や左右が崩れ、frame境界でないbyte数は不正です。core backendの共通formatはPCM16です。未知拡張子、壊れたRIFF、未対応bit depthは例外として扱い、黙って再生しない設計にします。
        """;

    [Story("Learn/Audio/VoicesAndMixer", Order = 3, Toc = true)]
    public static StoryResult Voices(StoryContext ctx) => $$"""
        # Voices and the one-shot mixer

        {{AudioCourseCatalog.Meta("Learn/Audio/VoicesAndMixer", "Beginner", "Game loop / Headless / Browser WASM", "Null / XAudio2 / Silk OpenAL / Web Audio", "Formats, clips, and loading")}}

        ## voiceの状態モデル

        `SubmitBuffer()`はqueueを増やし、`Play()`は再生を開始、`Pause()`はqueueを保持したまま停止、`Stop()`は停止してqueueを捨てます。`BuffersQueued == 0`はone-shot完了とpool返却の判定です。`IsPlaying`の枯渇時挙動はbackend依存なので、完了判定はqueueを中心に組みます。

        {{SampleSource("samples/LuxelAudio/AudioConceptSamples.cs", "audio-mixer-voice")}}

        {{StoryRef(ctx, "Examples/Audio/WaveformAndVoice")}}

        ## AudioMixerのpool

        `PlayOneShot`は同じ`AudioFormat`のidle voiceを借り、volume、pitch、panを設定してsubmit/playします。実backendが再生を終えて`BuffersQueued`を0にした後、毎frameの`AudioMixer.Tick()`がvoiceを停止してformat別poolへ返します。

        | parameter | 意味 | 注意 |
        |---|---|---|
        | volume | linear gain、通常0..1 | busの`EffectiveVolume`も乗算される |
        | pitch | playback speed ratio | 1が原音、backend範囲に依存 |
        | pan | -1 left .. +1 right | backendのpan lawは完全一致しない |

        ## 所有権と失敗

        mixerが借りたvoiceを所有するため、呼び出し側はone-shot voiceをdisposeしません。一方mixer自体はbackendより先にdisposeします。`Tick()`忘れはpoolへ戻らない原因です。`NullAudioBackend`は時間経過で自動drainしないため、testでは`Stop()`等で完了状態を決定的に作ります。
        """;

    [Story("Learn/Audio/ClipsSourcesAndBuses", Order = 4, Toc = true)]
    public static StoryResult Sources(StoryContext ctx) => $$"""
        # Clips, sources, and buses

        {{AudioCourseCatalog.Meta("Learn/Audio/ClipsSourcesAndBuses", "Beginner", "Game loop / Browser WASM", "Backend neutral source + Web Audio voice", "Voices and mixer")}}

        ## AudioSourceを使うとき

        `AudioSource`は1つのclipとvoiceを所有し、loop、pause、stop、継続的なvolume/pitch/pan更新を扱います。`Signal<float>`を書き換えただけではvoiceへ反映されず、毎frame `Tick()`が必要です。

        {{SampleSource("samples/LuxelAudio/AudioConceptSamples.cs", "audio-source-bus")}}

        ## bus tree

        `AudioBus`はOSのsubmix voiceではなくC#側の軽量な階層です。Master=0.8、Music=0.5ならMusicの`EffectiveVolume`は0.4です。source volume=0.5ならvoiceへ渡る値は0.2です。Master/Music/SFX/Voiceを兄弟として作ると設定画面とgame logicを分離できます。

        {{StoryRef(ctx, "Examples/Audio/Buses")}}

        ```csharp
        while (running)
        {
            music.Tick();       // Signal × bus -> IAudioVoice
            mixer.Tick();       // completed one-shots -> pool
            RunGameFrame();
        }
        ```

        ## lifecycle

        `Play(loop: true)`は最初の呼び出しでsubmitします。`Pause()`後の`Play()`は同じqueueを再開し、`Stop()`はqueueを捨てて次回Playで再submitします。sourceがvoiceを所有するので、sourceをbackendより先に`Dispose()`します。busはmanagedな値objectでdispose不要です。

        ## よくある失敗

        `Tick()`忘れ、parent busが0、sourceとone-shotの所有権混同、dispose後の再生が代表例です。bus値だけでなく最終voice volumeをheadlessで確認します。
        """;

    [Story("Learn/Audio/SpatialAudio", Order = 5, Toc = true)]
    public static StoryResult Spatial(StoryContext ctx) => $$"""
        # Spatial audio

        {{AudioCourseCatalog.Meta("Learn/Audio/SpatialAudio", "Intermediate", "Game loop / Headless / Browser WASM", "C# attenuation + backend pan", "Clips, sources, and buses")}}

        ## メンタルモデル

        `AudioListener`はposition/forward/upを持ち、`Right = Normalize(Forward × Up)`を導出します。`AudioSource3D.Update(listener)`は距離からlinear attenuation、正規化した方向とRightの内積からpanを計算し、busとsource volumeを掛けてvoiceへ転送します。

        {{SampleSource("samples/LuxelAudio/AudioConceptSamples.cs", "audio-spatial")}}

        距離が`MinDistance`以下なら1、`MaxDistance`以上なら0、その間は線形です。例では距離5、範囲1..9なのでattenuationは0.5、listener右側なのでpanは+1です。

        {{StoryRef(ctx, "Examples/Audio/SpatialAttenuation")}}

        ## frame更新順

        1. camera/playerからlistener poseを更新する。
        2. scene transformからsource `Position`を更新する。
        3. `source.Update(listener)`を呼ぶ。
        4. backendが同じframeのvolume/panを使う。

        ## 現在の範囲

        これはHRTF、Doppler、occlusion、room reverbではありません。C#で距離gainとstereo panを計算するportableな基礎です。mono clipでもvoice panを設定できますが、実際のspeaker mappingとpan lawはbackendに依存します。

        ## 失敗

        forward/upが平行だとRightを正規化できません。`MinDistance >= MaxDistance`はstep状の減衰になります。listener更新より前にsourceを更新すると1 frame古い結果になります。
        """;

    [Story("Learn/Audio/Streaming", Order = 6, Toc = true)]
    public static StoryResult Streaming(StoryContext ctx) => $$"""
        # Streaming audio

        {{AudioCourseCatalog.Meta("Learn/Audio/Streaming", "Intermediate", "Game loop / Headless / Browser WASM", "Null / XAudio2 / Silk OpenAL / Web Audio queue", "Spatial audio")}}

        ## clipかstreamか

        短いSFXや頻繁に再利用する音は`AudioClip`へ全展開します。長尺WAVは`IAudioStream`でfloat sampleを逐次供給し、`StreamingVoice`がPCM16へ量子化してvoice queueへ補充します。`WavStream`はseek可能なRIFF PCM16/float32、`LoopingStream`は終端で`Reset()`するdecoratorです。

        {{SampleSource("samples/LuxelAudio/AudioConceptSamples.cs", "audio-streaming")}}

        ## queueとPump

        constructorの`chunkSeconds`は1 bufferの長さ、`QueueDepth`は先読み数です。毎frame `Pump()`は`BuffersQueued < QueueDepth`の間だけdecode/submitし、最初のbufferで`Play()`します。既定は約100ms × 3です。

        ```csharp
        using var stream = WavStream.Open("music.wav");
        using var playback = new StreamingVoice(backend, stream);
        while (running) playback.Pump();
        ```

        {{StoryRef(ctx, "Examples/Audio/StreamingQueue")}}

        `Pump()`が遅いとunderrunし、queueが空になって無音になります。queueを深くするとjitter耐性と引き換えにlatency/memoryが増えます。`Finished`はstream終端に達し、かつbackend queueが0になったときだけtrueです。

        ## 終了、restart、loop、dispose

        `Stop()`はvoice queueを捨てて終了扱いにします。`Restart()`はvoiceをstopしstreamをresetして、次の`Pump()`から再開します。loopは`new LoopingStream(inner)`を渡します。`StreamingVoice.Dispose()`がvoiceとstreamを所有・破棄するので、同じstreamを別所有者から二重disposeしないでください。
        """;

    [Story("Learn/Audio/SpatialStreamingAndTesting", Order = 7, Toc = true)]
    public static StoryResult Testing(StoryContext ctx) => $$"""
        # Audio testing and troubleshooting

        {{AudioCourseCatalog.Meta("Learn/Audio/SpatialStreamingAndTesting", "Intermediate", "CI / Headless / Windows / Linux / macOS / Browser WASM", "Null / XAudio2 / Silk OpenAL / Web Audio", "Streaming audio")}}

        このページは既存Story IDを保ちながら、spatial/streamingを含むaudio全体のtesting入口にします。

        ## headlessで観測する契約

        {{SampleSource("samples/LuxelAudio/AudioConceptSamples.cs", "audio-headless-test")}}

        `NullAudioBackend`では、`Initialized`、`Voices.Count`、`BuffersQueued`、`IsPlaying`、投入したPCM byte数、volume/pitch/panを確認します。privateなNull voiceのsubmitted byte counterへ依存せず、生成したPCM lengthとqueue数をtest oracleにできます。busは`EffectiveVolume`、spatialは`EffectiveVolume`/`EffectivePan`、streamはqueue depthと`Finished`を直接検証します。

        ## 症状別チェックリスト

        | 症状 | 最初に確認すること |
        |---|---|
        | voiceがない | backend `Initialize()`と生成経路 |
        | queuedだが無音 | `Play()`、master/bus/source volume、実device |
        | one-shotが増え続ける | `AudioMixer.Tick()`とbackend queue drain |
        | streamが途切れる | 毎frame `Pump()`、chunk、`QueueDepth`、underrun |
        | streamが終わらない | `LoopingStream`の有無、backendの`BuffersQueued` |
        | spatial値が古い | listener → source position → `Update()`の順序 |
        | shutdownで例外 | source/mixer/streamを先、backendを最後に`Dispose()` |

        ## backend差を分けてtestする

        Null testは時間やspeakerに依存しないlogic contractです。XAudio2 integration testはWindows device、実時間のqueue完了、可聴結果を別層で確認します。Silk OpenAL backendは全OS共通のfake contractを持ち、OpenAL Soft runtimeのある環境では`ALC_SOFT_loopback`の決定的な440 Hz/RMS/pitch/pan testを実行できます。Linux CIではさらにPulseAudio null sinkのWAV captureを検証します。Web Audioはmanaged fake interopでlifecycle、PCM validation、ownershipを、mock `AudioContext`を使うJavaScript contract testで連続schedule、`onended` queue accounting、pause/pitch時のnode再生成を検証します。autoplay unlockと実音はbrowser sampleで手動確認します。AudioWorklet/SABは現行backendのtest対象ではありません。

        実音の最後の確認は [RealWindow/Audio/Tone](story:RealWindow/Audio/Tone) です。
        """;
}
