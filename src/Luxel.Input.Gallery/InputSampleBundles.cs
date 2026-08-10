using System.Runtime.CompilerServices;

namespace Luxel.Input.Gallery;

internal static class InputSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
        => SampleBundleRegistry.Register(new SampleBundleInfo(
            "input.actions", "Deterministic input actions",
            "FakeInputSource drives InputBus, InputStack, ButtonAction and Axis2DAction without a window.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelInput/LuxelInput.csproj", SampleFileKind.Project), new("samples/LuxelInput/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "InputStack",
            RunCommand: "dotnet run --project samples/LuxelInput", SmokeCommand: "dotnet run --project samples/LuxelInput",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "input: jump=True, triggered=1"));
}
