namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserEcsStoriesTests
{
    [Fact]
    public void EcsCubes_story_uses_browser_safe_ECS_extraction_and_submission()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root,
            "src", "Gallery", "Luxel.Gallery.Stories.CoreUi", "Stories", "EcsCubesStories.cs"));
        string project = File.ReadAllText(Path.Combine(root,
            "src", "Gallery", "Luxel.Gallery.Stories.CoreUi", "Luxel.Gallery.Stories.CoreUi.csproj"));
        string nativeSource = File.ReadAllText(Path.Combine(root,
            "src", "Gallery", "Luxel.Gallery.Stories", "Stories", "Gpu", "Ecs3DStories.cs"));

        Assert.Contains("[Story(\"Examples/3D/EcsCubes\"", source);
        Assert.Contains("Luxel.Ecs.csproj", project);
        Assert.Contains("Luxel.Assets.csproj", project);
        Assert.Contains("Luxel.AssetRuntime.csproj", project);
        Assert.Contains("shaders\\compiled\\cube_forward.*", project);
        Assert.Contains("TrimmerRootAssembly Include=\"Luxel.AssetRuntime\"", File.ReadAllText(Path.Combine(root,
            "gallery", "GalleryBrowser", "GalleryBrowser.csproj")));
        Assert.Contains("new Render3DExtractSystem", source);
        Assert.Contains("TransformPropagateSystem.Run(world)", source);
        Assert.Contains(".Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)", source);
        Assert.Contains("device.MainQueue.Submit(command)", source);
        Assert.Contains("surface.CopyColorToFramebuffer(pass.Cmd)", source);
        Assert.DoesNotContain("GpuShaderCode.Load", source);
        Assert.DoesNotContain("SubmitAndWait", source);
        Assert.DoesNotContain("WaitIdle", source);
        Assert.DoesNotContain("[Story(\"Examples/3D/EcsCubes\"", nativeSource);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root.");
    }
}
