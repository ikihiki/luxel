using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace Luxel.Audio.Linux;

internal sealed unsafe class NativeOpenAlApi : IOpenAlApi
{
    private const int AlcFrequency = 0x1007;
    private const int AlcFormatChannelsSoft = 0x1990;
    private const int AlcFormatTypeSoft = 0x1991;
    private const int AlcMonoSoft = 0x1500;
    private const int AlcStereoSoft = 0x1501;
    private const int AlcShortSoft = 0x1402;
    private const int AlStereoAngles = 0x1030;

    private readonly int? _loopbackSampleRate;
    private readonly int _loopbackChannels;
    private AL? _al;
    private ALContext? _alc;
    private Device* _device;
    private Context* _context;
    private nint _sourcefv;
    private AlcRenderSamplesSoft? _renderSamples;
    private bool _disposed;

    internal NativeOpenAlApi() { }

    internal NativeOpenAlApi(int loopbackSampleRate, int loopbackChannels)
    {
        if (loopbackSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(loopbackSampleRate));
        if (loopbackChannels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(loopbackChannels));
        _loopbackSampleRate = loopbackSampleRate;
        _loopbackChannels = loopbackChannels;
    }

    public bool SupportsStereoAngles { get; private set; }
    public bool SupportsLoopbackRendering => _renderSamples is not null;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context != null) return;

        _alc = ALContext.GetApi();
        _al = AL.GetApi();
        if (_loopbackSampleRate.HasValue)
        {
            var openLoopback = GetAlcDelegate<AlcLoopbackOpenDeviceSoft>("alcLoopbackOpenDeviceSOFT");
            _renderSamples = GetAlcDelegate<AlcRenderSamplesSoft>("alcRenderSamplesSOFT");
            _device = openLoopback(null);
        }
        else
        {
            _device = _alc.OpenDevice(null);
        }

        if (_device == null) throw new InvalidOperationException("OpenAL Soft could not open an audio device.");
        try
        {
            if (_loopbackSampleRate.HasValue)
            {
                int* attributes = stackalloc int[]
                {
                    AlcFrequency, _loopbackSampleRate.Value,
                    AlcFormatChannelsSoft, _loopbackChannels == 1 ? AlcMonoSoft : AlcStereoSoft,
                    AlcFormatTypeSoft, AlcShortSoft,
                    0,
                };
                _context = _alc.CreateContext(_device, attributes);
            }
            else
            {
                _context = _alc.CreateContext(_device, null);
            }

            if (_context == null) throw new InvalidOperationException("OpenAL Soft could not create an audio context.");
            MakeContextCurrent();
            SupportsStereoAngles = _al.IsExtensionPresent("AL_EXT_STEREO_ANGLES");
            if (SupportsStereoAngles) _sourcefv = (nint)_al.GetProcAddress("alSourcefv");
            CheckAlError("initialize OpenAL");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void MakeContextCurrent()
    {
        EnsureInitialized();
        if (!_alc!.MakeContextCurrent(_context))
            throw new InvalidOperationException("OpenAL Soft could not make its context current.");
    }

    public void SetListenerGain(float gain)
    {
        Api.SetListenerProperty(ListenerFloat.Gain, gain);
        CheckAlError("set listener gain");
    }

    public uint CreateSource()
    {
        uint source = Api.GenSource();
        CheckAlError("create source");
        return source;
    }

    public void DeleteSource(uint source)
    {
        Api.DeleteSource(source);
        CheckAlError("delete source");
    }

    public uint CreateBuffer(ReadOnlySpan<byte> pcm, AudioFormat format)
    {
        uint buffer = Api.GenBuffer();
        try
        {
            fixed (byte* data = pcm)
            {
                Api.BufferData(buffer, format.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16,
                    data, pcm.Length, format.SampleRate);
            }
            CheckAlError("upload PCM buffer");
            return buffer;
        }
        catch
        {
            Api.DeleteBuffer(buffer);
            throw;
        }
    }

    public void DeleteBuffer(uint buffer)
    {
        Api.DeleteBuffer(buffer);
        CheckAlError("delete buffer");
    }

    public void QueueBuffer(uint source, uint buffer)
    {
        Api.SourceQueueBuffers(source, [buffer]);
        CheckAlError("queue buffer");
    }

    public uint[] UnqueueProcessedBuffers(uint source)
    {
        Api.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out int processed);
        CheckAlError("query processed buffers");
        if (processed <= 0) return [];
        var buffers = new uint[processed];
        Api.SourceUnqueueBuffers(source, buffers);
        CheckAlError("unqueue processed buffers");
        return buffers;
    }

    public void ClearSourceQueue(uint source)
    {
        Api.SetSourceProperty(source, SourceInteger.Buffer, 0);
        CheckAlError("clear source queue");
    }

    public int GetBuffersQueued(uint source)
    {
        Api.GetSourceProperty(source, GetSourceInteger.BuffersQueued, out int queued);
        CheckAlError("query queued buffers");
        return queued;
    }

    public OpenAlPlaybackState GetSourceState(uint source)
    {
        Api.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
        CheckAlError("query source state");
        return (SourceState)state switch
        {
            SourceState.Playing => OpenAlPlaybackState.Playing,
            SourceState.Paused => OpenAlPlaybackState.Paused,
            SourceState.Stopped => OpenAlPlaybackState.Stopped,
            _ => OpenAlPlaybackState.Initial,
        };
    }

    public void Play(uint source) { Api.SourcePlay(source); CheckAlError("play source"); }
    public void Pause(uint source) { Api.SourcePause(source); CheckAlError("pause source"); }
    public void Stop(uint source) { Api.SourceStop(source); CheckAlError("stop source"); }
    public void SetLooping(uint source, bool looping) { Api.SetSourceProperty(source, SourceBoolean.Looping, looping); CheckAlError("set looping"); }
    public void SetGain(uint source, float gain) { Api.SetSourceProperty(source, SourceFloat.Gain, gain); CheckAlError("set gain"); }
    public void SetPitch(uint source, float pitch) { Api.SetSourceProperty(source, SourceFloat.Pitch, pitch); CheckAlError("set pitch"); }

    public void SetStereoPan(uint source, float pan)
    {
        if (!SupportsStereoAngles || _sourcefv == 0)
            throw new NotSupportedException("AL_EXT_STEREO_ANGLES is unavailable.");

        const float centerAngle = MathF.PI / 6f;
        float target = pan >= 0f ? -MathF.PI / 2f : MathF.PI / 2f;
        float amount = MathF.Abs(pan);
        float* angles = stackalloc float[2]
        {
            centerAngle + (target - centerAngle) * amount,
            -centerAngle + (target + centerAngle) * amount,
        };
        ((delegate* unmanaged[Cdecl]<uint, int, float*, void>)_sourcefv)(source, AlStereoAngles, angles);
        CheckAlError("set stereo pan");
    }

    public void RenderSamples(Span<short> samples, int frames)
    {
        if (_renderSamples is null) throw new NotSupportedException("This is not an ALC_SOFT_loopback context.");
        if (frames < 0 || samples.Length < frames * _loopbackChannels) throw new ArgumentOutOfRangeException(nameof(frames));
        fixed (short* destination = samples) _renderSamples(_device, destination, frames);
        CheckAlcError("render loopback samples");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_alc is not null)
        {
            if (_context != null)
            {
                _alc.MakeContextCurrent(null);
                _alc.DestroyContext(_context);
                _context = null;
            }
            if (_device != null)
            {
                _alc.CloseDevice(_device);
                _device = null;
            }
        }
        _al?.Dispose();
        _alc?.Dispose();
        _disposed = true;
    }

    private AL Api => _al ?? throw new InvalidOperationException("OpenAL is not initialized.");

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context == null) throw new InvalidOperationException("OpenAL is not initialized.");
    }

    private T GetAlcDelegate<T>(string name) where T : Delegate
    {
        void* address = _alc!.GetProcAddress(null, name);
        if (address == null) throw new NotSupportedException("OpenAL Soft does not expose ALC_SOFT_loopback.");
        return Marshal.GetDelegateForFunctionPointer<T>((nint)address);
    }

    private void CheckAlError(string operation)
    {
        AudioError error = Api.GetError();
        if (error != AudioError.NoError) throw new InvalidOperationException($"OpenAL failed to {operation}: {error}.");
    }

    private void CheckAlcError(string operation)
    {
        ContextError error = _alc!.GetError(_device);
        if (error != ContextError.NoError) throw new InvalidOperationException($"OpenAL failed to {operation}: {error}.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate Device* AlcLoopbackOpenDeviceSoft(byte* deviceName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void AlcRenderSamplesSoft(Device* device, void* buffer, int samples);
}
