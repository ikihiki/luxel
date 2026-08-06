using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Luxel.Audio;
using Luxel.Audio.Browser;

namespace LuxelAudioBrowser;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static BrowserAudioBackend? _backend;
    private static IAudioVoice? _voice;

    public static async Task Main()
    {
        _backend = await BrowserAudioBackend.CreateAsync();
        SetStatus($"Web Audio created ({_backend.State}). Select Enable Audio to satisfy browser autoplay policy.");
    }

    [JSExport]
    public static async Task<string> EnableAudio()
    {
        BrowserAudioBackend backend = RequireBackend();
        await backend.ResumeAsync();
        return $"Audio enabled ({backend.State}).";
    }

    [JSExport]
    public static string PlayTone(int frequency, float volume, float pan, float pitch, bool loop)
    {
        BrowserAudioBackend backend = RequireBackend();
        if (frequency is < 80 or > 2000) throw new ArgumentOutOfRangeException(nameof(frequency));
        _voice?.Dispose();
        _voice = backend.CreateVoice(AudioFormat.Pcm16Mono44k);
        _voice.Volume = volume;
        _voice.Pan = pan;
        _voice.Pitch = pitch;
        _voice.SubmitBuffer(CreateSine(frequency, 0.5), loop);
        _voice.Play();
        return $"Playing {frequency} Hz; loop={loop}, queued={_voice.BuffersQueued}.";
    }

    [JSExport]
    public static string PauseTone()
    {
        if (_voice is null) return "No tone voice.";
        _voice.Pause();
        return $"Paused; queued={_voice.BuffersQueued}.";
    }

    [JSExport]
    public static string ResumeTone()
    {
        if (_voice is null) return "No tone voice.";
        _voice.Play();
        return $"Resumed; queued={_voice.BuffersQueued}.";
    }

    [JSExport]
    public static string StopTone()
    {
        if (_voice is null) return "No tone voice.";
        _voice.Stop();
        return $"Stopped; queued={_voice.BuffersQueued}.";
    }

    private static BrowserAudioBackend RequireBackend() => _backend ?? throw new InvalidOperationException("Web Audio is not initialized.");

    private static byte[] CreateSine(int frequency, double seconds)
    {
        const int sampleRate = 44100;
        short[] samples = new short[(int)(sampleRate * seconds)];
        for (int i = 0; i < samples.Length; i++)
        {
            double envelope = Math.Min(1, Math.Min(i / 256.0, (samples.Length - i - 1) / 256.0));
            samples[i] = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * short.MaxValue * 0.35 * envelope);
        }
        return MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
    }

    [JSImport("setStatus", "luxel-audio-sample-host")]
    private static partial void SetStatus(string message);
}
