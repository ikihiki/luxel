using System.Net;
using System.Reflection;
using System.Text;
using Luxel.Scripting;

namespace Luxel.Gallery.Playground;

public static class PlaygroundWorkspace
{
    public static string Render(PlaygroundState state, string id = "luxel-playground")
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string safeId = H(id);
        PlaygroundFile main = state.Draft.MainFile;
        int entryIndex = state.Draft.Files.Select((file, index) => (file, index)).First(pair => ReferenceEquals(pair.file, main) || pair.file == main).index;
        string entryFileId = FileId(entryIndex);
        var html = new StringBuilder();
        html.Append("<section class=\"luxel-playground\" id=\"").Append(safeId)
            .Append("\" data-playground data-workspace-schema=\"2\" data-template-id=\"").Append(H(state.Draft.TemplateId))
            .Append("\" data-entry-file-id=\"").Append(entryFileId).Append("\" data-active-file-id=\"").Append(entryFileId)
            .Append("\" data-execution-id=\"").Append(state.ExecutionId).Append("\" aria-labelledby=\"")
            .Append(safeId).Append("-title\">");
        html.Append("<header class=\"playground-header\"><div><h1 id=\"").Append(safeId).Append("-title\">")
            .Append(H(state.Draft.Title)).Append("</h1><p>Edit and run this example in your browser.</p></div>")
            .Append("<p class=\"playground-status\" role=\"status\" aria-live=\"polite\" data-playground-status>")
            .Append(H(state.StatusText)).Append("</p></header>");
        html.Append("<div class=\"playground-actions\" role=\"toolbar\" aria-label=\"Playground actions\">")
            .Append("<button type=\"button\" data-playground-run").Append(state.CanRun ? "" : " disabled").Append(">Run</button>")
            .Append("<button type=\"button\" data-playground-cancel").Append(state.CanCancel ? "" : " disabled").Append(">Stop</button>")
            .Append("<button type=\"button\" data-playground-reset>Reset</button></div>");
        html.Append("<div class=\"playground-grid\"><section class=\"playground-editor\" aria-labelledby=\"")
            .Append(safeId).Append("-source-heading\"><h2 id=\"").Append(safeId).Append("-source-heading\">Source files</h2>")
            .Append("<div class=\"playground-workspace\"><nav class=\"playground-files\" aria-label=\"Workspace files\">")
            .Append("<div class=\"playground-file-actions\" role=\"toolbar\" aria-label=\"File actions\">")
            .Append("<button type=\"button\" data-playground-file-add>Add file</button>")
            .Append("<button type=\"button\" data-playground-file-rename>Rename</button>")
            .Append("<button type=\"button\" data-playground-file-delete>Delete</button></div>")
            .Append("<div class=\"playground-file-list\" role=\"tablist\" aria-label=\"Open files\" data-playground-file-list>");
        for (int index = 0; index < state.Draft.Files.Count; index++)
        {
            PlaygroundFile file = state.Draft.Files[index];
            string fileId = FileId(index);
            html.Append("<button type=\"button\" role=\"tab\" data-playground-file-select data-file-id=\"").Append(fileId)
                .Append("\" aria-selected=\"").Append(fileId == entryFileId ? "true" : "false").Append("\">")
                .Append(H(file.FileName)).Append(fileId == entryFileId ? " <span aria-label=\"entry file\">●</span>" : "").Append("</button>");
        }
        html.Append("</div></nav><div class=\"playground-file-editor\">")
            .Append("<label data-playground-active-file-label for=\"").Append(safeId).Append("-source\">").Append(H(main.FileName)).Append("</label>")
            .Append("<p class=\"playground-language-service\" data-playground-language-service>Monaco C# · Browser Roslyn completion, hover, and live diagnostics</p>")
            .Append("<div class=\"playground-editor-host\" data-playground-editor-host>")
            .Append("<div class=\"playground-monaco\" data-playground-monaco aria-label=\"").Append(H(main.FileName)).Append(" code editor\"></div>");
        for (int index = 0; index < state.Draft.Files.Count; index++)
        {
            PlaygroundFile file = state.Draft.Files[index];
            html.Append("<textarea");
            if (index == entryIndex) html.Append(" id=\"").Append(safeId).Append("-source\"");
            html.Append(" data-playground-source data-file-id=\"").Append(FileId(index)).Append("\" data-file-name=\"")
                .Append(H(file.FileName)).Append("\" data-file-language=\"").Append(FileLanguage(file.FileName))
                .Append("\" spellcheck=\"false\" autocomplete=\"off\">").Append(H(file.Source)).Append("</textarea>");
        }
        html.Append("</div></div></div></section>");
        html.Append("<section class=\"playground-preview\" aria-labelledby=\"").Append(safeId)
            .Append("-preview-heading\"><h2 id=\"").Append(safeId).Append("-preview-heading\">Preview</h2>")
            .Append("<div data-playground-preview role=\"region\" aria-live=\"polite\">");
        if (state.LastSuccessfulPreview is { } preview)
            html.Append("<pre>").Append(H(preview)).Append("</pre>");
        else
            html.Append("<p class=\"playground-empty\">Run the example to see a preview.</p>");
        html.Append("</div></section></div>");
        AppendDiagnostics(html, state.Result, safeId);
        AppendOutput(html, state.Result, safeId);
        html.Append("</section>");
        return html.ToString();
    }

    private static void AppendDiagnostics(StringBuilder html, ScriptExecutionResult? result, string id)
    {
        html.Append("<section class=\"playground-diagnostics\" aria-labelledby=\"").Append(id)
            .Append("-diagnostics-heading\"><h2 id=\"").Append(id).Append("-diagnostics-heading\">Diagnostics</h2>")
            .Append("<div data-playground-diagnostics aria-live=\"polite\">");
        if (result is null || (result.Diagnostics.Count == 0 && result.Failure is null))
            html.Append("<p class=\"playground-empty\">No diagnostics.</p>");
        else
        {
            if (result.Diagnostics.Count > 0)
            {
                html.Append("<ul>");
                foreach (ScriptExecutionDiagnostic diagnostic in result.Diagnostics)
                {
                    html.Append("<li data-severity=\"").Append(diagnostic.Severity.ToString().ToLowerInvariant()).Append("\">")
                        .Append("<strong>").Append(H(diagnostic.Code)).Append("</strong> ")
                        .Append(H(diagnostic.Message));
                    if (diagnostic.Span is { } span)
                        html.Append(" <span class=\"playground-location\">").Append(H(span.FileName)).Append(':')
                            .Append(span.StartLine).Append(':').Append(span.StartColumn).Append("</span>");
                    html.Append("</li>");
                }
                html.Append("</ul>");
            }
            if (result.Failure is { } failure)
            {
                html.Append("<div class=\"playground-failure\" role=\"alert\"><strong>")
                    .Append(H(failure.Kind.ToString())).Append(":</strong> ").Append(H(failure.Message));
                if (!string.IsNullOrWhiteSpace(failure.StackTrace))
                    html.Append("<details><summary>Failure details</summary><pre>").Append(H(failure.StackTrace)).Append("</pre></details>");
                html.Append("</div>");
            }
        }
        html.Append("</div></section>");
    }

    private static void AppendOutput(StringBuilder html, ScriptExecutionResult? result, string id)
    {
        html.Append("<section class=\"playground-output\" aria-labelledby=\"").Append(id)
            .Append("-output-heading\"><h2 id=\"").Append(id).Append("-output-heading\">Output</h2>")
            .Append("<div data-playground-output aria-live=\"polite\">");
        if (result is null || result.Logs.Count == 0)
            html.Append("<p class=\"playground-empty\">No output.</p>");
        else
        {
            html.Append("<ol class=\"playground-log\">");
            foreach (ScriptLogEntry entry in result.Logs)
                html.Append("<li data-level=\"").Append(entry.Level.ToString().ToLowerInvariant()).Append("\"><span>")
                    .Append(H(entry.Timestamp.ToString("u"))).Append("</span> ").Append(H(entry.Message)).Append("</li>");
            html.Append("</ol>");
        }
        html.Append("</div></section>");
    }

    private static string FileId(int index) => $"template-file-{index}";

    private static string FileLanguage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csx" => "csharp-script",
        ".cs" => "csharp",
        ".slang" or ".slangh" => "slang",
        _ => "text",
    };

    private static string H(string value) => WebUtility.HtmlEncode(value);
}

public static class PlaygroundAssets
{
    public const string StylePath = "wwwroot/luxel-playground.css";
    public const string ScriptPath = "wwwroot/luxel-playground.js";

    public static string ReadStyle() => ReadEmbedded("luxel-playground.css");
    public static string ReadScript() => ReadEmbedded("luxel-playground.js");

    private static string ReadEmbedded(string suffix)
    {
        Assembly assembly = typeof(PlaygroundAssets).Assembly;
        string name = assembly.GetManifestResourceNames().Single(resource =>
            resource.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded playground asset '{suffix}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
