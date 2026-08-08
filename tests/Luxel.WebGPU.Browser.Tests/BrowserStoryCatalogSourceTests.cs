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
        string styles = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "gallery.css"));
        string markdown = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "GalleryMarkdownHtml.cs"));
        string project = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "GalleryBrowser.csproj"));

        Assert.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", project, StringComparison.Ordinal);
        Assert.Contains("WebAssemblyHostBuilder.CreateDefault(args)", entryPoint, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddCoreUiStory()", entryPoint, StringComparison.Ordinal);
        Assert.Contains("@inject StoryCatalog Catalog", app, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(requested)", app, StringComparison.Ordinal);
        Assert.Contains("_ = RunStoryAsync(_story.Path, _argsJson)", app, StringComparison.Ordinal);
        Assert.Contains("BrowserGalleryApplication.RunAsync(Services, story, argsJson)", app, StringComparison.Ordinal);
        Assert.Contains("class=\"gallery-sidebar\"", app, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search stories\"", app, StringComparison.Ordinal);
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
        Assert.DoesNotContain("RuntimeBundleId", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("browser-runtime-manifest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet.create", script, StringComparison.Ordinal);
        Assert.Contains("export const setReady", script, StringComparison.Ordinal);
        Assert.Contains("export const publishWebGpuDiagnostics", script, StringComparison.Ordinal);
        Assert.Contains("BrowserWebGpuBackend.CaptureLatestDiagnostics(ex, \"BrowserGalleryApplication.RunAsync\")", runtime, StringComparison.Ordinal);
        Assert.Contains("browserBackend.CaptureDiagnostics()", runtime, StringComparison.Ordinal);
        Assert.Contains("BuildStoryWidget(story, context, result, font", runtime, StringComparison.Ordinal);
        Assert.Contains("MarkdownDoc.FromStoryResult", runtime, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(reference.Path)", runtime, StringComparison.Ordinal);
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
