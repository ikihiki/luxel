using System.Numerics;
using Luxel.Audio;
using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Audio.Gallery;

/// <summary>Browser-runnable audio examples embedded at the point where each concept is introduced.</summary>
public static class AudioStories
{
    private static IAudioBackend? _hostBackend;
    private static Func<Task>? _resumeHost;
    private static Func<Task>? _suspendHost;
    private static Func<string>? _stateHost;
    private static readonly Lazy<NullAudioBackend> Fallback = new(() =>
    {
        var backend = new NullAudioBackend();
        backend.Initialize();
        return backend;
    });

    /// <summary>Connects the browser Gallery runtime to Web Audio before a story is built.</summary>
    public static void ConfigureRuntime(IAudioBackend backend, Func<Task> resume, Func<Task> suspend, Func<string> state)
    {
        _hostBackend = backend ?? throw new ArgumentNullException(nameof(backend));
        _resumeHost = resume ?? throw new ArgumentNullException(nameof(resume));
        _suspendHost = suspend ?? throw new ArgumentNullException(nameof(suspend));
        _stateHost = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Removes the current host connection when the browser runtime shuts down.</summary>
    public static void ResetRuntime()
    {
        _hostBackend = null;
        _resumeHost = null;
        _suspendHost = null;
        _stateHost = null;
    }

    public static StoryResult BackendLifecycle()
    {
        var status = new Signal<string>($"現在の状態: {State}");

        async void Resume()
        {
            try
            {
                await ResumeAsync();
                status.Value = $"ResumeAsync完了: {State}。このclickがautoplay unlockのuser gestureです。";
            }
            catch (Exception error) { status.Value = $"resume失敗: {error.Message}"; }
        }

        async void Suspend()
        {
            try
            {
                await SuspendAsync();
                status.Value = $"SuspendAsync完了: {State}。voice queueは保持されます。";
            }
            catch (Exception error) { status.Value = $"suspend失敗: {error.Message}"; }
        }

        return ExampleFrame("Web Audio lifecycle",
            "AudioContextは作成直後にsuspendedの場合があります。ボタン操作から明示的にresumeし、ページを離れる前にはsuspendできます。",
            HStack(10)[Button(_ => Resume(), "Audioを有効化"), Button(_ => Suspend(), "Audioを一時停止", variant: Variant.Outline)],
            Text((Func<string>)(() => status.Value), 14, wrap: TextWrap.Word, width: 620));
    }

    public static StoryResult WaveformAndVoice()
    {
        AudioFormat format = AudioFormat.Pcm16Mono44k;
        byte[] pcm = SinePcm16(format, 440f, 0.45f);
        var clip = new AudioClip(format, pcm, "440 Hz tone");
        var status = new Signal<string>(DescribeClip(clip));
        IAudioVoice? voice = null;

        async void Play()
        {
            try
            {
                await ResumeAsync();
                voice?.Dispose();
                voice = Backend.CreateVoice(format);
                voice.Volume = 0.35f;
                voice.SubmitBuffer(clip.PcmData);
                voice.Play();
                status.Value = $"再生中: 440 Hz / queued={voice.BuffersQueued} / playing={voice.IsPlaying}";
            }
            catch (Exception error) { status.Value = $"再生できません: {error.Message}"; }
        }

        void Stop()
        {
            voice?.Stop();
            status.Value = "停止しました。queueは破棄されます。";
        }

        return ExampleFrame("PCM clip → voice",
            "生成したPCM16 clipをWeb Audio voiceへsubmitします。Enable/Playはブラウザのuser gestureとしてAudioContextを再開します。",
            HStack(10)[Button(_ => Play(), "440 Hzを再生"), Button(_ => Stop(), "停止", variant: Variant.Outline)],
            Text((Func<string>)(() => status.Value), 14, wrap: TextWrap.Word, width: 620),
            Text(DescribeClip(clip), 13, color: Bind.From(() => UiTheme.T.TextMuted)));
    }

    public static StoryResult Buses()
    {
        var master = new AudioBus("Master");
        var music = new AudioBus("Music", master);
        AudioSource? source = null;
        var status = new Signal<string>("Master 100% × Music 60% × Source 50% = voice 30%");
        master.Volume.Value = 1f;
        music.Volume.Value = 0.6f;

        async void Play()
        {
            try
            {
                await ResumeAsync();
                source?.Dispose();
                var clip = new AudioClip(AudioFormat.Pcm16Mono44k,
                    SinePcm16(AudioFormat.Pcm16Mono44k, 330f, 0.6f), "bus tone");
                source = new AudioSource(Backend, clip) { Bus = music };
                source.Volume.Value = 0.5f;
                source.Play(loop: true);
                source.Tick();
                UpdateStatus();
            }
            catch (Exception error) { status.Value = $"再生できません: {error.Message}"; }
        }

        void SetMaster(float value) { master.Volume.Value = value; source?.Tick(); UpdateStatus(); }
        void SetMusic(float value) { music.Volume.Value = value; source?.Tick(); UpdateStatus(); }
        void UpdateStatus() => status.Value =
            $"Master {master.Volume.Value:P0} × Music {music.Volume.Value:P0} × Source 50% = voice {music.EffectiveVolume * 0.5f:P0}";

        return ExampleFrame("AudioSourceとbus tree",
            "同じloop音を鳴らしたまま親子busのgainを変更し、EffectiveVolumeの乗算を耳と数値で確認します。",
            HStack(8)[Button(_ => Play(), "loopを再生"), Button(_ => SetMaster(1f), "Master 100%"), Button(_ => SetMaster(0.35f), "Master 35%")],
            HStack(8)[Button(_ => SetMusic(0.6f), "Music 60%"), Button(_ => SetMusic(0.15f), "Music 15%"), Button(_ => { source?.Stop(); status.Value = "停止しました。"; }, "停止", variant: Variant.Outline)],
            Text((Func<string>)(() => status.Value), 14, wrap: TextWrap.Word, width: 620));
    }

    public static StoryResult SpatialAttenuation()
    {
        var listener = new AudioListener { Position = Vector3.Zero };
        AudioSource3D? source = null;
        var status = new Signal<string>("位置を選ぶと、距離減衰とpanを計算して再生します。");

        async void PlayAt(float x, float distance)
        {
            try
            {
                await ResumeAsync();
                source?.Dispose();
                var clip = new AudioClip(AudioFormat.Pcm16Mono44k,
                    SinePcm16(AudioFormat.Pcm16Mono44k, 550f, 0.55f), "spatial tone");
                source = new AudioSource3D(Backend, clip)
                {
                    Position = new Vector3(x * distance, 0, 0),
                    MinDistance = 1f,
                    MaxDistance = 9f,
                };
                source.Play();
                source.Update(listener);
                status.Value = $"position=({source.Position.X:0.0}, 0, 0) / gain={source.EffectiveVolume:0.00} / pan={source.EffectivePan:+0.00;-0.00;0.00}";
            }
            catch (Exception error) { status.Value = $"再生できません: {error.Message}"; }
        }

        return ExampleFrame("Listenerと3D source",
            "listenerを原点に固定し、sourceの位置だけを変えます。左右はpan、遠近はlinear attenuationへ反映されます。",
            HStack(8)[Button(_ => PlayAt(-1, 3), "左・近い"), Button(_ => PlayAt(0, 3), "中央・近い"), Button(_ => PlayAt(1, 3), "右・近い")],
            HStack(8)[Button(_ => PlayAt(-1, 7), "左・遠い"), Button(_ => PlayAt(1, 7), "右・遠い"), Button(_ => { source?.Stop(); status.Value = "停止しました。"; }, "停止", variant: Variant.Outline)],
            Text((Func<string>)(() => status.Value), 14, wrap: TextWrap.Word, width: 620));
    }

    public static StoryResult StreamingQueue()
    {
        IAudioVoice? voice = null;
        var status = new Signal<string>("3個のPCM chunkをqueueし、Web Audioが切れ目なくscheduleします。");

        async void PlayQueue()
        {
            try
            {
                await ResumeAsync();
                voice?.Dispose();
                voice = Backend.CreateVoice(AudioFormat.Pcm16Mono44k);
                foreach (float frequency in new[] { 330f, 440f, 660f })
                    voice.SubmitBuffer(SinePcm16(AudioFormat.Pcm16Mono44k, frequency, 0.22f));
                voice.Play();
                status.Value = $"330 → 440 → 660 Hz / queued={voice.BuffersQueued} / playing={voice.IsPlaying}";
            }
            catch (Exception error) { status.Value = $"再生できません: {error.Message}"; }
        }

        return ExampleFrame("queued streaming chunks",
            "StreamingVoiceと同じbuffer queue契約を、周波数の異なる3 chunkで可視化します。各chunkは前の終了時刻へ連続scheduleされます。",
            HStack(10)[Button(_ => PlayQueue(), "3 chunkを再生"), Button(_ => { voice?.Stop(); status.Value = "停止してqueueを破棄しました。"; }, "停止", variant: Variant.Outline)],
            Text((Func<string>)(() => status.Value), 14, wrap: TextWrap.Word, width: 620));
    }

    private static IAudioBackend Backend => _hostBackend ?? Fallback.Value;
    private static Task ResumeAsync() => _resumeHost?.Invoke() ?? Task.CompletedTask;
    private static Task SuspendAsync() => _suspendHost?.Invoke() ?? Task.CompletedTask;
    private static string State => _stateHost?.Invoke() ?? "NullAudioBackend (headless preview)";

    private static Widget ExampleFrame(string title, string description, params Widget[] content)
    {
        Widget[] children =
        [
            Heading(title, 2),
            Text(description, 14, color: Bind.From(() => UiTheme.T.TextMuted), wrap: TextWrap.Word, width: 630),
            .. content,
        ];
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[VStack(12, width: 650)[children]]];
    }

    private static string DescribeClip(AudioClip clip)
        => $"format={clip.Format.SampleRate} Hz / {clip.Format.Channels} ch / PCM{clip.Format.BitsPerSample}, frames={clip.SampleCount}, duration={clip.Duration.TotalMilliseconds:0} ms, bytes={clip.PcmData.Length}";

    private static byte[] SinePcm16(AudioFormat format, float frequency, float seconds)
    {
        int frames = Math.Max(1, (int)(format.SampleRate * seconds));
        byte[] pcm = new byte[frames * format.BytesPerSample];
        for (int frame = 0; frame < frames; frame++)
        {
            double edge = Math.Min(frame / 256.0, (frames - frame - 1) / 256.0);
            double envelope = Math.Clamp(edge, 0, 1);
            short sample = (short)(Math.Sin(2 * Math.PI * frequency * frame / format.SampleRate) * short.MaxValue * 0.28 * envelope);
            for (int channel = 0; channel < format.Channels; channel++)
                BitConverter.TryWriteBytes(pcm.AsSpan((frame * format.Channels + channel) * 2, 2), sample);
        }
        return pcm;
    }
}
