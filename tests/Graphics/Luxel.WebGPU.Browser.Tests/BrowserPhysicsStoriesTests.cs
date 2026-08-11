namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserPhysicsStoriesTests
{
    private static readonly string[] Paths =
    [
        "Examples/3D/PhysicsFalling",
        "Examples/3D/PhysicsPlayground",
        "Examples/3D/PhysicsGizmos",
        "Examples/3D/PhysicsTrigger",
        "Examples/3D/PhysicsMesh",
    ];

    [Fact]
    public void Physics_stories_use_browser_safe_Bepu_simulation_and_submission()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root,
            "src", "Framework", "Luxel.Framework.Gallery", "Stories", "PhysicsBrowserStories.cs"));
        string frameworkGalleryProject = File.ReadAllText(Path.Combine(root,
            "src", "Framework", "Luxel.Framework.Gallery", "Luxel.Framework.Gallery.csproj"));
        string browserLibrary = File.ReadAllText(Path.Combine(root,
            "src", "Gallery", "Luxel.Gallery.Browser", "Luxel.Gallery.Browser.csproj"));
        string browserProject = File.ReadAllText(Path.Combine(root,
            "gallery", "GalleryBrowser", "GalleryBrowser.csproj"));

        foreach (string path in Paths) Assert.Contains($"[Story(\"{path}\"", source);
        Assert.Contains("Luxel.Physics.csproj", frameworkGalleryProject);
        Assert.Contains("Luxel.Physics.Gizmos.csproj", frameworkGalleryProject);
        Assert.Contains("Luxel.Physics.csproj", browserLibrary);
        Assert.Contains("TrimmerRootAssembly Include=\"Luxel.Physics\"", browserProject);
        Assert.Contains("TrimmerRootAssembly Include=\"Luxel.Physics.Gizmos\"", browserProject);
        Assert.Contains("PackageReference Include=\"BepuPhysics\"", browserProject);
        Assert.Contains("PackageReference Include=\"BepuUtilities\"", browserProject);
        Assert.Contains("TrimmerRootAssembly Include=\"BepuPhysics\"", browserProject);
        Assert.Contains("TrimmerRootAssembly Include=\"BepuUtilities\"", browserProject);
        Assert.Contains("Args = nameof(PhysicsPlaygroundArgs)", source);
        Assert.Contains("ctx.Arg(\"gravity\"", source);
        Assert.Contains("ctx.Arg(\"bounciness\"", source);
        Assert.Contains("ctx.Arg(\"reset\"", source);
        Assert.DoesNotContain("ctx.Signal(", source);
        Assert.DoesNotContain("PhysicsGpuView(", source);
        Assert.Contains("new PhysicsGpuDemo(device, null, null, null)", source);
        Assert.Contains("new PhysicsGpuDemo(device, gravity, bounciness, reset)", source);
        Assert.Contains("new PhysicsSettings { ThreadCount = 0 }", source);
        Assert.Contains("new PhysicsStepSystem", source);
        Assert.Contains("MeshCollider.Static", source);
        Assert.Contains("HullCollider.Dynamic", source);
        Assert.Contains("new Trigger()", source);
        Assert.Contains("PhysicsGizmos.DrawColliders", source);
        Assert.Contains("device.MainQueue.Submit(command)", source);
        Assert.DoesNotContain("GpuShaderCode.Load", source);
        Assert.DoesNotContain("SubmitAndWait", source);
        Assert.DoesNotContain("WaitIdle", source);
    }

    [Fact]
    public void Native_story_assembly_no_longer_owns_browser_physics_routes()
    {
        string root = FindRepositoryRoot();
        string storyRoot = Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Stories");
        string source = string.Join("\n", Directory.GetFiles(storyRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        foreach (string path in Paths) Assert.DoesNotContain($"[Story(\"{path}\"", source);
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
