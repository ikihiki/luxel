namespace Luxel.Audio.Silk;

internal enum OpenAlPlaybackState
{
    Initial,
    Playing,
    Paused,
    Stopped,
}

/// <summary>Small native seam used to make queue and lifecycle behavior testable without loading OpenAL.</summary>
internal interface IOpenAlApi : IDisposable
{
    bool SupportsStereoAngles { get; }
    bool SupportsLoopbackRendering { get; }

    void Initialize();
    void MakeContextCurrent();
    void SetListenerGain(float gain);
    uint CreateSource();
    void DeleteSource(uint source);
    uint CreateBuffer(ReadOnlySpan<byte> pcm, AudioFormat format);
    void DeleteBuffer(uint buffer);
    void QueueBuffer(uint source, uint buffer);
    uint[] UnqueueProcessedBuffers(uint source);
    void ClearSourceQueue(uint source);
    int GetBuffersQueued(uint source);
    OpenAlPlaybackState GetSourceState(uint source);
    void Play(uint source);
    void Pause(uint source);
    void Stop(uint source);
    void SetLooping(uint source, bool looping);
    void SetGain(uint source, float gain);
    void SetPitch(uint source, float pitch);
    void SetStereoPan(uint source, float pan);
    void RenderSamples(Span<short> samples, int frames);
}
