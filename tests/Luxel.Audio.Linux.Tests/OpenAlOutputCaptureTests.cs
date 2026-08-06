using Luxel.Audio;

namespace Luxel.Audio.Linux.Tests;

public sealed class OpenAlOutputCaptureTests
{
    [Fact]
    public void OutputCapture_EmitsOneKilohertzToneAndDrainsQueue()
    {
        if (Environment.GetEnvironmentVariable("LUXEL_AUDIO_OUTPUT_CAPTURE") != "1")
            return;

        using var backend = new OpenAlAudioBackend();
        backend.Initialize();
        using IAudioVoice voice = backend.CreateVoice(AudioFormat.Pcm16Stereo48k);
        voice.SubmitBuffer(CreateTone(1_000f, seconds: 1f));
        voice.Play();

        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (voice.BuffersQueued > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.Equal(0, voice.BuffersQueued);
        Assert.False(voice.IsPlaying);
    }

    private static byte[] CreateTone(float frequency, float seconds)
    {
        const int sampleRate = 48_000;
        int frames = checked((int)(sampleRate * seconds));
        byte[] pcm = new byte[frames * 2 * sizeof(short)];
        for (int frame = 0; frame < frames; frame++)
        {
            short sample = (short)MathF.Round(12_000f * MathF.Sin(2f * MathF.PI * frequency * frame / sampleRate));
            int offset = frame * 4;
            pcm[offset] = (byte)sample;
            pcm[offset + 1] = (byte)(sample >> 8);
            pcm[offset + 2] = (byte)sample;
            pcm[offset + 3] = (byte)(sample >> 8);
        }
        return pcm;
    }
}
