using Luxel.Audio;

namespace Luxel.Audio.Silk;

/// <summary>Cross-platform OpenAL Soft audio backend implemented with Silk.NET.OpenAL.</summary>
public sealed class OpenAlAudioBackend : IAudioBackend
{
    private readonly object _gate = new();
    private readonly IOpenAlApi _api;
    private readonly List<OpenAlAudioVoice> _voices = [];
    private bool _initialized;
    private bool _disposed;
    private float _masterVolume = 1f;

    public OpenAlAudioBackend() : this(new NativeOpenAlApi()) { }

    internal OpenAlAudioBackend(IOpenAlApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public float MasterVolume
    {
        get { lock (_gate) { ThrowIfDisposed(); return _masterVolume; } }
        set
        {
            if (value is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(value));
            Execute(api => { EnsureInitialized(); api.SetListenerGain(value); _masterVolume = value; });
        }
    }

    public void Initialize()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_initialized) return;
            _api.Initialize();
            _api.MakeContextCurrent();
            _api.SetListenerGain(_masterVolume);
            _initialized = true;
        }
    }

    public IAudioVoice CreateVoice(AudioFormat format)
    {
        ValidateFormat(format);
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureInitialized();
            _api.MakeContextCurrent();
            var voice = new OpenAlAudioVoice(this, _api.CreateSource(), format);
            _voices.Add(voice);
            return voice;
        }
    }

    internal T Execute<T>(Func<IOpenAlApi, T> action)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureInitialized();
            _api.MakeContextCurrent();
            return action(_api);
        }
    }

    internal void Execute(Action<IOpenAlApi> action) => Execute(api => { action(api); return 0; });

    internal void ReleaseVoice(OpenAlAudioVoice voice)
    {
        lock (_gate) _voices.Remove(voice);
    }

    internal void RenderSamples(Span<short> samples, int frames)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureInitialized();
            _api.MakeContextCurrent();
            _api.RenderSamples(samples, frames);
        }
    }

    internal bool SupportsLoopbackRendering => Execute(api => api.SupportsLoopbackRendering);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_initialized) _api.MakeContextCurrent();
            foreach (OpenAlAudioVoice voice in _voices.ToArray()) voice.DisposeFromBackend(_api);
            _voices.Clear();
            _api.Dispose();
            _disposed = true;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("OpenAlAudioBackend.Initialize() must be called first.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateFormat(AudioFormat format)
    {
        if (format.SampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(format), "Sample rate must be positive.");
        if (format.Channels is not (1 or 2)) throw new NotSupportedException("OpenAL backend supports mono or stereo PCM only.");
        if (format.BitsPerSample != 16) throw new NotSupportedException("OpenAL backend supports signed 16-bit PCM only.");
    }
}

internal sealed class OpenAlAudioVoice : IAudioVoice
{
    private readonly OpenAlAudioBackend _backend;
    private readonly AudioFormat _format;
    private readonly HashSet<uint> _buffers = [];
    private uint _source;
    private bool _looping;
    private bool _disposed;
    private float _volume = 1f;
    private float _pitch = 1f;
    private float _pan;

    internal OpenAlAudioVoice(OpenAlAudioBackend backend, uint source, AudioFormat format)
    {
        _backend = backend;
        _source = source;
        _format = format;
    }

    public void SubmitBuffer(ReadOnlyMemory<byte> pcm, bool loop = false)
    {
        ThrowIfDisposed();
        if (pcm.IsEmpty) throw new ArgumentException("PCM buffer must not be empty.", nameof(pcm));
        if (pcm.Length % _format.BytesPerSample != 0)
            throw new ArgumentException("PCM byte length must contain complete sample frames.", nameof(pcm));

        _backend.Execute(api =>
        {
            CollectProcessed(api);
            int queued = api.GetBuffersQueued(_source);
            if (loop && queued != 0)
                throw new InvalidOperationException("A looping voice can contain exactly one buffer.");
            if (_looping)
                throw new InvalidOperationException("Additional buffers cannot be queued while a looping buffer is present.");

            uint buffer = api.CreateBuffer(pcm.Span, _format);
            bool loopEnabled = false;
            try
            {
                if (loop)
                {
                    api.SetLooping(_source, true);
                    loopEnabled = true;
                }
                api.QueueBuffer(_source, buffer);
                _buffers.Add(buffer);
                _looping = loop;
            }
            catch
            {
                if (loopEnabled) api.SetLooping(_source, false);
                api.DeleteBuffer(buffer);
                throw;
            }
        });
    }

    public void Play()
    {
        ThrowIfDisposed();
        _backend.Execute(api => { CollectProcessed(api); if (api.GetBuffersQueued(_source) > 0) api.Play(_source); });
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _backend.Execute(StopAndClear);
    }

    public void Pause()
    {
        ThrowIfDisposed();
        _backend.Execute(api => api.Pause(_source));
    }

    public float Volume
    {
        get { ThrowIfDisposed(); return _volume; }
        set
        {
            ThrowIfDisposed();
            if (value is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(value));
            _backend.Execute(api => api.SetGain(_source, value));
            _volume = value;
        }
    }

    public float Pitch
    {
        get { ThrowIfDisposed(); return _pitch; }
        set
        {
            ThrowIfDisposed();
            if (value is < 0.5f or > 2f) throw new ArgumentOutOfRangeException(nameof(value));
            _backend.Execute(api => api.SetPitch(_source, value));
            _pitch = value;
        }
    }

    public float Pan
    {
        get { ThrowIfDisposed(); return _pan; }
        set
        {
            ThrowIfDisposed();
            if (value is < -1f or > 1f) throw new ArgumentOutOfRangeException(nameof(value));
            if (_format.Channels == 2 && value != 0f)
            {
                _backend.Execute(api =>
                {
                    if (!api.SupportsStereoAngles)
                        throw new NotSupportedException("Stereo pan requires the AL_EXT_STEREO_ANGLES extension.");
                    api.SetStereoPan(_source, value);
                });
            }
            else if (_format.Channels == 2)
            {
                _backend.Execute(api => { if (api.SupportsStereoAngles) api.SetStereoPan(_source, 0f); });
            }
            // IAudioVoice defines pan for stereo voices only; mono accepts/stores the value but has no effect.
            _pan = value;
        }
    }

    public bool IsPlaying
    {
        get
        {
            ThrowIfDisposed();
            return _backend.Execute(api => { CollectProcessed(api); return api.GetSourceState(_source) == OpenAlPlaybackState.Playing; });
        }
    }

    public int BuffersQueued
    {
        get
        {
            ThrowIfDisposed();
            return _backend.Execute(api => { CollectProcessed(api); return api.GetBuffersQueued(_source); });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _backend.Execute(DisposeNative);
        _backend.ReleaseVoice(this);
        _disposed = true;
    }

    internal void DisposeFromBackend(IOpenAlApi api)
    {
        if (_disposed) return;
        DisposeNative(api);
        _disposed = true;
    }

    private void DisposeNative(IOpenAlApi api)
    {
        StopAndClear(api);
        api.DeleteSource(_source);
        _source = 0;
    }

    private void StopAndClear(IOpenAlApi api)
    {
        api.Stop(_source);
        api.SetLooping(_source, false);
        api.ClearSourceQueue(_source);
        foreach (uint buffer in _buffers) api.DeleteBuffer(buffer);
        _buffers.Clear();
        _looping = false;
    }

    private void CollectProcessed(IOpenAlApi api)
    {
        foreach (uint buffer in api.UnqueueProcessedBuffers(_source))
        {
            if (_buffers.Remove(buffer)) api.DeleteBuffer(buffer);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
