using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Luxel.Controls;
using Luxel.Gallery;
using Luxel.UI;
using Markdig;

namespace Luxel.Gallery.Site;

public sealed record SiteExportReport(int Stories, int Images, int Unavailable, int Errors);
public sealed record SiteStory(string Path, string Name, string Component, string Fragment, string? Image,
    string Status, string? Error, string ImageSha256, string SearchText, IReadOnlyList<string> Aliases, SampleBundleInfo? Bundle);

public static partial class GallerySiteExporter
{
    private const string BrowserRuntimeEmbedPagePath = "Learn/Rendering/Basics/FirstTriangle";
    private const string BrowserRuntimeBaseUrl = "samples/webgpu-browser/";
    private sealed record BrowserRuntimeRoute(string StoryPath, string Url, string Title);
    private static readonly IReadOnlyDictionary<string, BrowserRuntimeRoute> BrowserRuntimeRoutes =
        new Dictionary<string, BrowserRuntimeRoute>(StringComparer.Ordinal)
        {
            ["Examples/3D/Triangle"] = new("Examples/3D/Triangle", BrowserRuntimeBaseUrl + "?app=triangle", "Interactive WebGPU triangle"),
            ["Controls/Button/Counter"] = new("Controls/Button/Counter", BrowserRuntimeBaseUrl + "?app=counter", "Interactive button counter"),
        };
    private static readonly string[] BrowserRuntimeRequiredFiles = ["index.html", "main.js", Path.Combine("_framework", "dotnet.js")];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static SiteExportReport Export(GalleryHost host, IReadOnlyList<StoryInfo> stories, string output, string repositoryRoot,
        string? browserWebGpuRoot = null)
    {
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        string fragmentsDir = Path.Combine(output, "stories");
        string imagesDir = Path.Combine(output, "images");
        Directory.CreateDirectory(fragmentsDir);
        Directory.CreateDirectory(imagesDir);
        string highlightDir = Path.Combine(output, "vendor", "highlightjs");
        Directory.CreateDirectory(highlightDir);
        CopyRuntimeAsset(Path.Combine("Assets", "highlightjs", "highlight.min.js"), Path.Combine(highlightDir, "highlight.min.js"));
        CopyRuntimeAsset(Path.Combine("Assets", "highlightjs", "github-dark.min.css"), Path.Combine(highlightDir, "github-dark.min.css"));
        string licensesDir = Path.Combine(output, "licenses");
        Directory.CreateDirectory(licensesDir);
        string boxLicense = Path.Combine(repositoryRoot, "tools", "khronos-samples", "Box-LICENSE.md");
        if (!File.Exists(boxLicense))
            throw new FileNotFoundException("Khronos Box license was not provisioned before site export.", boxLicense);
        CopyRuntimeAsset(Path.Combine("Assets", "highlightjs", "LICENSE"), Path.Combine(licensesDir, "highlight.js-LICENSE.txt"));
        File.Copy(boxLicense, Path.Combine(licensesDir, "Box-LICENSE.md"), overwrite: true);
        File.WriteAllText(Path.Combine(output, ".nojekyll"), "", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "site.css"), Css + StorySourceCss, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "site.js"), Js, new UTF8Encoding(false));

        bool browserRuntimeEnabled = browserWebGpuRoot is not null;
        if (browserRuntimeEnabled)
            CopyBrowserRuntime(browserWebGpuRoot!, Path.Combine(output, BrowserRuntimeBaseUrl.Replace('/', Path.DirectorySeparatorChar)));

        var manifest = new List<SiteStory>();
        var imageCache = new Dictionary<string, (string? Url, string Status, string? Error, string Hash)>(StringComparer.Ordinal);
        int unavailable = 0, errors = 0;
        foreach (StoryInfo story in stories)
        {
            string slug = Slug(story.Path);
            string fragmentUrl = $"stories/{slug}.html";
            string? imageUrl = null;
            string status = "captured";
            string? error = null;
            string imageHash = "";
            TextEditorView? document = null;

            string body = "";
            try
            {
                if (browserRuntimeEnabled && BrowserRuntimeRoutes.TryGetValue(story.Path, out BrowserRuntimeRoute? runtimeRoute))
                {
                    status = "runtime";
                    body = RuntimeStory(runtimeRoute, embedded: false);
                }
                else if (story.RealWindowOnly)
                {
                    status = "unavailable";
                    error = "This story requires a real window and is not available in the static gallery.";
                }
                else
                {
                    // CI renders through lavapipe, so rebuilding and stabilizing a story twice is especially
                    // expensive. Realize once, inspect the current root, and capture that same stabilized frame.
                    byte[]? png = FindGolden(story.Path, repositoryRoot) is { } golden ? File.ReadAllBytes(golden) : null;
                    string? realizationError = null;
                    try
                    {
                        host.SelectExact(story.Path);
                        GallerySnapshots.Stabilize(host);
                        if (png is null)
                        {
                            GallerySnapshotResult capture = GallerySnapshots.CaptureCurrent(host, story.Path);
                            png = capture.Png;
                            realizationError = capture.Error;
                        }
                        document = GallerySnapshots.FindDocument(host.CurrentRoot);
                    }
                    catch (Exception e)
                    {
                        realizationError = $"{e.GetType().Name}: {e.Message}";
                    }

                    (imageUrl, status, error, imageHash) = StoreStoryImage(
                        story, imagesDir, imageCache, png, realizationError);
                    if (png is not null && realizationError is not null)
                    {
                        // Preserve an already available golden or runtime capture if optional document
                        // introspection fails after the frame is ready.
                        error = "Live document introspection unavailable; static capture preserved. " + realizationError;
                    }
                }

                if (status == "runtime")
                {
                    // The runtime body was selected before any native realization or static capture.
                }
                else if (document is not null)
                {
                    IReadOnlyList<string> linkErrors = DocsIndex.ValidateLinks(story.Path, document.DocSource!);
                    if (linkErrors.Count > 0)
                        throw new InvalidDataException("Broken documentation links: " + string.Join(", ", linkErrors));
                    string md = ReplaceEmbeds(story.Path, document.DocSource!, document.DocEmbeds, host, imagesDir, repositoryRoot,
                        imageCache, browserRuntimeEnabled, ref unavailable, ref errors);
                    md = RewriteLocalImages(md, imagesDir, repositoryRoot);
                    md = ReplaceSpecialFences(md, host, imagesDir, ref errors);
                    body = RenderMarkdown(md, story.Path);
                }
                else if (imageUrl is not null)
                    body = StaticFigure(imageUrl, story.Path, "Static story capture");
                else
                    body = Unavailable(error ?? "No static capture is available.", status);
            }
            catch (Exception e)
            {
                // Semantic failures belong to this page. Preserve any image already captured, emit an explicit
                // error fragment/manifest entry, and continue exporting later stories. Shared setup, writes and
                // final structural validation remain fatal so a corrupt site is never reported as successful.
                status = "error";
                error = $"{e.GetType().Name}: {e.Message}";
                body = Unavailable(error, status);
                Console.Error.WriteLine($"[gallery-site] story export error '{story.Path}': {e}");
            }

            string fragment;
            if (status == "runtime")
                fragment = $"<article class=\"story runtime-page\">{body}</article>";
            else
            {
                string badge = "<p class=\"static-badge\">Static capture — not interactive</p>";
                fragment = $"<article class=\"story\"><header>{badge}<h1>{H(story.Path)}</h1></header>{body}{BundleHtml(SampleBundleRegistry.Find(story.SampleBundle))}{StorySourceHtml(story.Source)}</article>";
            }
            File.WriteAllText(Path.Combine(output, fragmentUrl.Replace('/', Path.DirectorySeparatorChar)), fragment, new UTF8Encoding(false));
            if (status == "unavailable") unavailable++;
            if (status == "error") errors++;
            string searchText = story.Path + "\n" + story.Name + "\n" + story.Component
                + (document?.DocSource is { } source ? "\n" + source : "")
                + (story.Source is { Length: > 0 } code ? "\n" + code : "");
            manifest.Add(new SiteStory(story.Path, story.Name, story.Component, fragmentUrl, imageUrl, status, error,
                imageHash, searchText, Array.Empty<string>(), SampleBundleRegistry.Find(story.SampleBundle)));
        }

        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json) + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "index.html"), Index, new UTF8Encoding(false));
        Validate(output);
        return new(stories.Count, Directory.GetFiles(imagesDir, "*.png").Length, unavailable, errors);
    }

    private static (string? Url, string Status, string? Error, string Hash) EnsureStoryImage(GalleryHost host, StoryInfo story,
        string imagesDir, string repositoryRoot, Dictionary<string, (string? Url, string Status, string? Error, string Hash)> cache)
    {
        if (cache.TryGetValue(story.Path, out var cached)) return cached;
        if (story.RealWindowOnly)
            return cache[story.Path] = (null, "unavailable", "RealWindowOnly story cannot be captured offscreen.", "");
        byte[]? png = FindGolden(story.Path, repositoryRoot) is { } golden ? File.ReadAllBytes(golden) : null;
        string? error = null;
        if (png is null)
        {
            GallerySnapshotResult result = GallerySnapshots.CaptureStory(host, story);
            png = result.Png;
            error = result.Error;
        }
        return StoreStoryImage(story, imagesDir, cache, png, error);
    }

    private static (string? Url, string Status, string? Error, string Hash) StoreStoryImage(StoryInfo story,
        string imagesDir, Dictionary<string, (string? Url, string Status, string? Error, string Hash)> cache,
        byte[]? png, string? error)
    {
        if (cache.TryGetValue(story.Path, out var cached)) return cached;
        if (png is null) return cache[story.Path] = (null, "error", error ?? "Capture failed.", "");
        string file = Slug(story.Path) + ".png";
        File.WriteAllBytes(Path.Combine(imagesDir, file), png);
        string hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        return cache[story.Path] = ($"images/{file}", "captured", null, hash);
    }

    private static string ReplaceEmbeds(string containingStoryPath, string md, IReadOnlyList<DocEmbed> embeds, GalleryHost host,
        string imagesDir, string repositoryRoot,
        Dictionary<string, (string? Url, string Status, string? Error, string Hash)> cache,
        bool browserRuntimeEnabled, ref int unavailable, ref int errors)
    {
        for (int i = 0; i < embeds.Count; i++)
        {
            DocEmbed embed = embeds[i];
            string html;
            if (embed.Kind == DocEmbedKind.ControlApiTable)
                html = ControlApiHtml(embed.Reference, embed.IncludeInherited);
            else if (embed.Kind == DocEmbedKind.TypeApiTable)
                html = TypeApiHtml(embed.Reference);
            else if (browserRuntimeEnabled
                     && embed.Kind == DocEmbedKind.StoryRef
                     && containingStoryPath == BrowserRuntimeEmbedPagePath
                     && embed.Reference == "Examples/3D/Triangle")
                html = RuntimeStory(BrowserRuntimeRoutes["Examples/3D/Triangle"], embedded: true);
            else if (embed.Kind == DocEmbedKind.StoryRef && embed.Reference is { } path && StoryRegistry.Find(path) is { } story)
            {
                var result = EnsureStoryImage(host, story, imagesDir, repositoryRoot, cache);
                if (result.Url is { } url)
                    html = StaticFigure(url, path, "Static embedded story capture",
                        "#story=" + Uri.EscapeDataString(path));
                else
                {
                    html = Unavailable(result.Error ?? "Embedded story unavailable.", result.Status);
                    if (result.Status == "unavailable") unavailable++; else errors++;
                }
            }
            else if (embed.Kind == DocEmbedKind.StoryRef)
            {
                html = Unavailable($"Referenced story was not found: {embed.Reference}", "error");
                errors++;
            }
            else
            {
                GallerySnapshotResult result = GallerySnapshots.CaptureWidget(host, $"doc-embed-{i}", embed.Widget);
                if (result.Png is { } png)
                {
                    string file = $"embed-{Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()[..20]}.png";
                    File.WriteAllBytes(Path.Combine(imagesDir, file), png);
                    html = StaticFigure("images/" + file, $"Documentation embed {i + 1}", "Static widget capture");
                }
                else { html = Unavailable(result.Error ?? "Widget capture failed.", "error"); errors++; }
            }
            md = md.Replace($"```{DocString.UiTypeId}\n{i}\n```", "\n" + html + "\n", StringComparison.Ordinal)
                   .Replace($"[￼]({DocString.InlineScheme}{i})", html, StringComparison.Ordinal);
        }
        return md;
    }

    internal static string RenderMarkdown(string markdown, string storyPath)
    {
        string storyRoute = Uri.EscapeDataString(storyPath);
        markdown = LocalAnchors().Replace(markdown, m =>
            $"[{m.Groups[1].Value}](#story={storyRoute}&section={Uri.EscapeDataString(m.Groups[2].Value)})");
        markdown = StoryLinks().Replace(markdown, m =>
            $"[{m.Groups[1].Value}](#story={Uri.EscapeDataString(m.Groups[2].Value)})");
        return Markdig.Markdown.ToHtml(markdown, Pipeline);
    }

    internal static string ControlApiHtml(string? name, bool inherited)
    {
        ControlApi? api = name is null ? null : ControlApiRegistry.Find(name);
        if (api is null) return Unavailable($"Control API was not found: {name}", "error");
        bool Show(ApiMember member) => inherited || !member.Inherited;
        return ApiHtml(api.Summary,
            ("コンストラクタ引数", api.Members.Where(member => member.Kind == "ctor")),
            ("イベント", api.Members.Where(member => member.Kind == "event" && Show(member))),
            ("パラメータ", api.Members.Where(member => member.Kind == "param" && Show(member))));
    }

    internal static string TypeApiHtml(string? name)
    {
        TypeApi? api = name is null ? null : TypeApiRegistry.Find(name);
        if (api is null) return Unavailable($"Type API was not found: {name}", "error");
        return ApiHtml(api.Summary.Length > 0 ? $"{api.Kind} — {api.Summary}" : "",
            ("コンストラクタ", api.Members.Where(member => member.Kind == "ctor")),
            ("メソッド", api.Members.Where(member => member.Kind == "method")),
            ("プロパティ", api.Members.Where(member => member.Kind == "prop")),
            ("イベント", api.Members.Where(member => member.Kind == "event")),
            (api.Kind == "enum" ? "値" : "フィールド", api.Members.Where(member => member.Kind == "field")));
    }

    private static string ApiHtml(string summary,
        params (string Title, IEnumerable<ApiMember> Members)[] sections)
    {
        var html = new StringBuilder("<section class=\"api-reference\">");
        if (!string.IsNullOrWhiteSpace(summary)) html.Append("<p class=\"api-summary\">").Append(H(summary)).Append("</p>");
        foreach ((string title, IEnumerable<ApiMember> members) in sections)
        {
            ApiMember[] rows = members.ToArray();
            if (rows.Length == 0) continue;
            html.Append("<h3>").Append(H(title)).Append("</h3><div class=\"api-table-wrap\"><table class=\"api-table\"><thead><tr><th scope=\"col\">名前</th><th scope=\"col\">型</th><th scope=\"col\">説明</th></tr></thead><tbody>");
            foreach (ApiMember member in rows)
            {
                string type = member.Stateable ? $"{member.Type} (状態対応)" : member.Type;
                html.Append("<tr><th scope=\"row\"><code>").Append(H(member.Name)).Append("</code></th><td><code>")
                    .Append(H(type)).Append("</code></td><td>").Append(H(member.Description)).Append("</td></tr>");
            }
            html.Append("</tbody></table></div>");
        }
        return html.Append("</section>").ToString();
    }

    private static string RewriteLocalImages(string markdown, string imagesDir, string repositoryRoot)
        => MarkdownImage().Replace(markdown, match =>
        {
            string url = match.Groups[2].Value;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return match.Value;
            string source = Path.GetFullPath(Path.Combine(repositoryRoot, url.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(source))
                return Unavailable($"Referenced documentation image was not found: {url}", "error");
            byte[] bytes = File.ReadAllBytes(source);
            string extension = Path.GetExtension(source).ToLowerInvariant();
            string file = $"asset-{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..20]}{extension}";
            File.WriteAllBytes(Path.Combine(imagesDir, file), bytes);
            return $"{match.Groups[1].Value}images/{file}{match.Groups[3].Value}";
        });

    private static string ReplaceSpecialFences(string md, GalleryHost host, string imagesDir, ref int errors)
    {
        int index = 0, failed = 0;
        string replaced = SpecialFence().Replace(md, match =>
        {
            string kind = match.Groups[1].Value;
            string source = match.Groups[2].Value.Trim();
            Widget widget = kind == "mermaid"
                ? Luxel.Diagram.Factories.DiagramBlock(source, 640f)
                : Luxel.MathText.Factories.MathBlockView(source, maxWidth: 640f);
            GallerySnapshotResult result = GallerySnapshots.CaptureWidget(host, $"{kind}-{index++}", widget, 680, 480);
            if (result.Png is not { } png)
            {
                failed++;
                return Unavailable(result.Error ?? $"{kind} capture failed.", "error");
            }
            string file = $"{kind}-{Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()[..20]}.png";
            File.WriteAllBytes(Path.Combine(imagesDir, file), png);
            return StaticFigure("images/" + file, $"{kind} diagram", $"Static {kind} capture");
        });
        errors += failed;
        return replaced;
    }

    public static void Validate(string output)
    {
        string manifestPath = Path.Combine(output, "manifest.json");
        SiteStory[] manifest = JsonSerializer.Deserialize<SiteStory[]>(File.ReadAllText(manifestPath), Json)
            ?? throw new InvalidDataException("manifest.json is empty.");
        foreach (SiteStory story in manifest)
        {
            RequireRelativeFile(output, story.Fragment, "manifest fragment");
            if (story.Image is { } image) RequireRelativeFile(output, image, "manifest image");
            if (story.Status is not ("captured" or "runtime" or "unavailable" or "error"))
                throw new InvalidDataException($"Unknown capture status '{story.Status}' for {story.Path}.");
            if (story.Status == "runtime")
            {
                if (!BrowserRuntimeRoutes.TryGetValue(story.Path, out BrowserRuntimeRoute? route)
                    || story.Image is not null || story.ImageSha256.Length != 0)
                    throw new InvalidDataException($"Invalid runtime manifest entry for {story.Path}.");
                string fragment = File.ReadAllText(Path.Combine(output, story.Fragment.Replace('/', Path.DirectorySeparatorChar)));
                RequireRuntimeIframe(fragment, story.Fragment, route);
            }
        }

        foreach (string png in Directory.GetFiles(output, "*.png", SearchOption.AllDirectories))
            ValidatePng(png);

        bool foundRuntimeIframe = false;
        foreach (string file in Directory.GetFiles(output, "*.html", SearchOption.AllDirectories))
        {
            string html = File.ReadAllText(file);
            if (html.Contains("<iframe", StringComparison.Ordinal)
                && html.Contains("data-luxel-runtime-story", StringComparison.Ordinal))
            {
                string? storyPath = BrowserRuntimeRoutes.Keys.FirstOrDefault(path => html.Contains($"data-luxel-runtime-story=\"{path}\"", StringComparison.Ordinal));
                if (storyPath is null) throw new InvalidDataException($"Unknown runtime story iframe: {file}");
                RequireRuntimeIframe(html, Path.GetRelativePath(output, file), BrowserRuntimeRoutes[storyPath]);
                foundRuntimeIframe = true;
            }
            if (html.Contains("language-luxel-ui", StringComparison.Ordinal)
                || html.Contains("href=\"luxel-ui:", StringComparison.Ordinal))
                throw new InvalidDataException($"Live placeholder remains: {file}");
            foreach (Match m in LocalReference().Matches(html))
            {
                string value = WebUtility.HtmlDecode(m.Groups[1].Value);
                if (value.StartsWith('#') || value.StartsWith("http:") || value.StartsWith("https:") || value.StartsWith("data:")) continue;
                string path = value.Split('?', '#')[0].Replace('/', Path.DirectorySeparatorChar);
                // Story fragments are fetched into index.html via innerHTML, so browser-relative URLs resolve
                // from the site root, not from the physical stories/ directory containing the fragment file.
                string baseDirectory = Path.GetRelativePath(output, file).StartsWith("stories" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    ? output : Path.GetDirectoryName(file)!;
                string full = Path.GetFullPath(Path.Combine(baseDirectory, path));
                bool exists = File.Exists(full) || Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html"));
                if (!exists) throw new FileNotFoundException($"Missing local reference '{value}' in {file}", full);
            }
        }
        if (foundRuntimeIframe)
            foreach (string relative in BrowserRuntimeRequiredFiles)
                RequireRelativeFile(output, BrowserRuntimeBaseUrl + relative.Replace(Path.DirectorySeparatorChar, '/'), "browser runtime app file");
    }

    private static void RequireRuntimeIframe(string html, string file, BrowserRuntimeRoute route)
    {
        string expected = $"<iframe src=\"{route.Url}\" data-luxel-runtime-story=\"{route.StoryPath}\"";
        if (!html.Contains(expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Runtime story iframe is missing its exact mapped source and story marker: {file}");
        if (html.Contains("src=\"/samples/webgpu-browser/", StringComparison.Ordinal))
            throw new InvalidDataException($"Runtime story iframe uses a root-absolute source: {file}");
    }

    private static void ValidatePng(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using FileStream stream = File.OpenRead(path);
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature) || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException($"Static capture is not a valid PNG: {path}");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
        if (width == 0 || height == 0)
            throw new InvalidDataException($"Static capture has an invalid size {width}x{height}: {path}");
    }

    private static void RequireRelativeFile(string output, string relative, string kind)
    {
        if (Path.IsPathRooted(relative) || Uri.TryCreate(relative, UriKind.Absolute, out _))
            throw new InvalidDataException($"Root/absolute URL is not allowed for {kind}: {relative}");
        string full = Path.GetFullPath(Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Reference escapes output for {kind}: {relative}");
        if (!File.Exists(full)) throw new FileNotFoundException($"Missing {kind}: {relative}", full);
    }

    private static string RuntimeStory(BrowserRuntimeRoute route, bool embedded)
    {
        string heading = embedded ? "Interactive tutorial triangle" : route.Title;
        string modifier = embedded ? " runtime-story-embedded" : "";
        return $"<section class=\"runtime-story{modifier}\" data-runtime-kind=\"webgpu-browser\"><div class=\"runtime-frame\"><iframe src=\"{route.Url}\" data-luxel-runtime-story=\"{H(route.StoryPath)}\" title=\"{H(heading)}\" loading=\"eager\" allow=\"webgpu\"></iframe></div></section>";
    }

    private static void CopyBrowserRuntime(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Browser WebGPU publish root is missing: {source}");
        foreach (string relative in BrowserRuntimeRequiredFiles)
        {
            string required = Path.Combine(source, relative);
            if (!File.Exists(required))
                throw new FileNotFoundException($"Browser WebGPU publish root is incomplete; required app file is missing: {relative}", required);
        }
        CopyDirectory(source, destination);
        foreach (string relative in BrowserRuntimeRequiredFiles)
            if (!File.Exists(Path.Combine(destination, relative)))
                throw new FileNotFoundException($"Copied browser WebGPU app is incomplete: {relative}", Path.Combine(destination, relative));
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Browser WebGPU publish root is missing: {source}");
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopyRuntimeAsset(string relativePath, string destination)
    {
        string suffix = relativePath.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
        System.Reflection.Assembly assembly = typeof(GallerySiteExporter).Assembly;
        string? resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
        if (resource is null)
            throw new FileNotFoundException($"Embedded static Gallery runtime asset is missing: {relativePath}");
        using Stream source = assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"Embedded static Gallery runtime asset cannot be opened: {relativePath}");
        using FileStream target = File.Create(destination);
        source.CopyTo(target);
    }

    public static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (string? dir = start; dir is not null; dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "Luxel.slnx"))) return dir;
        throw new DirectoryNotFoundException("Could not locate Luxel.slnx.");
    }

    private static string? FindGolden(string path, string root)
    {
        string dir = Path.Combine(root, "src", "Luxel.Gallery", "goldens");
        string prefix = GoldenName(path);
        string exact = Path.Combine(dir, prefix + ".vk.png");
        if (File.Exists(exact)) return exact;
        if (!Directory.Exists(dir)) return null;
        string? vulkan = Directory.GetFiles(dir, prefix + ".*.vk.png").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (vulkan is not null) return vulkan;
        string directX = Path.Combine(dir, prefix + ".dx.png");
        if (File.Exists(directX)) return directX;
        return Directory.GetFiles(dir, prefix + ".*.dx.png").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
    }

    public static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }
    private static string GoldenName(string path) { var sb = new StringBuilder(path.Length); foreach (char c in path) sb.Append(char.IsLetterOrDigit(c) ? c : '_'); return sb.ToString(); }
    private static string H(string value) => WebUtility.HtmlEncode(value);
    internal static string BundleHtml(SampleBundleInfo? bundle)
    {
        if (bundle is null) return "<p class=\"sample-level gallery-only\">GalleryOnly — this Story source requires the Gallery harness.</p>";
        string files = string.Join("", bundle.Files.Select(file => $"<li><code>{H(file.Path)}</code> <span>{H(file.Kind.ToString())}</span></li>"));
        string requirements = bundle.Requirements is null ? "" : $"<p><strong>Requirements:</strong> {H(string.Join(" / ", bundle.Requirements))}</p>";
        string command = bundle.RunCommand is null ? "" : $"<p><strong>Run</strong></p><pre><code class=\"language-shell\">{H(bundle.RunCommand)}</code></pre>";
        string smoke = bundle.SmokeCommand is null ? "" : $"<p><strong>Smoke test</strong></p><pre><code class=\"language-shell\">{H(bundle.SmokeCommand)}</code></pre>";
        string platforms = bundle.Platforms is null ? "" : $"<p><strong>Platforms:</strong> {H(string.Join(" / ", bundle.Platforms))}</p>";
        string contract = $"<p><strong>Verification:</strong> exit {bundle.ExpectedExitCode}, timeout {bundle.TimeoutSeconds}s"
            + (bundle.ExpectedStdoutMarker is null ? "" : $", stdout <code>{H(bundle.ExpectedStdoutMarker)}</code>") + "</p>";
        return $"<details class=\"sample-bundle\"><summary>Run this sample — {H(bundle.CopyLevel.ToString())}</summary><p>{H(bundle.Description)}</p>{requirements}{platforms}{contract}<ul>{files}</ul>{command}{smoke}</details>";
    }

    internal static string StorySourceHtml(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        return $"<details class=\"story-source\"><summary>Story source</summary><pre><code class=\"language-csharp\">{H(source)}</code></pre></details>";
    }

    private static string StaticFigure(string url, string alt, string caption, string? href = null)
    {
        string image = $"<img src=\"{H(url)}\" alt=\"{H(alt)}\" loading=\"lazy\">";
        if (href is not null) image = $"<a href=\"{H(href)}\">{image}</a>";
        return $"<figure class=\"static-capture\">{image}<figcaption>{H(caption)} — not interactive</figcaption></figure>";
    }
    private static string Unavailable(string message, string status) => $"<aside class=\"capture-{H(status)}\" data-capture-status=\"{H(status)}\"><strong>Static capture {H(status)}</strong><pre>{H(message)}</pre></aside>";

    [GeneratedRegex(@"(!\[[^\]]*\]\()([^)\s]+)(\))")]
    private static partial Regex MarkdownImage();
    [GeneratedRegex(@"\[([^\]]+)\]\(#([^)]+)\)")]
    private static partial Regex LocalAnchors();
    [GeneratedRegex(@"\[([^\]]+)\]\(story:([^)]+)\)")]
    private static partial Regex StoryLinks();
    [GeneratedRegex(@"```(mermaid|math)\s*\n(.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex SpecialFence();
    [GeneratedRegex("(?:src|href)=\"([^\"]+)\"")]
    private static partial Regex LocalReference();

    internal static string IndexHtml => Index;
    internal static string SiteCss => Css + StorySourceCss;

    internal static string ClientScript => Js;

    private const string Index = """<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><title>Luxel Gallery — Static Captures</title><link rel="stylesheet" href="site.css"><link rel="stylesheet" href="vendor/highlightjs/github-dark.min.css"></head><body><button id="sidebar-toggle" class="floating-toggle" type="button" aria-controls="sidebar" aria-expanded="true">ストーリー</button><button id="review-toggle" class="floating-toggle" type="button" aria-controls="review-panel" aria-expanded="false">フィードバック</button><aside id="sidebar"><header><h1>Luxel Gallery</h1><p>静的HTML版です。API表はHTML、その他の埋め込みは静的キャプチャで、操作はできません。</p><button id="theme" type="button">テーマ切替</button><p><a href="licenses/Box-LICENSE.md">Khronos Box license</a> · <a href="licenses/highlight.js-LICENSE.txt">Highlight.js license</a></p></header><input id="search" type="search" placeholder="Story・見出し・本文を検索"><nav id="stories" aria-label="Stories"></nav></aside><main id="content"><p>Galleryを読み込んでいます…</p></main><aside id="review-panel" aria-label="ギャラリーフィードバック"><header class="review-header"><div class="review-actions" role="toolbar" aria-label="フィードバック操作"><button id="review-prev" class="icon-button" type="button" aria-label="前のコメント" title="前のコメント"><span aria-hidden="true">←</span></button><button id="review-next" class="icon-button" type="button" aria-label="次のコメント" title="次のコメント"><span aria-hidden="true">→</span></button><button id="review-copy" class="icon-button" type="button" aria-label="全件をコピー" title="全件をコピー"><span aria-hidden="true">⧉</span></button><button id="review-download-md" class="icon-button" type="button" aria-label="Markdownを保存" title="Markdownを保存"><span aria-hidden="true">↓</span></button><button id="review-download-json" class="icon-button" type="button" aria-label="JSONをバックアップ" title="JSONをバックアップ"><span aria-hidden="true">⇩</span></button><label class="icon-button button-label" for="review-import" aria-label="JSONを復元" title="JSONを復元"><span aria-hidden="true">⇧</span></label><input id="review-import" type="file" accept="application/json,.json"><button id="review-issue" class="icon-button" type="button" aria-label="この内容でIssueを開く" title="この内容でIssueを開く"><span aria-hidden="true">↗</span></button><button id="review-close" class="icon-button" type="button" aria-label="フィードバックを閉じる" title="閉じる"><span aria-hidden="true">×</span></button></div></header><label class="visually-hidden" for="review-status">状態</label><select id="review-status" aria-label="レビュー状態"><option value="unchecked">未確認</option><option value="reviewed">確認済み</option><option value="needs-change">要修正</option></select><label class="review-comment-label" for="review-comment">フィードバック</label><textarea id="review-comment" rows="10" placeholder="表示を見ながら、気づいた点をここに記録します。"></textarea><p id="review-save-state" class="visually-hidden" role="status" aria-live="polite"></p><textarea id="review-export-fallback" class="review-export-fallback" aria-label="コピー用フィードバック" readonly hidden></textarea></aside><script src="vendor/highlightjs/highlight.min.js"></script><script src="site.js"></script></body></html>""";
    private const string Js = """
const nav=document.querySelector('#stories'),content=document.querySelector('#content'),search=document.querySelector('#search'),theme=document.querySelector('#theme');
const sidebar=document.querySelector('#sidebar'),sidebarToggle=document.querySelector('#sidebar-toggle'),reviewPanel=document.querySelector('#review-panel'),reviewToggle=document.querySelector('#review-toggle'),reviewClose=document.querySelector('#review-close');
const reviewStatus=document.querySelector('#review-status'),reviewComment=document.querySelector('#review-comment'),reviewSaveState=document.querySelector('#review-save-state'),reviewFallback=document.querySelector('#review-export-fallback');
let stories=[],activeReviewPath=null,storageAvailable=true;
const languageAliases={slang:'cpp',hlsl:'cpp',powershell:'shell',pwsh:'shell',csharp:'cs'},openKey='luxel-gallery-tree-open',reviewKey='luxel-gallery-review:v1:'+location.pathname,reviewUiKey='luxel-gallery-review-ui:'+location.pathname,sidebarUiKey='luxel-gallery-sidebar-ui:'+location.pathname;
function highlight(root){if(typeof hljs==='undefined')return;for(const code of root.querySelectorAll('pre code')){const match=[...code.classList].find(x=>x.startsWith('language-'));const requested=match?.slice(9).toLowerCase();const language=languageAliases[requested]||requested;if(language&&hljs.getLanguage(language)){if(match)code.classList.replace(match,'language-'+language)}else if(requested){code.classList.add('no-highlight')}hljs.highlightElement(code)}}
const route=()=>{const p=new URLSearchParams(location.hash.slice(1));return{story:p.get('story')||stories[0]?.path,section:p.get('section')}};
const key=()=>route().story;
const storyHash=path=>'#story='+encodeURIComponent(path);
function safeGet(name){try{return localStorage.getItem(name)}catch{storageAvailable=false;return null}}
function safeSet(name,value){try{localStorage.setItem(name,value);return true}catch{storageAvailable=false;reviewSaveState.textContent='保存できません。コピーまたはJSONバックアップを利用してください。';reviewSaveState.classList.add('error');return false}}
function savedOpen(){try{return new Set(JSON.parse(safeGet(openKey)||'[]'))}catch{return new Set}}
function hasSavedOpen(){return safeGet(openKey)!==null}
function saveOpen(){const paths=[...nav.querySelectorAll('details.tree-folder[open]')].map(x=>x.dataset.path);safeSet(openKey,JSON.stringify(paths))}
function treeFor(items){const root={children:new Map(),story:null};for(const story of items){let node=root;for(const part of story.path.split('/')){if(!node.children.has(part))node.children.set(part,{children:new Map(),story:null});node=node.children.get(part)}node.story=story}return root}
function renderLevel(node,prefix,open,expandAll){const list=document.createElement('ul');list.className='tree-level';for(const [name,child] of node.children){const path=prefix?prefix+'/'+name:name;const item=document.createElement('li');if(child.children.size){const details=document.createElement('details');details.className='tree-folder';details.dataset.path=path;details.open=expandAll||open.has(path)||(!hasSavedOpen()&&!prefix);const summary=document.createElement('summary');summary.textContent=name;details.append(summary,renderLevel(child,path,open,expandAll));if(!expandAll)details.addEventListener('toggle',saveOpen);if(child.story){const own=document.createElement('a');own.href=storyHash(child.story.path);own.dataset.path=child.story.path;own.textContent=name+' — overview';details.prepend(own)}item.append(details)}else if(child.story){const a=document.createElement('a');a.href=storyHash(child.story.path);a.dataset.path=child.story.path;a.textContent=name;item.append(a)}list.append(item)}return list}
function reveal(path){const parts=path.split('/');for(let i=1;i<parts.length;i++){const folder=nav.querySelector('details.tree-folder[data-path="'+CSS.escape(parts.slice(0,i).join('/'))+'"]');if(folder)folder.open=true}}
function setActive(path){for(const a of nav.querySelectorAll('a[data-path]')){const active=a.dataset.path===path;a.classList.toggle('active',active);if(active)a.setAttribute('aria-current','page');else a.removeAttribute('aria-current')}}
function emptyReviewStore(){return{version:1,stories:{}}}
function readReviewStore(){try{const parsed=JSON.parse(safeGet(reviewKey)||'null');return parsed?.version===1&&parsed.stories?parsed:emptyReviewStore()}catch{return emptyReviewStore()}}
function writeReviewStore(store){return safeSet(reviewKey,JSON.stringify(store))}
function currentStoryUrl(path){return location.href.split('#')[0]+storyHash(path)}
function saveCurrentReview(){if(!activeReviewPath)return;const store=readReviewStore(),comment=reviewComment.value,status=reviewStatus.value;if(!comment.trim()&&status==='unchecked')delete store.stories[activeReviewPath];else store.stories[activeReviewPath]={status,comment,url:currentStoryUrl(activeReviewPath),updatedAt:new Date().toISOString()};if(writeReviewStore(store)){reviewSaveState.textContent='この端末に保存しました · '+new Date().toLocaleTimeString();reviewSaveState.classList.remove('error')}updateReviewCounts()}
function loadReview(path){activeReviewPath=path;const note=readReviewStore().stories[path];reviewStatus.value=note?.status||'unchecked';reviewComment.value=note?.comment||'';reviewSaveState.textContent=storageAvailable?'':'保存できません。コピーまたはJSONバックアップを利用してください。';updateReviewCounts()}
function annotatedStories(){const notes=readReviewStore().stories;return stories.filter(story=>notes[story.path]&&(notes[story.path].comment.trim()||notes[story.path].status!=='unchecked'))}
function updateReviewCounts(){const count=annotatedStories().length;reviewToggle.textContent=count?'フィードバック ('+count+')':'フィードバック'}
function navigateAnnotated(delta){saveCurrentReview();const items=annotatedStories();if(!items.length)return;let index=items.findIndex(x=>x.path===activeReviewPath);if(index<0)index=delta>0?-1:0;location.hash=storyHash(items[(index+delta+items.length)%items.length].path)}
function statusLabel(status){return status==='reviewed'?'確認済み':status==='needs-change'?'要修正':'未確認'}
function reviewMarkdown(){saveCurrentReview();const store=readReviewStore(),items=stories.filter(x=>store.stories[x.path]);const lines=['# Luxel Gallery feedback','',`Gallery: ${location.href.split('#')[0]}`,''];for(const story of items){const note=store.stories[story.path];if(!note.comment.trim()&&note.status==='unchecked')continue;lines.push('## '+story.path,'',`- 状態: ${statusLabel(note.status)}`,`- URL: ${note.url||currentStoryUrl(story.path)}`,'',note.comment.trim()||'（コメントなし）','')}if(lines.length===4)lines.push('フィードバックはまだありません。','');return lines.join('\n')}
function download(name,text,type){const blob=new Blob([text],{type}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),0)}
async function copyReviews(){const markdown=reviewMarkdown();try{await navigator.clipboard.writeText(markdown);reviewSaveState.textContent='全フィードバックをコピーしました。';reviewFallback.hidden=true}catch{reviewFallback.value=markdown;reviewFallback.hidden=false;reviewFallback.focus();reviewFallback.select();reviewSaveState.textContent='自動コピーできないため、下の欄を選択しました。'}}
function openIssue(){saveCurrentReview();const note=readReviewStore().stories[activeReviewPath]||{status:'unchecked',comment:''};const title='Gallery feedback: '+activeReviewPath;const body=['## Story','',activeReviewPath,'','## Status','',statusLabel(note.status),'','## Gallery URL','',currentStoryUrl(activeReviewPath),'','## Feedback','',note.comment.trim()||'（コメントを記入してください）'].join('\n');open('https://github.com/ikihiki/luxel/issues/new?title='+encodeURIComponent(title)+'&body='+encodeURIComponent(body),'_blank','noopener')}
function syncVisualViewport(){const viewport=window.visualViewport,height=viewport?.height||innerHeight,offset=viewport?.offsetTop||0,inset=Math.max(0,document.documentElement.clientHeight-height-offset);document.documentElement.style.setProperty('--visual-viewport-height',height+'px');document.documentElement.style.setProperty('--keyboard-inset',inset+'px')}function setReviewOpen(open){document.body.classList.toggle('review-open',open);reviewToggle.setAttribute('aria-expanded',String(open));reviewPanel.setAttribute('aria-hidden',String(!open));safeSet(reviewUiKey,open?'open':'closed');if(!open&&reviewPanel.contains(document.activeElement))reviewToggle.focus()}
function setSidebarOpen(open){document.body.classList.toggle('sidebar-collapsed',!open);sidebarToggle.setAttribute('aria-expanded',String(open));sidebar.setAttribute('aria-hidden',String(!open));safeSet(sidebarUiKey,open?'open':'closed')}
async function show(){saveCurrentReview();const requested=key();const s=stories.find(x=>x.path===requested||(x.aliases||[]).includes(requested))||stories[0];if(!s)return;const r=await fetch(s.fragment);content.innerHTML=r.ok?await r.text():'<p>ページを読み込めませんでした。</p>';document.body.classList.toggle('runtime-active',!!content.querySelector('.runtime-page'));highlight(content);setActive(s.path);reveal(s.path);loadReview(s.path);document.title=s.path+' — Luxel Gallery';const section=route().section;if(section)requestAnimationFrame(()=>document.getElementById(section)?.scrollIntoView());else content.scrollTo?.(0,0);if(matchMedia('(max-width: 1180px)').matches)setSidebarOpen(false)}
function draw(q=''){nav.innerHTML='';const needle=q.trim().toLowerCase();const filtered=stories.filter(x=>(x.searchText||x.path).toLowerCase().includes(needle));nav.append(renderLevel(treeFor(filtered),'',savedOpen(),needle.length>0));const current=key();if(current){setActive(current);reveal(current)}}
const saved=safeGet('luxel-gallery-theme');if(saved==='light')document.documentElement.dataset.theme='light';
theme.addEventListener('click',()=>{const light=document.documentElement.dataset.theme!=='light';document.documentElement.dataset.theme=light?'light':'';safeSet('luxel-gallery-theme',light?'light':'dark')});
sidebarToggle.addEventListener('click',()=>setSidebarOpen(document.body.classList.contains('sidebar-collapsed')));reviewToggle.addEventListener('click',()=>setReviewOpen(!document.body.classList.contains('review-open')));reviewClose.addEventListener('click',()=>setReviewOpen(false));
reviewComment.addEventListener('input',saveCurrentReview);reviewComment.addEventListener('focus',()=>{document.body.classList.add('review-keyboard');syncVisualViewport()});reviewComment.addEventListener('blur',()=>{document.body.classList.remove('review-keyboard');syncVisualViewport()});reviewStatus.addEventListener('change',saveCurrentReview);document.querySelector('#review-prev').addEventListener('click',()=>navigateAnnotated(-1));document.querySelector('#review-next').addEventListener('click',()=>navigateAnnotated(1));document.querySelector('#review-copy').addEventListener('click',copyReviews);document.querySelector('#review-download-md').addEventListener('click',()=>download('luxel-gallery-feedback.md',reviewMarkdown(),'text/markdown;charset=utf-8'));document.querySelector('#review-download-json').addEventListener('click',()=>{saveCurrentReview();download('luxel-gallery-feedback.json',JSON.stringify(readReviewStore(),null,2)+'\n','application/json')});document.querySelector('#review-issue').addEventListener('click',openIssue);
document.querySelector('#review-import').addEventListener('change',async event=>{const file=event.target.files?.[0];if(!file)return;try{const data=JSON.parse(await file.text());if(data?.version!==1||!data.stories||Array.isArray(data.stories))throw new Error('invalid');if(!writeReviewStore(data))return;loadReview(activeReviewPath);reviewSaveState.textContent='JSONバックアップを復元しました。'}catch{reviewSaveState.textContent='このJSONバックアップは復元できません。';reviewSaveState.classList.add('error')}finally{event.target.value=''}});
addEventListener('pagehide',saveCurrentReview);addEventListener('hashchange',show);addEventListener('resize',syncVisualViewport);window.visualViewport?.addEventListener('resize',syncVisualViewport);window.visualViewport?.addEventListener('scroll',syncVisualViewport);search.addEventListener('input',()=>draw(search.value));
syncVisualViewport();const initialReview=safeGet(reviewUiKey),initialSidebar=safeGet(sidebarUiKey);setReviewOpen(initialReview?initialReview==='open':matchMedia('(orientation: landscape) and (min-width: 800px)').matches);setSidebarOpen(initialSidebar?initialSidebar==='open':!matchMedia('(max-width: 1180px)').matches);
fetch('manifest.json').then(r=>r.json()).then(x=>{stories=x;draw();show()}).catch(()=>content.innerHTML='<p>manifest.jsonを読み込めませんでした。</p>');
""";
    private const string StorySourceCss = """
.story-source{margin-top:28px;border-top:1px solid var(--line);padding-top:14px}.story-source summary{cursor:pointer;color:var(--link);font-weight:650;padding:8px 0}.story-source pre{max-width:100%;overflow-x:auto;background:var(--code);border:1px solid var(--line);border-radius:8px;padding:14px}.story-source code{white-space:pre;font:13px/1.55 ui-monospace,SFMono-Regular,Consolas,monospace}
.runtime-story{width:100%;height:100%;margin:0}.runtime-frame,.runtime-frame iframe{display:block;width:100%;height:100%;margin:0;padding:0;border:0;background:#10151d}.runtime-frame{overflow:hidden}.runtime-page{width:100%;max-width:none;height:100%;margin:0}.runtime-story-embedded{height:min(70vh,720px);min-height:360px;margin:18px 0 28px}
body.runtime-active main{padding:0;overflow:hidden}body.runtime-active main>.runtime-page{height:100%}
@media(max-width:600px){.runtime-story-embedded{height:60vh;min-height:280px;margin:12px 0 22px}}
""";
    private const string Css = """
:root{color-scheme:dark;--bg:#10131a;--panel:#171b24;--text:#e7eaf0;--muted:#9eabc1;--line:#303746;--link:#84b8ff;--code:#090b10;--active:#283044;--danger:#ffaaa2;background:var(--bg);color:var(--text);font:15px/1.65 system-ui,sans-serif}:root[data-theme=light]{color-scheme:light;--bg:#f7f8fb;--panel:#fff;--text:#1d2430;--muted:#657086;--line:#d8dde7;--link:#155fc4;--code:#eef1f6;--active:#e2e9f6;--danger:#a42820}*{box-sizing:border-box}html,body{height:100%;overflow:hidden;background:var(--bg)}body{margin:0;display:grid;grid-template-columns:310px minmax(0,1fr);height:100vh;height:100dvh;color:var(--text)}body.review-open{grid-template-columns:310px minmax(0,1fr) minmax(320px,390px)}button,select,textarea,input{font:inherit}button,.button-label,select{min-height:44px;color:inherit;background:var(--panel);border:1px solid var(--line);border-radius:8px;cursor:pointer}.floating-toggle{position:fixed;z-index:30;top:max(8px,env(safe-area-inset-top));padding:7px 12px;box-shadow:0 2px 12px #0005}#sidebar-toggle{left:max(8px,env(safe-area-inset-left))}#review-toggle{right:max(8px,env(safe-area-inset-right))}#sidebar{position:sticky;top:0;height:100vh;height:100dvh;overflow:auto;padding:64px 20px 20px max(20px,env(safe-area-inset-left));border-right:1px solid var(--line);background:var(--panel);z-index:20}body.sidebar-collapsed{grid-template-columns:minmax(0,1fr)}body.sidebar-collapsed.review-open{grid-template-columns:minmax(0,1fr) minmax(320px,390px)}body.sidebar-collapsed #sidebar{display:none}#sidebar h1{font-size:20px;margin-bottom:0}#sidebar h2{font-size:12px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);margin:18px 8px 4px}#theme{padding:6px 10px;margin-bottom:12px;background:transparent}#search{width:100%;min-height:44px;padding:9px;background:var(--bg);color:inherit;border:1px solid var(--line);border-radius:8px}.tree-level{list-style:none;padding-left:14px;margin:4px 0}.tree-level:first-child{padding-left:0}.tree-level li{margin:2px 0}.tree-folder>summary{cursor:pointer;padding:8px;border-radius:6px;font-weight:650}.tree-folder>a,.tree-level a{display:block;color:var(--text);text-decoration:none;padding:8px;border-radius:6px}.tree-folder>a{margin-left:18px}.tree-level a:hover,.tree-level a.active,.tree-folder>summary:hover{background:var(--active)}main{min-width:0;height:100vh;height:100dvh;padding:64px clamp(20px,4vw,64px) 64px;overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain}.story{max-width:1040px;margin:auto}.story h1{font-size:clamp(28px,4vw,46px);line-height:1.15}.story h2{margin-top:38px;border-bottom:1px solid var(--line)}.story h3{margin-top:28px}.story a{color:var(--link)}.story img{display:block;max-width:100%;height:auto;margin:auto;border:1px solid var(--line);border-radius:8px}.static-badge,.sample-level{color:var(--muted);font-weight:650}.static-capture figcaption{color:var(--muted);text-align:center}.capture-unavailable,.capture-error{border:1px solid var(--line);border-left:4px solid #d77;padding:18px;border-radius:8px}.capture-unavailable pre,.capture-error pre{white-space:pre-wrap}.story pre{max-width:100%;overflow:auto;background:var(--code);padding:14px;border-radius:8px}.story code{font-family:ui-monospace,SFMono-Regular,Consolas,monospace}.story table{display:block;max-width:100%;overflow:auto;border-collapse:collapse}.story th,.story td{padding:7px 10px;border:1px solid var(--line);vertical-align:top}.story blockquote{margin-left:0;padding-left:16px;border-left:3px solid var(--line);color:var(--muted)}.api-table code{white-space:nowrap}.api-anchor{opacity:.55;text-decoration:none}.sample-bundle{margin-top:24px;border:1px solid var(--line);border-radius:10px;padding:10px 14px}.sample-bundle summary{cursor:pointer;color:var(--link);font-weight:700}.sample-bundle li{margin:4px 0}.sample-bundle li span{color:var(--muted)}
#review-panel{display:none;position:sticky;top:0;height:100vh;height:100dvh;overflow:hidden;padding:16px max(14px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) 14px;border-left:1px solid var(--line);background:var(--panel);z-index:15}body.review-open #review-panel{display:flex;flex-direction:column}body.review-open #review-toggle{display:none}.review-header{flex:none;margin-bottom:4px}.review-actions{display:flex;flex-wrap:nowrap;align-items:center;width:100%;gap:4px}.review-actions>.icon-button{flex:1 1 0}.icon-button{display:flex;align-items:center;justify-content:center;min-width:0;min-height:40px;padding:0;font-size:20px;line-height:1;background:transparent}.icon-button:hover,.icon-button:focus-visible{background:var(--active)}.button-label{cursor:pointer}.visually-hidden{position:absolute!important;width:1px!important;height:1px!important;padding:0!important;margin:-1px!important;overflow:hidden!important;clip:rect(0,0,0,0)!important;white-space:nowrap!important;border:0!important}#review-status,#review-comment{width:100%}#review-status{min-height:36px;padding:4px 9px;font-size:13px}.review-comment-label{display:block;margin:3px 0 2px;color:var(--muted);font-size:10px;font-weight:650;line-height:1.1}#review-comment{flex:1 1 160px;min-height:80px;padding:10px;resize:none;color:inherit;background:var(--bg);border:1px solid var(--line);border-radius:8px}.review-save-state.error{color:var(--danger)}#review-import{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0,0,0,0)}.review-export-fallback{width:100%;min-height:100px;margin-top:6px;background:var(--bg);color:inherit}.review-export-fallback[hidden]{display:none}

@media(max-width:1180px){body,body.review-open,body.sidebar-collapsed,body.sidebar-collapsed.review-open{grid-template-columns:minmax(0,1fr) minmax(320px,390px)}body:not(.review-open),body.sidebar-collapsed:not(.review-open){grid-template-columns:minmax(0,1fr)}#sidebar{display:block;position:fixed;left:0;width:min(86vw,340px);box-shadow:8px 0 28px #0008}body.sidebar-collapsed #sidebar{display:none}}
@media(max-width:820px),(orientation:portrait) and (max-width:1024px){body,body.review-open,body.sidebar-collapsed,body.sidebar-collapsed.review-open{display:block}main{padding:64px 18px 96px}body.review-open main{padding-bottom:calc(min(44dvh,340px) + 28px)}#review-panel{position:fixed;left:0;right:0;bottom:var(--keyboard-inset,0px);top:auto;width:100%;height:min(44vh,340px);height:min(44dvh,340px);max-height:calc(var(--visual-viewport-height,100dvh) - 12px);border-left:0;border-top:1px solid var(--line);border-radius:14px 14px 0 0;box-shadow:0 -8px 24px #0008;padding:8px max(10px,env(safe-area-inset-right)) max(8px,env(safe-area-inset-bottom)) max(10px,env(safe-area-inset-left))}.review-actions{display:flex;flex-wrap:nowrap;gap:4px}.icon-button{min-height:36px;font-size:18px}#review-status{min-height:32px}.review-comment-label{margin-top:3px}#review-comment{min-height:58px}.review-save-state{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}body.review-keyboard #review-panel{height:min(34vh,220px);height:min(calc(var(--visual-viewport-height,100dvh)*.38),220px)}body.review-keyboard main{padding-bottom:calc(min(34dvh,220px) + 20px)}}
@media(max-width:520px){.review-actions{display:flex;flex-wrap:nowrap}.icon-button{min-height:34px;font-size:17px}}
@media(prefers-reduced-motion:no-preference){#review-panel{animation:review-in .16s ease-out}@keyframes review-in{from{opacity:.7;transform:translateY(10px)}to{opacity:1;transform:none}}}
""";
}
