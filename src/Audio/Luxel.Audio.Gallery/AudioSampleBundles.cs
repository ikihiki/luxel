using System.Runtime.CompilerServices;

namespace Luxel.Audio.Gallery;

internal static class AudioSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
        => SampleBundleRegistry.Register(new SampleBundleInfo(
            "audio.tone", "Headless audio tone",
            "Procedural PCM16, AudioClip, AudioMixer and NullAudioBackend with observable voice state.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelAudio/LuxelAudio.csproj", SampleFileKind.Project),
             new("samples/LuxelAudio/Program.cs", SampleFileKind.CSharp),
             new("samples/LuxelAudio/AudioConceptSamples.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"],
            Requirements: [".NET 10", "Headless: any supported OS", "Audible output: Windows/XAudio2 integration"],
            ExportSymbol: "AudioMixer",
            RunCommand: "dotnet run --project samples/LuxelAudio",
            SmokeCommand: "dotnet run --project samples/LuxelAudio",
            Platforms: ["Windows", "Linux", "macOS"],
            ExpectedStdoutMarker: "audio: initialized=True, voices=1"));
}
