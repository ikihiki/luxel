using Luxel.Audio;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Luxel.Audio.Windows;

/// <summary>
/// Vortice.XAudio2 経由の XAudio2 バックエンド。
/// <see cref="IXAudio2"/> + <see cref="IXAudio2MasteringVoice"/> をプロセスで 1 個持ち、
/// <see cref="CreateVoice"/> ごとに <see cref="IXAudio2SourceVoice"/> を確保する。
/// </summary>
public sealed class XAudio2Backend : IAudioBackend
{
    private IXAudio2? _xa2;
    private IXAudio2MasteringVoice? _master;
    private readonly List<XAudio2Voice> _voices = new();
    private bool _disposed;

    public float MasterVolume
    {
        get => _master?.Volume ?? 1f;
        set { if (_master != null) _master.Volume = value; }
    }

    public void Initialize()
    {
        if (_xa2 != null) return;
        _xa2 = XAudio2.XAudio2Create();
        _master = _xa2.CreateMasteringVoice();
    }

    public IAudioVoice CreateVoice(AudioFormat format)
    {
        if (_xa2 == null) throw new InvalidOperationException("XAudio2Backend.Initialize() first");
        var wf = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
        IXAudio2SourceVoice src = _xa2.CreateSourceVoice(wf);
        var voice = new XAudio2Voice(src, format);
        _voices.Add(voice);
        return voice;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var v in _voices) v.Dispose();
        _voices.Clear();
        _master?.DestroyVoice();
        _master?.Dispose();
        _xa2?.Dispose();
    }
}

/// <summary>XAudio2 SourceVoice のラッパ。</summary>
internal sealed class XAudio2Voice : IAudioVoice
{
    private readonly IXAudio2SourceVoice _src;
    private readonly AudioFormat _format;
    private bool _isPlaying;
    private bool _disposed;
    private float _volume = 1f, _pitch = 1f, _pan = 0f;

    public AudioFormat Format => _format;

    public XAudio2Voice(IXAudio2SourceVoice src, AudioFormat format)
    {
        _src = src;
        _format = format;
    }

    public unsafe void SubmitBuffer(ReadOnlyMemory<byte> pcm, bool loop = false)
    {
        // Vortice の AudioBuffer に流し込む。data はコピー (呼び出し側の buffer 保持不要)。
        var array = pcm.ToArray();
        var buffer = new AudioBuffer(array)
        {
            LoopCount = loop ? 0xFFFFFFFFu : 0u,   // XAUDIO2_LOOP_INFINITE
        };
        _src.SubmitSourceBuffer(buffer);
    }

    public void Play()
    {
        _src.Start();
        _isPlaying = true;
    }

    public void Stop()
    {
        _src.Stop();
        _src.FlushSourceBuffers();
        _isPlaying = false;
    }

    public void Pause()
    {
        _src.Stop();
        _isPlaying = false;
    }

    public float Volume
    {
        get => _volume;
        set { _volume = value; _src.Volume = value; }
    }

    public float Pitch
    {
        get => _pitch;
        set { _pitch = Math.Clamp(value, 0.001f, 2.0f); _src.FrequencyRatio = _pitch; }
    }

    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Math.Clamp(value, -1f, 1f);
            // ステレオ voice の場合のみ有効。simple linear pan (中央から±で L/R attenuate)
            if (_format.Channels == 2)
            {
                float left = _pan <= 0 ? 1f : 1f - _pan;
                float right = _pan >= 0 ? 1f : 1f + _pan;
                _src.SetOutputMatrix(null, 2, 2, new[] { left, 0f, 0f, right });
            }
        }
    }

    public bool IsPlaying => _isPlaying;

    public int BuffersQueued => (int)_src.State.BuffersQueued;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _src.DestroyVoice();
        _src.Dispose();
    }
}
