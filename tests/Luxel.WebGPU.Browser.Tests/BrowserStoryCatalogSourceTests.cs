namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserStoryCatalogSourceTests
{
    [Fact]
    public void Blazor_host_reads_the_CoreUi_catalog_directly_without_a_runtime_manifest()
    {
        string root = FindRepositoryRoot();
        string entryPoint = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "Program.cs"));
        string app = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "App.razor"));
        string runtime = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Browser", "BrowserGalleryApplication.cs"));
        string script = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "main.js"));
        string project = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "GalleryBrowser.csproj"));

        Assert.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", project, StringComparison.Ordinal);
        Assert.Contains("WebAssemblyHostBuilder.CreateDefault(args)", entryPoint, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddCoreUiStory()", entryPoint, StringComparison.Ordinal);
        Assert.Contains("@inject StoryCatalog Catalog", app, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(requested)", app, StringComparison.Ordinal);
        Assert.Contains("_ = RunStoryAsync(story.Path, argsJson)", app, StringComparison.Ordinal);
        Assert.Contains("BrowserGalleryApplication.RunAsync(Services, story, argsJson)", app, StringComparison.Ordinal);
        Assert.Contains("JSHost.ImportAsync(\"luxel-browser-host\", \"../main.js\")", app, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(path)", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeBundleId", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("browser-runtime-manifest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet.create", script, StringComparison.Ordinal);
        Assert.Contains("export const setReady", script, StringComparison.Ordinal);
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
