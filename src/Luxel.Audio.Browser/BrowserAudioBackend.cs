using System.Text.Json;

namespace Luxel.Audio.Browser;

/// <summary>Observable lifecycle state of a browser Web Audio context.</summary>
public enum BrowserAudioState
{
    Suspended,
    Running,
    Closed,
}

/// <summary>Web Audio backend for browser WebAssembly applications.</summary>
public sealed class BrowserAudioBackend : IAudioBackend
{
    private readonly IBrowserAudioInterop _interop;
    private readonly List<BrowserAudioVoice> _voices = [];
    private float _masterVolume = 1f;
    private bool _disposed;

    private BrowserAudioBackend(IBrowserAudioInterop interop, int handle, BrowserAudioState state)
    {
        _interop = interop;
        Handle = handle;
        State = state;
    }

    internal int Handle { get; }
    internal IBrowserAudioInterop Interop => _interop;

    /// <summary>Current lifecycle state last observed by this backend.</summary>
    public BrowserAudioState State { get; private set; }

    /// <summary>Creates the Web Audio context. User activation is normally required before <see cref="ResumeAsync"/>.</summary>
    public static async Task<BrowserAudioBackend> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("BrowserAudioBackend requires a browser WebAssembly runtime with Web Audio.");
        return await CreateAsync(new BrowserAudioInterop(), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<BrowserAudioBackend> CreateAsync(IBrowserAudioInterop interop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interop);
        string json = await interop.InitializeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        int handle = root.GetProperty("handle").GetInt32();
        if (handle <= 0) throw new InvalidOperationException("Web Audio initialization returned an invalid backend handle.");
        string state = root.TryGetProperty("state", out JsonElement value) ? value.GetString() ?? "suspended" : "suspended";
        return new BrowserAudioBackend(interop, handle, ParseState(state));
    }

    /// <summary>The asynchronous factory performs initialization; this method validates that the backend is usable.</summary>
    public void Initialize() => ThrowIfDisposed();

    /// <summary>Resumes audio. Call from a browser user-activation handler.</summary>
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _interop.ResumeAsync(Handle).WaitAsync(cancellationToken).ConfigureAwait(false);
        State = BrowserAudioState.Running;
    }

    /// <summary>Suspends the AudioContext without discarding voice queues.</summary>
    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _interop.SuspendAsync(Handle).WaitAsync(cancellationToken).ConfigureAwait(false);
        State = BrowserAudioState.Suspended;
    }

    public IAudioVoice CreateVoice(AudioFormat format)
    {
        ThrowIfDisposed();
        ValidateFormat(format);
        int handle = _interop.CreateVoice(Handle, format.SampleRate, format.Channels, format.BitsPerSample);
        if (handle <= 0) throw new InvalidOperationException("Web Audio returned an invalid voice handle.");
        var voice = new BrowserAudioVoice(this, handle, format);
        _voices.Add(voice);
        return voice;
    }

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            ThrowIfDisposed();
            _masterVolume = ValidateUnit(value, nameof(value));
            _interop.SetMasterVolume(Handle, _masterVolume);
        }
    }

    internal void Retire(BrowserAudioVoice voice)
    {
        _voices.Remove(voice);
        if (!_disposed) _interop.DisposeVoice(voice.Handle);
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        foreach (BrowserAudioVoice voice in _voices.ToArray()) voice.Dispose();
        _voices.Clear();
        _disposed = true;
        _interop.DisposeBackend(Handle);
        State = BrowserAudioState.Closed;
    }

    internal static float ValidateUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f) throw new ArgumentOutOfRangeException(name);
        return value;
    }

    private static void ValidateFormat(AudioFormat format)
    {
        if (format.SampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(format), "Sample rate must be positive.");
        if (format.Channels is < 1 or > 2) throw new NotSupportedException("Browser Web Audio supports mono or stereo voices.");
        if (format.BitsPerSample != 16) throw new NotSupportedException("Browser Web Audio currently supports signed 16-bit PCM only.");
    }

    private static BrowserAudioState ParseState(string state) => state switch
    {
        "running" => BrowserAudioState.Running,
        "closed" => BrowserAudioState.Closed,
        _ => BrowserAudioState.Suspended,
    };
}

internal sealed class BrowserAudioVoice : IAudioVoice
{
    private readonly BrowserAudioBackend _owner;
    private bool _disposed;
    private float _volume = 1f;
    private float _pitch = 1f;
    private float _pan;

    internal BrowserAudioVoice(BrowserAudioBackend owner, int handle, AudioFormat format)
    {
        _owner = owner;
        Handle = handle;
        Format = format;
    }

    internal int Handle { get; }
    public AudioFormat Format { get; }

    public void SubmitBuffer(ReadOnlyMemory<byte> pcm, bool loop = false)
    {
        ThrowIfDisposed();
        if (pcm.IsEmpty) throw new ArgumentException("PCM data cannot be empty.", nameof(pcm));
        if (pcm.Length % Format.BytesPerSample != 0) throw new ArgumentException("PCM data must contain complete sample frames.", nameof(pcm));
        _owner.Interop.SubmitBuffer(Handle, Convert.ToBase64String(pcm.Span), loop);
    }

    public void Play() { ThrowIfDisposed(); _owner.Interop.Play(Handle); }
    public void Stop() { ThrowIfDisposed(); _owner.Interop.Stop(Handle); }
    public void Pause() { ThrowIfDisposed(); _owner.Interop.Pause(Handle); }

    public float Volume
    {
        get => _volume;
        set { ThrowIfDisposed(); _volume = BrowserAudioBackend.ValidateUnit(value, nameof(value)); _owner.Interop.SetVolume(Handle, _volume); }
    }

    public float Pitch
    {
        get => _pitch;
        set
        {
            ThrowIfDisposed();
            if (!float.IsFinite(value) || value < 0.5f || value > 2f) throw new ArgumentOutOfRangeException(nameof(value));
            _pitch = value;
            _owner.Interop.SetPitch(Handle, value);
        }
    }

    public float Pan
    {
        get => _pan;
        set
        {
            ThrowIfDisposed();
            if (!float.IsFinite(value) || value < -1f || value > 1f) throw new ArgumentOutOfRangeException(nameof(value));
            _pan = value;
            _owner.Interop.SetPan(Handle, value);
        }
    }

    public bool IsPlaying { get { ThrowIfDisposed(); return _owner.Interop.IsPlaying(Handle) != 0; } }
    public int BuffersQueued { get { ThrowIfDisposed(); return _owner.Interop.BuffersQueued(Handle); } }

    private void ThrowIfDisposed()
    {
        _owner.ThrowIfDisposed();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Retire(this);
    }
}
