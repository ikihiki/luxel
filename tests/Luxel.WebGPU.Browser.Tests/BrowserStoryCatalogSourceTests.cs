namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserStoryCatalogSourceTests
{
    [Fact]
    public void Blazor_host_composes_resource_and_CoreUi_catalogs_without_a_runtime_manifest()
    {
        string root = FindRepositoryRoot();
        string entryPoint = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "Program.cs"));
        string app = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "App.razor"));
        string runtime = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Browser", "BrowserGalleryApplication.cs"));
        string script = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "main.js"));
        string styles = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "gallery.css"));
        string markdown = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "GalleryMarkdownHtml.cs"));
        string project = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "GalleryBrowser.csproj"));

        string solution = File.ReadAllText(Path.Combine(root, "Luxel.slnx"));
        string resourceProject = File.ReadAllText(Path.Combine(root, "src", "Resource", "Luxel.Resources.Gallery", "ResourceGalleryProject.cs"));
        string resourceBundles = File.ReadAllText(Path.Combine(root, "src", "Resource", "Luxel.Resources.Gallery", "ResourceSampleBundles.cs"));
        string fullGalleryBundles = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Stories", "SampleBundles.cs"));
        string fixtureTargets = File.ReadAllText(Path.Combine(root, "assets", "Luxel.KhronosBox.targets"));
        string dependencyChecker = File.ReadAllText(Path.Combine(root, "eng", "check-project-dependencies.py"));

        Assert.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", project, StringComparison.Ordinal);
        Assert.Contains("WebAssemblyHostBuilder.CreateDefault(args)", entryPoint, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddSingleton(new HttpClient", entryPoint, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddResourceGallery()", entryPoint, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddCoreUiStory()", entryPoint, StringComparison.Ordinal);
        Assert.True(
            entryPoint.IndexOf("builder.Services.AddResourceGallery()", StringComparison.Ordinal)
            < entryPoint.IndexOf("builder.Services.AddCoreUiStory()", StringComparison.Ordinal));
        Assert.Contains("Luxel.Resources.Gallery.csproj", project, StringComparison.Ordinal);
        Assert.Contains("src/Resource/Luxel.Resources.Gallery/Luxel.Resources.Gallery.csproj", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Luxel.Gallery.Resources.Stories", solution, StringComparison.Ordinal);
        Assert.Contains("StoryRegistration_Luxel_Resources_Gallery", resourceProject, StringComparison.Ordinal);
        Assert.Contains("\"resources.scenarios\", \"Resource scenarios\"", resourceBundles, StringComparison.Ordinal);
        Assert.DoesNotContain("\"resources.scenarios\", \"Resource scenarios\"", fullGalleryBundles, StringComparison.Ordinal);
        Assert.Contains("wwwroot\\tools\\khronos-samples\\Box\\Box.gltf", project, StringComparison.Ordinal);
        Assert.Contains("wwwroot\\tools\\khronos-samples\\BoxAnimated\\BoxAnimated.glb", project, StringComparison.Ordinal);
        Assert.Contains("wwwroot\\tools\\khronos-samples\\RiggedSimple\\RiggedSimple.glb", project, StringComparison.Ordinal);
        Assert.Contains("@inject StoryCatalog Catalog", app, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(requested)", app, StringComparison.Ordinal);
        Assert.Contains("_ = RunStoryAsync(_story.Path, _argsJson)", app, StringComparison.Ordinal);
        Assert.Contains("BrowserGalleryApplication.RunAsync(Services, story, argsJson)", app, StringComparison.Ordinal);
        Assert.Contains("class=\"gallery-sidebar\"", app, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Storyを検索\"", app, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(StoryHref(story.Path), forceLoad: false)", app, StringComparison.Ordinal);
        Assert.Contains("Navigation.LocationChanged += OnLocationChanged", app, StringComparison.Ordinal);
        Assert.Contains("class=\"story-runtime-frame\"", app, StringComparison.Ordinal);
        Assert.Contains("@onclick:preventDefault", File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "StoryTree.razor")), StringComparison.Ordinal);
        Assert.Contains("class=\"markdown-document\"", app, StringComparison.Ordinal);
        Assert.Contains("GalleryMarkdownHtml.Render(story, result)", app, StringComparison.Ordinal);
        Assert.Contains("Markdig", project, StringComparison.Ordinal);
        Assert.Contains("Markdown.ToHtml", markdown, StringComparison.Ordinal);
        Assert.Contains("markdown-story-embed", markdown, StringComparison.Ordinal);
        Assert.Contains(".gallery-sidebar", styles, StringComparison.Ordinal);
        Assert.Contains("JSHost.ImportAsync(\"luxel-browser-host\", \"../main.js\")", app, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(path)", runtime, StringComparison.Ordinal);
        Assert.Contains("new WebPlatformFileSystem(", runtime, StringComparison.Ordinal);
        Assert.Contains("ResourceSystemDefaultHandles defaults = resourceBuilder.AddBrowserCore()", runtime, StringComparison.Ordinal);
        Assert.Contains("ResourceSystemDefaults.AddBuiltinSourcesForWeb(resourceBuilder, defaults, files, http)", runtime, StringComparison.Ordinal);
        Assert.Contains("resourceBuilder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())", runtime, StringComparison.Ordinal);
        Assert.Contains("options.ConfigureDomain = domain => domain.UseBrowserCooperative()", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResourceSystem(", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallAssetGpu", runtime, StringComparison.Ordinal);
        Assert.Contains("await resources.PumpAsync()", runtime, StringComparison.Ordinal);
        Assert.Contains("await context.PumpObservedResourcesAsync()", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("resources.Pump();", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeBundleId", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("browser-runtime-manifest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet.create", script, StringComparison.Ordinal);
        Assert.Contains("export const setReady", script, StringComparison.Ordinal);
        Assert.Contains("export const publishWebGpuDiagnostics", script, StringComparison.Ordinal);
        Assert.Contains("BrowserWebGpuBackend.CaptureLatestDiagnostics(ex, \"BrowserGalleryApplication.RunAsync\")", runtime, StringComparison.Ordinal);
        Assert.Contains("browserBackend.CaptureDiagnostics()", runtime, StringComparison.Ordinal);
        Assert.Contains("BuildStoryWidget(story, context, result, font", runtime, StringComparison.Ordinal);
        Assert.Contains("StoryMarkdownDocumentAdapter.FromStoryResult", runtime, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(reference.Path)", runtime, StringComparison.Ordinal);
        Assert.Contains("5bad5aaa0bbb5d0f9cdc934e626f27d0df1e79b8", fixtureTargets, StringComparison.Ordinal);
        Assert.Contains("BoxAnimated/glTF-Binary/BoxAnimated.glb", fixtureTargets, StringComparison.Ordinal);
        Assert.Contains("RiggedSimple/glTF-Binary/RiggedSimple.glb", fixtureTargets, StringComparison.Ordinal);
        Assert.Contains("GetFileHash Files=\"@(_KhronosSampleAsset)\" Algorithm=\"SHA256\"", fixtureTargets, StringComparison.Ordinal);
        Assert.Contains("src/Resource/Luxel.Resources.Browser/Luxel.Resources.Browser.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("forbid_closure(\"Luxel.Resources\"", dependencyChecker, StringComparison.Ordinal);
        Assert.Contains("stem == \"Luxel.Resources.Gallery\"", dependencyChecker, StringComparison.Ordinal);
        Assert.Contains("forbid_closure(\"Luxel.Resources.Gallery\", browser_forbidden", dependencyChecker, StringComparison.Ordinal);
        Assert.Contains("Luxel.Graphics.Vulkan", dependencyChecker, StringComparison.Ordinal);
        Assert.Contains("forbid_closure(\"GalleryBrowser\", browser_forbidden", dependencyChecker, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "browser-runtime-manifest.json")));
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
