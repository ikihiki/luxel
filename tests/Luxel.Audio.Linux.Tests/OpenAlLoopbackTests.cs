namespace Luxel.Audio.Linux.Tests;

public sealed class OpenAlLoopbackTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void LoopbackRenders440HzAtExpectedRmsAndPitchDoublesFrequency()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var backend = CreateLoopback();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono48k);
        voice.SubmitBuffer(CreateSine(440f, 0.5f, channels: 1), loop: true);
        voice.Volume = 0.5f;
        voice.Play();

        Render(backend, 1024); // OpenAL Soft's resampler has a short startup transient.
        short[] normal = Render(backend, 12000);
        double normalFrequency = EstimateFrequency(normal, channels: 2, SampleRate);
        double normalRms = Rms(normal, channel: 0, channels: 2);
        Assert.InRange(normalFrequency, 435, 445);
        Assert.InRange(normalRms, 0.09, 0.13);

        voice.Pitch = 2f;
        short[] pitched = Render(backend, 12000);
        double pitchedFrequency = EstimateFrequency(pitched, channels: 2, SampleRate);
        Assert.InRange(pitchedFrequency, 870, 890);
    }

    [Fact]
    public void LoopbackStopProducesSilenceAndSingleBufferLoopContinues()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var backend = CreateLoopback();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono48k);
        voice.SubmitBuffer(CreateSine(440f, 0.02f, channels: 1), loop: true);
        voice.Play();

        Render(backend, 1024);
        short[] beyondOriginalBuffer = Render(backend, 4800);
        Assert.True(Rms(beyondOriginalBuffer, 0, 2) > 0.15);
        Assert.Equal(1, voice.BuffersQueued);

        voice.Stop();
        Render(backend, 1024); // Drain the mixer/resampler tail already in flight.
        short[] stopped = Render(backend, 1024);
        Assert.True(Rms(stopped, 0, 2) < 0.0001);
        Assert.Equal(0, voice.BuffersQueued);
        Assert.False(voice.IsPlaying);
    }

    [Fact]
    public void LoopbackStereoPanUsesStereoAnglesWhenAvailable()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var backend = CreateLoopback();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Stereo48k);
        voice.SubmitBuffer(CreateSine(440f, 0.25f, channels: 2), loop: true);
        try { voice.Pan = 1f; }
        catch (NotSupportedException error)
        {
            throw new Xunit.Sdk.XunitException($"OpenAL Soft is present but AL_EXT_STEREO_ANGLES is unavailable: {error.Message}");
        }
        voice.Play();

        Render(backend, 1024);
        short[] rendered = Render(backend, 6000);
        double left = Rms(rendered, 0, 2);
        double right = Rms(rendered, 1, 2);
        Assert.True(right > left * 4, $"Expected right pan, got left RMS {left:F4}, right RMS {right:F4}.");
    }

    [Fact]
    public void NativeLoopbackCollectsCompletedQueuedBuffers()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var backend = CreateLoopback();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Mono48k);
        voice.SubmitBuffer(CreateSine(330f, 0.02f, 1));
        voice.SubmitBuffer(CreateSine(550f, 0.02f, 1));
        voice.Play();

        Render(backend, 2400);

        Assert.Equal(0, voice.BuffersQueued);
        Assert.False(voice.IsPlaying);
    }

    private static OpenAlAudioBackend CreateLoopback()
    {
        var backend = new OpenAlAudioBackend(new NativeOpenAlApi(SampleRate, 2));
        try
        {
            backend.Initialize();
            Assert.True(backend.SupportsLoopbackRendering, "ALC_SOFT_loopback was not activated.");
            return backend;
        }
        catch (Exception error) when (error is DllNotFoundException or FileNotFoundException)
        {
            backend.Dispose();
            throw new Xunit.Sdk.XunitException($"Linux native tests require libopenal.so.1: {error.Message}");
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    private static short[] Render(OpenAlAudioBackend backend, int frames)
    {
        var result = new short[frames * 2];
        backend.RenderSamples(result, frames);
        return result;
    }

    private static byte[] CreateSine(float frequency, float seconds, int channels)
    {
        int frames = (int)(SampleRate * seconds);
        var pcm = new byte[frames * channels * sizeof(short)];
        for (int frame = 0; frame < frames; frame++)
        {
            short sample = (short)MathF.Round(MathF.Sin(2 * MathF.PI * frequency * frame / SampleRate) * short.MaxValue * 0.5f);
            for (int channel = 0; channel < channels; channel++)
            {
                int offset = (frame * channels + channel) * 2;
                pcm[offset] = (byte)sample;
                pcm[offset + 1] = (byte)(sample >> 8);
            }
        }
        return pcm;
    }

    private static double Rms(short[] samples, int channel, int channels)
    {
        double sum = 0;
        int count = 0;
        for (int i = channel; i < samples.Length; i += channels)
        {
            double value = samples[i] / 32768.0;
            sum += value * value;
            count++;
        }
        return Math.Sqrt(sum / count);
    }

    private static double EstimateFrequency(short[] samples, int channels, int sampleRate)
    {
        // Schmitt-trigger crossings ignore sub-LSB startup/resampler noise around zero.
        const short threshold = 512;
        int crossings = 0;
        bool below = false;
        int frames = samples.Length / channels;
        for (int frame = 0; frame < frames; frame++)
        {
            short current = samples[frame * channels];
            if (current < -threshold) below = true;
            else if (below && current > threshold) { crossings++; below = false; }
        }
        return crossings * sampleRate / (double)frames;
    }
}
