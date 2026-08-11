using System.Runtime.CompilerServices;

namespace Luxel.Gallery;

internal static class SharedSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "support.source-tree", "Luxel source dependency closure",
            "Internal support bundle that preserves repository-relative ProjectReference and shader import paths for clean temp builds.", "Internal",
            SampleCopyLevel.GalleryOnly,
            [new("Directory.Build.props", SampleFileKind.Asset),
             new("src", SampleFileKind.Asset, Destination: "src", AssetGlob: "*", Mode: SampleFileMode.Glob),
             new("shaders", SampleFileKind.Asset, Destination: "shaders", AssetGlob: "*", Mode: SampleFileMode.Glob),
             new("eng/Luxel.ShaderWgslGen", SampleFileKind.Asset, Destination: "eng/Luxel.ShaderWgslGen", AssetGlob: "*", Mode: SampleFileMode.Glob)]));
    }
}
