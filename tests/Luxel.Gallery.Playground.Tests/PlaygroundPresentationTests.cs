using System.Net;
using Luxel.Gallery.Playground;
using Luxel.Scripting;

namespace Luxel.Gallery.Playground.Tests;

public sealed class PlaygroundPresentationTests
{
    [Fact]
    public void Workspace_exports_accessible_editor_controls_and_result_regions()
    {
        PlaygroundDraft draft = PlaygroundTemplates.Button.CreateDraft().UpdateFile(
            "Button.csx", "return \"<unsafe>\";");
        var state = new PlaygroundState
        {
            Draft = draft,
            Status = PlaygroundStatus.Failed,
            LastSuccessfulResult = new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.Succeeded,
                ReturnValue = "<button>last good</button>",
            },
            Result = new ScriptExecutionResult
            {
                Outcome = ScriptExecutionOutcome.RuntimeFailed,
                Diagnostics =
                [
                    new ScriptExecutionDiagnostic
                    {
                        Code = "LX001",
                        Message = "Bad <value>",
                        Severity = ScriptDiagnosticSeverity.Error,
                        Span = new ScriptSourceSpan
                        {
                            FileName = "Button.csx", StartLine = 2, StartColumn = 3,
                        },
                    },
                ],
                Logs =
                [
                    new ScriptLogEntry
                    {
                        Level = ScriptLogLevel.Information,
                        Message = "hello <world>",
                        Timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    },
                ],
                Failure = new ScriptExecutionFailure
                {
                    Kind = ScriptFailureKind.Runtime,
                    Message = "Boom <now>",
                    StackTrace = "at <script>",
                },
            },
        };

        string html = PlaygroundWorkspace.Render(state, "sample");

        Assert.Contains("<textarea", html);
        Assert.Contains("Monaco C# · Browser Roslyn completion, hover, and live diagnostics", html);
        Assert.Contains("data-playground-monaco", html);
        Assert.Contains("data-playground-editor-host", html);
        Assert.Contains("data-playground-sample-select", html);
        Assert.Contains("data-playground-sample-load", html);
        Assert.Contains("data-playground-samples", html);
        Assert.Contains("3D Slang Cube", html);
        Assert.Contains("data-playground-run", html);
        Assert.Contains(">Run</button>", html);
        Assert.Contains(">Stop</button>", html);
        Assert.Contains(">Reset</button>", html);
        Assert.Contains(">Preview</h2>", html);
        Assert.Contains(">Diagnostics</h2>", html);
        Assert.Contains(">Output</h2>", html);
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("data-workspace-schema=\"2\"", html);
        Assert.Contains($"data-entry-file-id=\"{draft.MainFileId}\"", html);
        Assert.Contains($"data-active-file-id=\"{draft.SelectedFileId}\"", html);
        Assert.Contains($"data-workspace-revision=\"{draft.Revision}\"", html);
        Assert.Contains("data-playground-file-list", html);
        Assert.Contains("data-playground-file-add", html);
        Assert.Contains("data-playground-file-rename", html);
        Assert.Contains("data-playground-file-delete", html);
        Assert.Contains("data-playground-file-select", html);
        Assert.Contains("data-playground-active-file-label for=\"sample-source\">Button.csx</label>", html);
        Assert.Contains("&lt;button&gt;last good&lt;/button&gt;", html);
        Assert.Contains("Bad &lt;value&gt;", html);
        Assert.Contains("hello &lt;world&gt;", html);
        Assert.Contains("Boom &lt;now&gt;", html);
        Assert.DoesNotContain("<unsafe>", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Default_template_renders_a_csharp_script_entry_for_browser_protocol_parsing()
    {
        PlaygroundDraft draft = PlaygroundTemplates.Button.CreateDraft();

        Assert.Equal("csharp-script", draft.MainFile.Language);
        string html = PlaygroundWorkspace.Render(new PlaygroundState { Draft = draft }, "default-template");
        Assert.Contains("data-file-name=\"Button.csx\" data-file-language=\"csharp-script\"", html);
    }

    [Fact]
    public void Workspace_round_trips_shared_model_identity_selection_language_and_versions()
    {
        PlaygroundFile[] files =
        [
            new PlaygroundFile("entry&id", "Main.csx", "csharp-script", "return 1;", 7),
            new PlaygroundFile("selected\"file", "notes.txt", "markdown", "# <selected>", 11),
        ];
        var draft = new PlaygroundDraft(
            "custom-template",
            "Custom workspace",
            files[0].Id,
            files[1].Id,
            files,
            13);

        string html = PlaygroundWorkspace.Render(new PlaygroundState { Draft = draft }, "custom");

        string entryId = WebUtility.HtmlEncode(files[0].Id);
        string selectedId = WebUtility.HtmlEncode(files[1].Id);
        Assert.Contains($"data-entry-file-id=\"{entryId}\"", html);
        Assert.Contains($"data-active-file-id=\"{selectedId}\"", html);
        Assert.Contains("data-workspace-revision=\"13\"", html);
        Assert.Contains($"data-file-id=\"{entryId}\" data-file-name=\"Main.csx\" data-file-language=\"csharp-script\" data-file-version=\"7\"", html);
        Assert.Contains($"id=\"custom-source\" data-playground-source data-file-id=\"{selectedId}\" data-file-name=\"notes.txt\" data-file-language=\"markdown\" data-file-version=\"11\"", html);
        Assert.Contains($"data-file-id=\"{selectedId}\" title=\"notes.txt\" aria-controls=\"custom-file-editor\" aria-selected=\"true\" tabindex=\"0\"", html);
        Assert.Contains($"data-file-id=\"{entryId}\" title=\"Main.csx\" aria-controls=\"custom-file-editor\" aria-selected=\"false\" tabindex=\"-1\"", html);
        Assert.Contains("class=\"playground-file-editor\" id=\"custom-file-editor\" role=\"tabpanel\"", html);
        Assert.Contains("data-playground-active-file-label for=\"custom-source\">notes.txt</label>", html);
        Assert.Contains("data-playground-monaco aria-label=\"notes.txt code editor\"", html);
        Assert.Contains("# &lt;selected&gt;", html);
        Assert.DoesNotContain("selected\"file", html);
    }

    [Fact]
    public void Client_asset_persists_drafts_and_uses_an_injected_event_bridge_without_leakage_sinks()
    {
        string script = PlaygroundAssets.ReadScript();
        string style = PlaygroundAssets.ReadStyle();

        Assert.Contains("const maxFiles = 128", script);
        Assert.Contains("const maxCSharpFileBytes = 128 * 1024", script);
        Assert.Contains("const maxWorkspaceBytes = 2 * 1024 * 1024", script);
        Assert.Contains("new TextEncoder()", script);
        Assert.Contains("luxel.playground.workspace.v2:", script);
        Assert.Contains("luxel.playground.draft.v1:", script);
        Assert.Contains("schemaVersion = 2", script);
        Assert.Contains("source.dataset.fileVersion", script);
        Assert.Contains("root.dataset.workspaceRevision", script);
        Assert.Contains("selectFile(root, workspace.activeFileId, null, false)", script);
        Assert.Contains("cloneWorkspace", script);
        Assert.Contains("readSamples", script);
        Assert.Contains("loadSample", script);
        Assert.Contains("sampleId", script);
        Assert.Contains("Replace the current workspace with this sample?", script);
        Assert.Contains("addFile", script);
        Assert.Contains("renameFile", script);
        Assert.Contains("deleteFile", script);
        Assert.Contains("selectFile", script);
        Assert.Contains("localStorage", script);
        Assert.Contains("memoryDrafts", script);
        Assert.Contains("addEventListener(\"input\"", script);
        Assert.Contains("LuxelMonacoReady", script);
        Assert.Contains("monaco.editor.createModel", script);
        Assert.Contains("\"csharp\"", script);
        Assert.Contains("luxel-language-service", script);
        Assert.Contains("registerCompletionItemProvider", script);
        Assert.Contains("registerHoverProvider", script);
        Assert.Contains("luxel-playground:language-request", script);
        Assert.Contains("language service", script);
        Assert.Contains("setModelMarkers", script);
        Assert.Contains("navigateFileTabs", script);
        Assert.Contains("ArrowLeft", script);
        Assert.Contains("Home", script);
        Assert.Contains("scrollIntoView", script);
        Assert.Contains("luxel-playground:execute", script);
        Assert.Contains("luxel-playground:cancel", script);
        Assert.Contains("luxel-playground:reset", script);
        Assert.Contains("CustomEvent", script);
        Assert.DoesNotContain("fetch(", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WebSocket", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sendBeacon", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("window.location", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("URLSearchParams", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("console.", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eval(", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".luxel-playground", style);
        Assert.Contains("container-type: inline-size", style);
        Assert.Contains("@container luxel-playground (min-width: 62rem)", style);
        Assert.Contains("@container luxel-playground (max-width: 40rem)", style);
        Assert.Contains("grid-template-columns: minmax(0, 2fr) minmax(20rem, 1fr)", style);
        Assert.Contains(".playground-samples", style);
        Assert.Contains(".playground-preview iframe", style);
        Assert.Contains("min-height: 44px", style);
        Assert.Contains(":focus-visible", style);
    }
}
