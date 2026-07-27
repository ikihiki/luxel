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
    string Status, string? Error, string ImageSha256, string SearchText, IReadOnlyList<string> Aliases);

public static partial class GallerySiteExporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static SiteExportReport Export(GalleryHost host, IReadOnlyList<StoryInfo> stories, string output, string repositoryRoot)
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
        File.WriteAllText(Path.Combine(output, "site.css"), Css, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "site.js"), Js, new UTF8Encoding(false));

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

            if (story.RealWindowOnly)
            {
                status = "unavailable";
                error = "This story requires a real window and is not available in the static gallery.";
            }
            else
            {
                (imageUrl, status, error, imageHash) = EnsureStoryImage(host, story, imagesDir, repositoryRoot, imageCache);
                try
                {
                    host.SelectExact(story.Path);
                    GallerySnapshots.Stabilize(host);
                    document = GallerySnapshots.FindDocument(host.CurrentRoot);
                }
                catch (Exception e) when (imageUrl is not null)
                {
                    // A checked-in Vulkan golden is still a valid canonical static preview even if optional
                    // runtime assets needed to realize the live story are absent in the exporter environment.
                    error = $"Live realization unavailable; canonical golden used. {e.GetType().Name}: {e.Message}";
                }
                catch (Exception e)
                {
                    status = "error";
                    error = $"{e.GetType().Name}: {e.Message}";
                }
            }

            string body;
            if (document is not null)
            {
                IReadOnlyList<string> linkErrors = DocsIndex.ValidateLinks(story.Path, document.DocSource!);
                if (linkErrors.Count > 0)
                    throw new InvalidDataException("Broken documentation links: " + string.Join(", ", linkErrors));
                string md = ReplaceEmbeds(document.DocSource!, document.DocEmbeds, host, imagesDir, repositoryRoot,
                    imageCache, ref unavailable, ref errors);
                md = RewriteLocalImages(md, imagesDir, repositoryRoot);
                md = ReplaceSpecialFences(md, host, imagesDir, ref errors);
                md = StoryLinks().Replace(md, m => $"[{m.Groups[1].Value}](#story={Uri.EscapeDataString(m.Groups[2].Value)})");
                body = Markdig.Markdown.ToHtml(md, Pipeline);
            }
            else if (imageUrl is not null)
                body = StaticFigure(imageUrl, story.Path, "Static story capture");
            else
                body = Unavailable(error ?? "No static capture is available.", status);

            string fragment = $"<article class=\"story\"><header><p class=\"static-badge\">Static capture — not interactive</p><h1>{H(story.Path)}</h1></header>{body}</article>";
            File.WriteAllText(Path.Combine(output, fragmentUrl.Replace('/', Path.DirectorySeparatorChar)), fragment, new UTF8Encoding(false));
            if (status == "unavailable") unavailable++;
            if (status == "error") errors++;
            string searchText = story.Path + "\n" + story.Name + "\n" + story.Component
                + (document?.DocSource is { } source ? "\n" + source : "")
                + (story.Source is { Length: > 0 } code ? "\n" + code : "");
            manifest.Add(new SiteStory(story.Path, story.Name, story.Component, fragmentUrl, imageUrl, status, error,
                imageHash, searchText, StoryRegistry.AliasesFor(story.Path)));
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
        if (png is null) return cache[story.Path] = (null, "error", error ?? "Capture failed.", "");
        string file = Slug(story.Path) + ".png";
        File.WriteAllBytes(Path.Combine(imagesDir, file), png);
        string hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        return cache[story.Path] = ($"images/{file}", "captured", null, hash);
    }

    private static string ReplaceEmbeds(string md, IReadOnlyList<DocEmbed> embeds, GalleryHost host, string imagesDir,
        string repositoryRoot, Dictionary<string, (string? Url, string Status, string? Error, string Hash)> cache,
        ref int unavailable, ref int errors)
    {
        for (int i = 0; i < embeds.Count; i++)
        {
            DocEmbed embed = embeds[i];
            string html;
            if (embed.Kind == DocEmbedKind.StoryRef && embed.Reference is { } path && StoryRegistry.Find(path) is { } story)
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
            if (story.Status is not ("captured" or "unavailable" or "error"))
                throw new InvalidDataException($"Unknown capture status '{story.Status}' for {story.Path}.");
        }

        foreach (string png in Directory.GetFiles(output, "*.png", SearchOption.AllDirectories))
            ValidatePng(png);

        foreach (string file in Directory.GetFiles(output, "*.html", SearchOption.AllDirectories))
        {
            string html = File.ReadAllText(file);
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
                if (!File.Exists(full)) throw new FileNotFoundException($"Missing local reference '{value}' in {file}", full);
            }
        }
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
    private static string StaticFigure(string url, string alt, string caption, string? href = null)
    {
        string image = $"<img src=\"{H(url)}\" alt=\"{H(alt)}\" loading=\"lazy\">";
        if (href is not null) image = $"<a href=\"{H(href)}\">{image}</a>";
        return $"<figure class=\"static-capture\">{image}<figcaption>{H(caption)} — not interactive</figcaption></figure>";
    }
    private static string Unavailable(string message, string status) => $"<aside class=\"capture-{H(status)}\" data-capture-status=\"{H(status)}\"><strong>Static capture {H(status)}</strong><pre>{H(message)}</pre></aside>";

    [GeneratedRegex(@"(!\[[^\]]*\]\()([^)\s]+)(\))")]
    private static partial Regex MarkdownImage();
    [GeneratedRegex(@"\[([^\]]+)\]\(story:([^)]+)\)")]
    private static partial Regex StoryLinks();
    [GeneratedRegex(@"```(mermaid|math)\s*\n(.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex SpecialFence();
    [GeneratedRegex("(?:src|href)=\"([^\"]+)\"")]
    private static partial Regex LocalReference();

    private const string Index = """<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Luxel Gallery — Static Captures</title><link rel="stylesheet" href="site.css"><link rel="stylesheet" href="vendor/highlightjs/github-dark.min.css"></head><body><aside id="sidebar"><header><h1>Luxel Gallery</h1><p>静的HTML版です。埋め込みはPNGで、操作はできません。</p><button id="theme" type="button">テーマ切替</button><p><a href="licenses/Box-LICENSE.md">Khronos Box license</a> · <a href="licenses/highlight.js-LICENSE.txt">Highlight.js license</a></p></header><input id="search" type="search" placeholder="Story・見出し・本文を検索"><nav id="stories" aria-label="Stories"></nav></aside><main id="content"><p>Galleryを読み込んでいます…</p></main><script src="vendor/highlightjs/highlight.min.js"></script><script src="site.js"></script></body></html>""";
    private const string Js = """const nav=document.querySelector('#stories'),content=document.querySelector('#content'),search=document.querySelector('#search'),theme=document.querySelector('#theme');let stories=[];const languageAliases={slang:'cpp',hlsl:'cpp',powershell:'shell',pwsh:'shell',csharp:'cs'},openKey='luxel-gallery-tree-open';function highlight(root){if(typeof hljs==='undefined')return;for(const code of root.querySelectorAll('pre code')){const match=[...code.classList].find(x=>x.startsWith('language-'));const requested=match?.slice(9).toLowerCase();const language=languageAliases[requested]||requested;if(language&&hljs.getLanguage(language)){if(match)code.classList.replace(match,'language-'+language)}else if(requested){code.classList.add('no-highlight')}hljs.highlightElement(code)}}const key=()=>{const m=location.hash.match(/story=([^&]+)/);return m?decodeURIComponent(m[1]):stories[0]?.path};function savedOpen(){try{return new Set(JSON.parse(localStorage.getItem(openKey)||'[]'))}catch{return new Set}}function hasSavedOpen(){return localStorage.getItem(openKey)!==null}function saveOpen(){const paths=[...nav.querySelectorAll('details.tree-folder[open]')].map(x=>x.dataset.path);localStorage.setItem(openKey,JSON.stringify(paths))}function treeFor(items){const root={children:new Map(),story:null};for(const story of items){let node=root;for(const part of story.path.split('/')){if(!node.children.has(part))node.children.set(part,{children:new Map(),story:null});node=node.children.get(part)}node.story=story}return root}function renderLevel(node,prefix,open,expandAll){const list=document.createElement('ul');list.className='tree-level';for(const [name,child] of node.children){const path=prefix?prefix+'/'+name:name;const item=document.createElement('li');if(child.children.size){const details=document.createElement('details');details.className='tree-folder';details.dataset.path=path;details.open=expandAll||open.has(path)||(!hasSavedOpen()&&!prefix);const summary=document.createElement('summary');summary.textContent=name;details.append(summary,renderLevel(child,path,open,expandAll));if(!expandAll)details.addEventListener('toggle',saveOpen);if(child.story){const own=document.createElement('a');own.href='#story='+encodeURIComponent(child.story.path);own.dataset.path=child.story.path;own.textContent=name+' — overview';details.prepend(own)}item.append(details)}else if(child.story){const a=document.createElement('a');a.href='#story='+encodeURIComponent(child.story.path);a.dataset.path=child.story.path;a.textContent=name;item.append(a)}list.append(item)}return list}function reveal(path){const parts=path.split('/');for(let i=1;i<parts.length;i++){const folder=nav.querySelector('details.tree-folder[data-path="'+CSS.escape(parts.slice(0,i).join('/'))+'"]');if(folder)folder.open=true}}function setActive(path){for(const a of nav.querySelectorAll('a[data-path]')){const active=a.dataset.path===path;a.classList.toggle('active',active);if(active)a.setAttribute('aria-current','page');else a.removeAttribute('aria-current')}}async function show(){const requested=key();const s=stories.find(x=>x.path===requested||(x.aliases||[]).includes(requested))||stories[0];if(!s)return;const r=await fetch(s.fragment);content.innerHTML=r.ok?await r.text():'<p>ページを読み込めませんでした。</p>';highlight(content);setActive(s.path);reveal(s.path);document.title=s.path+' — Luxel Gallery';scrollTo(0,0)}function draw(q=''){nav.innerHTML='';const needle=q.trim().toLowerCase();const filtered=stories.filter(x=>(x.searchText||x.path).toLowerCase().includes(needle));nav.append(renderLevel(treeFor(filtered),'',savedOpen(),needle.length>0));const current=key();if(current){setActive(current);reveal(current)}}const saved=localStorage.getItem('luxel-gallery-theme');if(saved==='light')document.documentElement.dataset.theme='light';theme.addEventListener('click',()=>{const light=document.documentElement.dataset.theme!=='light';document.documentElement.dataset.theme=light?'light':'';localStorage.setItem('luxel-gallery-theme',light?'light':'dark')});fetch('manifest.json').then(r=>r.json()).then(x=>{stories=x;draw();show()}).catch(()=>content.innerHTML='<p>manifest.jsonを読み込めませんでした。</p>');addEventListener('hashchange',show);search.addEventListener('input',()=>draw(search.value));""";
    private const string Css = """:root{color-scheme:dark;--bg:#10131a;--panel:#171b24;--text:#e7eaf0;--muted:#9eabc1;--line:#303746;--link:#84b8ff;--code:#090b10;--active:#283044;background:var(--bg);color:var(--text);font:15px/1.65 system-ui,sans-serif}:root[data-theme=light]{color-scheme:light;--bg:#f7f8fb;--panel:#fff;--text:#1d2430;--muted:#657086;--line:#d8dde7;--link:#155fc4;--code:#eef1f6;--active:#e2e9f6}*{box-sizing:border-box}body{margin:0;display:grid;grid-template-columns:310px 1fr;min-height:100vh;background:var(--bg);color:var(--text)}#sidebar{position:sticky;top:0;height:100vh;overflow:auto;padding:20px;border-right:1px solid var(--line);background:var(--panel)}#sidebar h1{font-size:20px;margin-bottom:0}#sidebar h2{font-size:12px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);margin:18px 8px 4px}#theme{padding:6px 10px;margin-bottom:12px;color:inherit;background:transparent;border:1px solid var(--line);border-radius:6px;cursor:pointer}#search{width:100%;padding:9px;background:var(--bg);color:inherit;border:1px solid var(--line);border-radius:6px}nav .tree-level{list-style:none;margin:0;padding-left:14px}nav>.tree-level{padding-left:0}nav li{margin:1px 0}nav details>summary{cursor:pointer;padding:5px 6px;border-radius:6px;color:var(--muted);font-weight:650;user-select:none}nav details>summary:hover{background:var(--active);color:var(--text)}nav details>summary::marker{color:var(--link)}nav a{display:block;padding:5px 8px 5px 21px;border-radius:6px;color:var(--text);text-decoration:none;overflow-wrap:anywhere}nav a:hover,nav a.active{background:var(--active)}nav a.active{color:var(--link);font-weight:700}main{min-width:0;width:100%;max-width:1100px;padding:32px 48px}.story h1{line-height:1.2}.static-badge,figcaption{color:var(--muted)}.static-badge{display:inline-block;border:1px solid var(--muted);border-radius:99px;padding:2px 10px}.static-capture{margin:24px 0;border:1px solid var(--line);background:var(--panel);padding:12px;border-radius:9px}.static-capture img{display:block;max-width:100%;height:auto;margin:auto}.static-capture a{display:block}.static-capture figcaption{padding-top:8px}.capture-unavailable,.capture-error{border-left:4px solid #d19520;background:#fff4d6;color:#342b16;padding:12px;margin:18px 0}.capture-error{border-color:#d33d50;background:#ffe4e8;color:#3b171d}pre{white-space:pre-wrap;overflow:auto;background:var(--code);padding:12px;border-radius:6px}pre code.hljs{white-space:pre;overflow:visible;padding:0;background:transparent}code{color:inherit}a{color:var(--link)}table{border-collapse:collapse;max-width:100%}th,td{border:1px solid var(--line);padding:6px 9px}@media(max-width:760px){body{display:block}#sidebar{position:relative;height:auto;border-right:0;border-bottom:1px solid var(--line)}main{padding:22px}}""";
}
