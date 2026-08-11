using System.Text.Json;

namespace Luxel.Audio.Browser.Tests;

public sealed class BrowserAudioManagedTests
{
    [Fact]
    public async Task LifecycleAndMasterVolumeForwardToInterop()
    {
        var interop = new FakeInterop();
        using var backend = await BrowserAudioBackend.CreateAsync(interop);

        Assert.Equal(BrowserAudioState.Suspended, backend.State);
        backend.Initialize();
        backend.MasterVolume = 0.25f;
        await backend.ResumeAsync();
        Assert.Equal(BrowserAudioState.Running, backend.State);
        await backend.SuspendAsync();

        Assert.Equal(BrowserAudioState.Suspended, backend.State);
        Assert.Equal(0.25f, interop.MasterVolumes[backend.Handle]);
        Assert.Equal(1, interop.ResumeCount);
        Assert.Equal(1, interop.SuspendCount);
    }

    [Fact]
    public async Task VoiceValidatesFormatPcmAndControls()
    {
        using var backend = await BrowserAudioBackend.CreateAsync(new FakeInterop());
        Assert.Throws<ArgumentOutOfRangeException>(() => backend.CreateVoice(new AudioFormat(0, 1, 16)));
        Assert.Throws<NotSupportedException>(() => backend.CreateVoice(new AudioFormat(44100, 3, 16)));
        Assert.Throws<NotSupportedException>(() => backend.CreateVoice(new AudioFormat(44100, 1, 8)));

        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Stereo44k);
        Assert.Throws<ArgumentException>(() => voice.SubmitBuffer(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => voice.SubmitBuffer(new byte[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Volume = 1.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Pitch = 0.49f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Pan = -1.01f);

        voice.Volume = 0.4f;
        voice.Pitch = 1.5f;
        voice.Pan = -0.5f;
        Assert.Equal(0.4f, voice.Volume);
        Assert.Equal(1.5f, voice.Pitch);
        Assert.Equal(-0.5f, voice.Pan);
    }

    [Fact]
    public async Task QueuePauseResumeAndStopPreserveExpectedSemantics()
    {
        var interop = new FakeInterop();
        using var backend = await BrowserAudioBackend.CreateAsync(interop);
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono44k);

        voice.SubmitBuffer(new byte[8]);
        voice.SubmitBuffer(new byte[12], loop: true);
        Assert.Equal(2, voice.BuffersQueued);
        Assert.False(voice.IsPlaying);

        voice.Play();
        Assert.True(voice.IsPlaying);
        voice.Pause();
        Assert.False(voice.IsPlaying);
        Assert.Equal(2, voice.BuffersQueued);
        voice.Play();
        Assert.True(voice.IsPlaying);

        voice.Stop();
        Assert.False(voice.IsPlaying);
        Assert.Equal(0, voice.BuffersQueued);
        Assert.Equal([false, true], interop.Voices.Values.Single().Loops);
    }

    [Fact]
    public async Task AudioMixerReusesVoiceAfterBrowserQueueDrains()
    {
        var interop = new FakeInterop();
        using var backend = await BrowserAudioBackend.CreateAsync(interop);
        using var mixer = new AudioMixer(backend);
        var clip = new AudioClip(AudioFormat.Pcm16Mono22k, new byte[8], "tick");

        mixer.PlayOneShot(clip);
        Assert.Equal(1, mixer.ActiveVoiceCount);
        Assert.Single(interop.Voices);

        interop.Voices.Values.Single().Queued = 0;
        interop.Voices.Values.Single().Playing = false;
        mixer.Tick();
        Assert.Equal(0, mixer.ActiveVoiceCount);

        mixer.PlayOneShot(clip);
        Assert.Equal(1, mixer.ActiveVoiceCount);
        Assert.Single(interop.Voices);
    }

    [Fact]
    public async Task DisposingBackendReleasesOnlyItsOwnedVoices()
    {
        var interop = new FakeInterop();
        var first = await BrowserAudioBackend.CreateAsync(interop);
        using var second = await BrowserAudioBackend.CreateAsync(interop);
        IAudioVoice firstVoice = first.CreateVoice(AudioFormat.Pcm16Mono22k);
        using IAudioVoice secondVoice = second.CreateVoice(AudioFormat.Pcm16Mono22k);
        int firstHandle = interop.Voices.Single(pair => pair.Value.Backend == first.Handle).Key;
        int secondHandle = interop.Voices.Single(pair => pair.Value.Backend == second.Handle).Key;

        first.Dispose();

        Assert.Contains(firstHandle, interop.DisposedVoices);
        Assert.DoesNotContain(secondHandle, interop.DisposedVoices);
        Assert.Throws<ObjectDisposedException>(() => firstVoice.Play());
        secondVoice.SubmitBuffer(new byte[2]);
        Assert.Equal(1, secondVoice.BuffersQueued);
        Assert.Equal(BrowserAudioState.Closed, first.State);
    }

    [Fact]
    public async Task FactoryRejectsInvalidHandleAndDisposedObjectsRejectUse()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => BrowserAudioBackend.CreateAsync(new FakeInterop { InvalidHandle = true }));
        var backend = await BrowserAudioBackend.CreateAsync(new FakeInterop());
        IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono48k);
        voice.Dispose();
        Assert.Throws<ObjectDisposedException>(() => voice.SubmitBuffer(new byte[2]));
        backend.Dispose();
        Assert.Throws<ObjectDisposedException>(() => backend.CreateVoice(AudioFormat.Pcm16Mono48k));
    }

    private sealed class FakeInterop : IBrowserAudioInterop
    {
        private int _next = 10;
        public bool InvalidHandle { get; set; }
        public int ResumeCount { get; private set; }
        public int SuspendCount { get; private set; }
        public Dictionary<int, float> MasterVolumes { get; } = [];
        public Dictionary<int, VoiceState> Voices { get; } = [];
        public HashSet<int> DisposedVoices { get; } = [];

        public Task<string> InitializeAsync() => Task.FromResult(JsonSerializer.Serialize(new { handle = InvalidHandle ? 0 : ++_next, state = "suspended" }));
        public Task<string> ResumeAsync(int backend) { ResumeCount++; return Task.FromResult("running"); }
        public Task<string> SuspendAsync(int backend) { SuspendCount++; return Task.FromResult("suspended"); }
        public void SetMasterVolume(int backend, float volume) => MasterVolumes[backend] = volume;
        public int CreateVoice(int backend, int sampleRate, int channels, int bitsPerSample)
        {
            int handle = ++_next;
            Voices.Add(handle, new VoiceState(backend));
            return handle;
        }
        public void SubmitBuffer(int voice, byte[] pcm, bool loop)
        {
            VoiceState state = Voices[voice];
            state.Queued++;
            state.Loops.Add(loop);
        }
        public void Play(int voice) { if (Voices[voice].Queued > 0) Voices[voice].Playing = true; }
        public void Pause(int voice) => Voices[voice].Playing = false;
        public void Stop(int voice) { Voices[voice].Playing = false; Voices[voice].Queued = 0; }
        public void SetVolume(int voice, float volume) => Voices[voice].Volume = volume;
        public void SetPitch(int voice, float pitch) => Voices[voice].Pitch = pitch;
        public void SetPan(int voice, float pan) => Voices[voice].Pan = pan;
        public int IsPlaying(int voice) => Voices[voice].Playing ? 1 : 0;
        public int BuffersQueued(int voice) => Voices[voice].Queued;
        public void DisposeVoice(int voice) { DisposedVoices.Add(voice); Voices.Remove(voice); }
        public void DisposeBackend(int backend) { }
    }

    private sealed class VoiceState(int backend)
    {
        public int Backend { get; } = backend;
        public bool Playing { get; set; }
        public int Queued { get; set; }
        public float Volume { get; set; } = 1f;
        public float Pitch { get; set; } = 1f;
        public float Pan { get; set; }
        public List<bool> Loops { get; } = [];
    }
}
