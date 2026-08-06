using Luxel.Audio;

// docs:begin audio-tone
var format = new AudioFormat(48_000, 1, 16);
byte[] pcm = new byte[4_800 * format.BytesPerSample];
for (int i = 0; i < 4_800; i++)
{
    short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / format.SampleRate) * short.MaxValue * 0.2);
    BitConverter.TryWriteBytes(pcm.AsSpan(i * 2, 2), sample);
}
var clip = new AudioClip(format, pcm, "440Hz tone");
using var backend = new NullAudioBackend();
backend.Initialize();
using var mixer = new AudioMixer(backend);
mixer.PlayOneShot(clip, volume: 0.5f);
// docs:end audio-tone
IAudioVoice voice = backend.Voices.Single();
Console.WriteLine($"audio: initialized={backend.Initialized}, voices={backend.Voices.Count}, queued={voice.BuffersQueued}, playing={voice.IsPlaying}, bytes={pcm.Length}");
return backend.Initialized && voice.BuffersQueued == 1 && voice.IsPlaying && AudioConceptSamples.RunAll() ? 0 : 1;
