namespace Luxel.Audio.Silk.Tests;

public sealed class OpenAlManagedTests
{
    [Fact]
    public void InitializeIsIdempotentAndMasterVolumeIsValidated()
    {
        var api = new FakeOpenAlApi();
        using var backend = new OpenAlAudioBackend(api);

        backend.Initialize();
        backend.Initialize();
        backend.MasterVolume = 0.25f;

        Assert.Equal(1, api.InitializeCount);
        Assert.Equal(0.25f, backend.MasterVolume);
        Assert.Equal(0.25f, api.ListenerGain);
        Assert.Throws<ArgumentOutOfRangeException>(() => backend.MasterVolume = 1.01f);
    }

    [Fact]
    public void VoiceValidatesFormatPcmAndControls()
    {
        using var backend = CreateBackend(new FakeOpenAlApi());
        Assert.Throws<ArgumentOutOfRangeException>(() => backend.CreateVoice(new AudioFormat(0, 1, 16)));
        Assert.Throws<NotSupportedException>(() => backend.CreateVoice(new AudioFormat(44100, 3, 16)));
        Assert.Throws<NotSupportedException>(() => backend.CreateVoice(new AudioFormat(44100, 1, 8)));

        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Stereo44k);
        Assert.Throws<ArgumentException>(() => voice.SubmitBuffer(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => voice.SubmitBuffer(new byte[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Volume = -0.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Pitch = 2.01f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Pan = -1.01f);

        voice.Volume = 0.4f;
        voice.Pitch = 1.5f;
        voice.Pan = -0.5f;
        Assert.Equal(0.4f, voice.Volume);
        Assert.Equal(1.5f, voice.Pitch);
        Assert.Equal(-0.5f, voice.Pan);
    }

    [Fact]
    public void StereoPanExplicitlyRejectsMissingExtensionButPreservesZeroAndMonoSemantics()
    {
        var api = new FakeOpenAlApi { SupportsStereoAngles = false };
        using var backend = CreateBackend(api);
        using IAudioVoice stereo = backend.CreateVoice(AudioFormat.Pcm16Stereo44k);
        using IAudioVoice mono = backend.CreateVoice(AudioFormat.Pcm16Mono44k);

        stereo.Pan = 0f;
        Assert.Throws<NotSupportedException>(() => stereo.Pan = 0.25f);
        Assert.Equal(0f, stereo.Pan);
        mono.Pan = 0.75f;
        Assert.Equal(0.75f, mono.Pan);
        Assert.Empty(api.StereoPans);
    }

    [Fact]
    public void QueueCollectsProcessedBuffersAndPauseStopHaveExactSemantics()
    {
        var api = new FakeOpenAlApi();
        using var backend = CreateBackend(api);
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono44k);

        voice.SubmitBuffer(new byte[8]);
        voice.SubmitBuffer(new byte[12]);
        Assert.Equal(2, voice.BuffersQueued);
        voice.Play();
        Assert.True(voice.IsPlaying);
        voice.Pause();
        Assert.False(voice.IsPlaying);
        Assert.Equal(2, voice.BuffersQueued);

        api.MarkOneProcessed();
        Assert.Equal(1, voice.BuffersQueued);
        Assert.Single(api.DeletedBuffers);

        voice.Stop();
        Assert.False(voice.IsPlaying);
        Assert.Equal(0, voice.BuffersQueued);
        Assert.Equal(2, api.DeletedBuffers.Count);
    }

    [Fact]
    public void LoopingRequiresExactlyOneBufferAndStopResetsVoiceForStreaming()
    {
        var api = new FakeOpenAlApi();
        using var backend = CreateBackend(api);
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono44k);

        voice.SubmitBuffer(new byte[8], loop: true);
        Assert.Throws<InvalidOperationException>(() => voice.SubmitBuffer(new byte[8]));
        Assert.True(api.Looping);
        voice.Stop();
        Assert.False(api.Looping);

        voice.SubmitBuffer(new byte[8]);
        voice.SubmitBuffer(new byte[8]);
        Assert.Throws<InvalidOperationException>(() => voice.SubmitBuffer(new byte[8], loop: true));
        Assert.Equal(2, voice.BuffersQueued);
    }

    [Fact]
    public void StreamingVoiceQueuesMultipleNonLoopChunks()
    {
        var api = new FakeOpenAlApi();
        using var backend = CreateBackend(api);
        using var streaming = new StreamingVoice(backend, new FiniteStream(), chunkSeconds: 0.001f, queueDepth: 3);

        streaming.Pump();

        Assert.Equal(3, streaming.BuffersQueued);
        Assert.True(streaming.IsPlaying);
        Assert.False(api.Looping);
    }

    [Fact]
    public void NativeCallsAreSerializedAcrossThreads()
    {
        var api = new FakeOpenAlApi { DelayCalls = true };
        using var backend = CreateBackend(api);
        using IAudioVoice first = backend.CreateVoice(AudioFormat.Pcm16Mono44k);
        using IAudioVoice second = backend.CreateVoice(AudioFormat.Pcm16Mono44k);

        Parallel.For(0, 20, i =>
        {
            if ((i & 1) == 0) first.Volume = 0.5f;
            else second.Pitch = 1.25f;
        });

        Assert.Equal(1, api.MaxConcurrentCalls);
    }

    [Fact]
    public void DisposingBackendReleasesOwnedVoicesAndRejectsFurtherUse()
    {
        var api = new FakeOpenAlApi();
        var backend = CreateBackend(api);
        IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono44k);
        voice.SubmitBuffer(new byte[8]);

        backend.Dispose();

        Assert.True(api.Disposed);
        Assert.Single(api.DeletedSources);
        Assert.Throws<ObjectDisposedException>(() => voice.Play());
        Assert.Throws<ObjectDisposedException>(() => backend.CreateVoice(AudioFormat.Pcm16Mono44k));
    }

    private static OpenAlAudioBackend CreateBackend(FakeOpenAlApi api)
    {
        var backend = new OpenAlAudioBackend(api);
        backend.Initialize();
        return backend;
    }

    private sealed class FiniteStream : IAudioStream
    {
        private int _reads;
        public int SampleRate => 48000;
        public int Channels => 1;
        public int Read(Span<float> destination)
        {
            if (_reads++ >= 4) return 0;
            destination[..Math.Min(48, destination.Length)].Fill(0.25f);
            return Math.Min(48, destination.Length);
        }
        public void Reset() => _reads = 0;
        public void Dispose() { }
    }

    private sealed class FakeOpenAlApi : IOpenAlApi
    {
        private readonly object _sync = new();
        private readonly Queue<uint> _queued = new();
        private readonly Queue<uint> _processed = new();
        private uint _next = 1;
        private int _activeCalls;
        public int InitializeCount { get; private set; }
        public float ListenerGain { get; private set; } = 1f;
        public bool SupportsStereoAngles { get; set; } = true;
        public bool SupportsLoopbackRendering => false;
        public bool Looping { get; private set; }
        public bool Disposed { get; private set; }
        public bool DelayCalls { get; set; }
        public int MaxConcurrentCalls { get; private set; }
        public OpenAlPlaybackState State { get; private set; }
        public List<uint> DeletedBuffers { get; } = [];
        public List<uint> DeletedSources { get; } = [];
        public List<float> StereoPans { get; } = [];

        public void Initialize() { Call(() => InitializeCount++); }
        public void MakeContextCurrent() { Call(() => { }); }
        public void SetListenerGain(float gain) { Call(() => ListenerGain = gain); }
        public uint CreateSource() => Call(() => _next++);
        public void DeleteSource(uint source) { Call(() => DeletedSources.Add(source)); }
        public uint CreateBuffer(ReadOnlySpan<byte> pcm, AudioFormat format) => Call(() => _next++);
        public void DeleteBuffer(uint buffer) { Call(() => DeletedBuffers.Add(buffer)); }
        public void QueueBuffer(uint source, uint buffer) { Call(() => _queued.Enqueue(buffer)); }
        public uint[] UnqueueProcessedBuffers(uint source) => Call(() => { var result = _processed.ToArray(); _processed.Clear(); return result; });
        public void ClearSourceQueue(uint source) { Call(() => _queued.Clear()); }
        public int GetBuffersQueued(uint source) => Call(() => _queued.Count);
        public OpenAlPlaybackState GetSourceState(uint source) => Call(() => State);
        public void Play(uint source) { Call(() => State = _queued.Count == 0 ? OpenAlPlaybackState.Stopped : OpenAlPlaybackState.Playing); }
        public void Pause(uint source) { Call(() => State = OpenAlPlaybackState.Paused); }
        public void Stop(uint source) { Call(() => State = OpenAlPlaybackState.Stopped); }
        public void SetLooping(uint source, bool looping) { Call(() => Looping = looping); }
        public void SetGain(uint source, float gain) { Call(() => { }); }
        public void SetPitch(uint source, float pitch) { Call(() => { }); }
        public void SetStereoPan(uint source, float pan) { Call(() => StereoPans.Add(pan)); }
        public void RenderSamples(Span<short> samples, int frames) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;

        public void MarkOneProcessed()
        {
            uint buffer = _queued.Dequeue();
            _processed.Enqueue(buffer);
        }

        private void Call(Action action) => Call(() => { action(); return 0; });
        private T Call<T>(Func<T> action)
        {
            int active = Interlocked.Increment(ref _activeCalls);
            lock (_sync) MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
            try
            {
                if (DelayCalls) Thread.Sleep(2);
                return action();
            }
            finally { Interlocked.Decrement(ref _activeCalls); }
        }
    }
}
