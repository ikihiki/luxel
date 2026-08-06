using System.Numerics;
using System.Text;
using Luxel.Audio;

internal static class AudioConceptSamples
{
    internal static bool RunAll()
        => FormatAndClip() && MixerAndVoice() && SourceAndBus() && Spatial() && Streaming() && HeadlessTest();

    // docs:begin audio-format-clip
    internal static bool FormatAndClip()
    {
        var format = new AudioFormat(SampleRate: 48_000, Channels: 1, BitsPerSample: 16);
        int frames = 4_800; // 100 ms
        byte[] pcm = SinePcm16(format, frames, 440f);
        var clip = new AudioClip(format, pcm, "procedural 440 Hz");

        return clip.SampleCount == frames
            && clip.Duration == TimeSpan.FromMilliseconds(100)
            && clip.PcmData.Length == frames * format.BytesPerSample;
    }
    // docs:end audio-format-clip

    // docs:begin audio-mixer-voice
    internal static bool MixerAndVoice()
    {
        var format = AudioFormat.Pcm16Mono48k;
        var clip = new AudioClip(format, SinePcm16(format, 480, 660f), "one-shot");
        using var backend = new NullAudioBackend();
        backend.Initialize();
        using var mixer = new AudioMixer(backend);

        mixer.PlayOneShot(clip, volume: 0.5f, pitch: 1.25f, pan: -0.25f);
        IAudioVoice voice = backend.Voices.Single();
        bool submitted = mixer.ActiveVoiceCount == 1
            && voice.BuffersQueued == 1 && voice.IsPlaying
            && voice.Volume == 0.5f && voice.Pitch == 1.25f && voice.Pan == -0.25f;

        // Real backends decrement BuffersQueued as playback completes; then Tick returns the voice to the pool.
        voice.Stop();
        mixer.Tick();
        return submitted && mixer.ActiveVoiceCount == 0;
    }
    // docs:end audio-mixer-voice

    // docs:begin audio-source-bus
    internal static bool SourceAndBus()
    {
        var master = new AudioBus("Master");
        var music = new AudioBus("Music", master);
        var sfx = new AudioBus("SFX", master);
        master.Volume.Value = 0.8f;
        music.Volume.Value = 0.5f;
        sfx.Volume.Value = 0.25f;

        using var backend = new NullAudioBackend();
        backend.Initialize();
        var clip = new AudioClip(AudioFormat.Pcm16Mono48k, new byte[960], "loop");
        using var source = new AudioSource(backend, clip) { Bus = music };
        source.Volume.Value = 0.5f;
        source.Play(loop: true);
        source.Tick(); // copy Signal and bus values to the voice every frame

        IAudioVoice voice = backend.Voices.Single();
        return Math.Abs(music.EffectiveVolume - 0.4f) < 0.0001f
            && Math.Abs(sfx.EffectiveVolume - 0.2f) < 0.0001f
            && Math.Abs(voice.Volume - 0.2f) < 0.0001f;
    }
    // docs:end audio-source-bus

    // docs:begin audio-spatial
    internal static bool Spatial()
    {
        using var backend = new NullAudioBackend();
        backend.Initialize();
        var clip = new AudioClip(AudioFormat.Pcm16Mono48k, new byte[960], "spatial probe");
        var listener = new AudioListener { Position = Vector3.Zero };
        using var source = new AudioSource3D(backend, clip)
        {
            Position = new Vector3(5, 0, 0),
            MinDistance = 1,
            MaxDistance = 9,
        };

        source.Play();
        source.Update(listener); // listener pose first, source position second, spatial update last
        return Math.Abs(source.EffectiveVolume - 0.5f) < 0.0001f
            && Math.Abs(source.EffectivePan - 1f) < 0.0001f;
    }
    // docs:end audio-spatial

    // docs:begin audio-streaming
    internal static bool Streaming()
    {
        byte[] wav = CreatePcm16Wav(sampleRate: 1_000, channels: 1, frames: 250);
        using var backend = new NullAudioBackend();
        backend.Initialize();
        using var memory = new MemoryStream(wav, writable: false);
        using var stream = new WavStream(memory, leaveOpen: true);
        using var playback = new StreamingVoice(backend, stream, chunkSeconds: 0.1f, queueDepth: 2);

        playback.Pump();
        bool prebuffered = playback.IsPlaying && playback.BuffersQueued == 2;
        playback.Stop();
        return prebuffered && playback.Finished && playback.BuffersQueued == 0;
    }
    // docs:end audio-streaming

    // docs:begin audio-headless-test
    internal static bool HeadlessTest()
    {
        using var backend = new NullAudioBackend();
        backend.Initialize();
        IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Stereo48k);
        voice.SubmitBuffer(new byte[480 * AudioFormat.Pcm16Stereo48k.BytesPerSample]);
        voice.Volume = 0.75f;
        voice.Pan = 0.25f;
        voice.Play();

        bool observable = backend.Initialized && backend.Voices.Count == 1
            && voice.BuffersQueued == 1 && voice.IsPlaying
            && voice.Volume == 0.75f && voice.Pan == 0.25f;
        voice.Dispose();
        return observable;
    }
    // docs:end audio-headless-test

    private static byte[] SinePcm16(AudioFormat format, int frames, float frequency)
    {
        byte[] pcm = new byte[frames * format.BytesPerSample];
        for (int frame = 0; frame < frames; frame++)
        {
            short sample = (short)(Math.Sin(2 * Math.PI * frequency * frame / format.SampleRate) * short.MaxValue * 0.2);
            for (int channel = 0; channel < format.Channels; channel++)
                BitConverter.TryWriteBytes(pcm.AsSpan((frame * format.Channels + channel) * 2, 2), sample);
        }
        return pcm;
    }

    private static byte[] CreatePcm16Wav(int sampleRate, short channels, int frames)
    {
        int dataBytes = frames * channels * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
        writer.Write(channels); writer.Write(sampleRate); writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2)); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
        return stream.ToArray();
    }
}
