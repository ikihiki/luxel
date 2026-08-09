using System.Runtime.CompilerServices;

namespace Luxel.GamesSamples.Gallery;

internal static class GamesSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "game.cavern", "Luxel Cavern capstone",
            "Repository recipe integrating Framework, UI, 2D, input, audio, resources, particles and settings.", "Advanced",
            SampleCopyLevel.Recipe,
            [new("samples/LuxelCavern/LuxelCavern/LuxelCavern.csproj", SampleFileKind.Project),
             new("samples/LuxelCavern/LuxelCavern/CavernRealtimeScene.cs", SampleFileKind.CSharp)],
            Requirements: ["Repository checkout", ".NET 10", "Windows real-window host"], ExportSymbol: "CavernRealtimeScene",
            RunCommand: "dotnet run --project samples/LuxelCavern/LuxelCavern",
            SmokeCommand: "dotnet build samples/LuxelCavern/LuxelCavern/LuxelCavern.csproj --configuration Release --no-restore",
            Platforms: ["Windows"], TimeoutSeconds: 180));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "game.range", "Luxel Range capstone",
            "Repository recipe integrating ECS, physics, glTF, GPU asset extraction and 3D particles.", "Advanced",
            SampleCopyLevel.Recipe,
            [new("samples/LuxelRange/LuxelRange/LuxelRange.csproj", SampleFileKind.Project),
             new("samples/LuxelRange/LuxelRange/RangeRealtimeScene.cs", SampleFileKind.CSharp)],
            Requirements: ["Repository checkout", ".NET 10", "Windows real-window host", "Khronos Fox asset"], ExportSymbol: "RangeRealtimeScene",
            RunCommand: "dotnet run --project samples/LuxelRange/LuxelRange",
            SmokeCommand: "dotnet build samples/LuxelRange/LuxelRange/LuxelRange.csproj --configuration Release --no-restore",
            Platforms: ["Windows"], TimeoutSeconds: 240));
    }
}
