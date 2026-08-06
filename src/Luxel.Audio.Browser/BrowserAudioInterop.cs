using System.Runtime.InteropServices.JavaScript;

namespace Luxel.Audio.Browser;

internal interface IBrowserAudioInterop
{
    Task<string> InitializeAsync();
    Task ResumeAsync(int backend);
    Task SuspendAsync(int backend);
    void SetMasterVolume(int backend, float volume);
    int CreateVoice(int backend, int sampleRate, int channels, int bitsPerSample);
    void SubmitBuffer(int voice, string pcmBase64, bool loop);
    void Play(int voice);
    void Pause(int voice);
    void Stop(int voice);
    void SetVolume(int voice, float volume);
    void SetPitch(int voice, float pitch);
    void SetPan(int voice, float pan);
    int IsPlaying(int voice);
    int BuffersQueued(int voice);
    void DisposeVoice(int voice);
    void DisposeBackend(int backend);
}

internal sealed partial class BrowserAudioInterop : IBrowserAudioInterop
{
    private const string Module = "./luxel-audio-browser.js";

    public Task<string> InitializeAsync() => InitializeCoreAsync();
    public Task ResumeAsync(int backend) => ResumeCoreAsync(backend);
    public Task SuspendAsync(int backend) => SuspendCoreAsync(backend);
    public void SetMasterVolume(int backend, float volume) => SetMasterVolumeCore(backend, volume);
    public int CreateVoice(int backend, int sampleRate, int channels, int bitsPerSample) => CreateVoiceCore(backend, sampleRate, channels, bitsPerSample);
    public void SubmitBuffer(int voice, string pcmBase64, bool loop) => SubmitBufferCore(voice, pcmBase64, loop);
    public void Play(int voice) => PlayCore(voice);
    public void Pause(int voice) => PauseCore(voice);
    public void Stop(int voice) => StopCore(voice);
    public void SetVolume(int voice, float volume) => SetVolumeCore(voice, volume);
    public void SetPitch(int voice, float pitch) => SetPitchCore(voice, pitch);
    public void SetPan(int voice, float pan) => SetPanCore(voice, pan);
    public int IsPlaying(int voice) => IsPlayingCore(voice);
    public int BuffersQueued(int voice) => BuffersQueuedCore(voice);
    public void DisposeVoice(int voice) => DisposeVoiceCore(voice);
    public void DisposeBackend(int backend) => DisposeBackendCore(backend);

    [JSImport("initialize", Module)] private static partial Task<string> InitializeCoreAsync();
    [JSImport("resume", Module)] private static partial Task ResumeCoreAsync(int backend);
    [JSImport("suspend", Module)] private static partial Task SuspendCoreAsync(int backend);
    [JSImport("setMasterVolume", Module)] private static partial void SetMasterVolumeCore(int backend, float volume);
    [JSImport("createVoice", Module)] private static partial int CreateVoiceCore(int backend, int sampleRate, int channels, int bitsPerSample);
    [JSImport("submitBuffer", Module)] private static partial void SubmitBufferCore(int voice, string pcmBase64, bool loop);
    [JSImport("play", Module)] private static partial void PlayCore(int voice);
    [JSImport("pause", Module)] private static partial void PauseCore(int voice);
    [JSImport("stop", Module)] private static partial void StopCore(int voice);
    [JSImport("setVolume", Module)] private static partial void SetVolumeCore(int voice, float volume);
    [JSImport("setPitch", Module)] private static partial void SetPitchCore(int voice, float pitch);
    [JSImport("setPan", Module)] private static partial void SetPanCore(int voice, float pan);
    [JSImport("isPlaying", Module)] private static partial int IsPlayingCore(int voice);
    [JSImport("buffersQueued", Module)] private static partial int BuffersQueuedCore(int voice);
    [JSImport("disposeVoice", Module)] private static partial void DisposeVoiceCore(int voice);
    [JSImport("disposeBackend", Module)] private static partial void DisposeBackendCore(int backend);
}
