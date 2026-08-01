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
        Assert.Contains("data-playground-run", html);
        Assert.Contains(">Run</button>", html);
        Assert.Contains(">Stop</button>", html);
        Assert.Contains(">Reset</button>", html);
        Assert.Contains(">Preview</h2>", html);
        Assert.Contains(">Diagnostics</h2>", html);
        Assert.Contains(">Output</h2>", html);
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("<label for=\"sample-source\">Button.csx</label>", html);
        Assert.Contains("&lt;button&gt;last good&lt;/button&gt;", html);
        Assert.Contains("Bad &lt;value&gt;", html);
        Assert.Contains("hello &lt;world&gt;", html);
        Assert.Contains("Boom &lt;now&gt;", html);
        Assert.DoesNotContain("<unsafe>", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Client_asset_persists_drafts_and_uses_an_injected_event_bridge_without_leakage_sinks()
    {
        string script = PlaygroundAssets.ReadScript();
        string style = PlaygroundAssets.ReadStyle();

        Assert.Contains("localStorage", script);
        Assert.Contains("memoryDrafts", script);
        Assert.Contains("addEventListener(\"input\"", script);
        Assert.Contains("LuxelMonacoReady", script);
        Assert.Contains("monaco.editor.createModel", script);
        Assert.Contains("\"csharp\"", script);
        Assert.Contains("luxel-roslyn", script);
        Assert.Contains("registerCompletionItemProvider", script);
        Assert.Contains("registerHoverProvider", script);
        Assert.Contains("luxel-playground:language-request", script);
        Assert.Contains("Roslyn C#", script);
        Assert.Contains("setModelMarkers", script);
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
    }
}
