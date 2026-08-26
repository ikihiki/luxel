using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Editor.Browser.E2E.Tests;

internal static class EditorPageExtensions
{
    public static EditorPageFailures CollectFailures(this IPage page) => new(page);

    public static async Task<JsonElement> OpenEditorAsync(this IPage page, string? url = null)
    {
        await EditorBrowserTestHost.EnsureStartedAsync();
        await page.GotoAsync(url ?? EditorBrowserTestHost.BaseUrl + "/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        return await page.WaitForEditorAsync();
    }

    public static async Task<JsonElement> WaitForEditorAsync(this IPage page)
    {
        ILocator status = page.GetByTestId("editor-status");
        await Expect(status).ToHaveAttributeAsync("data-status", "ready", new() { Timeout = 90_000 });
        await Expect(page.GetByTestId("editor-canvas")).ToBeVisibleAsync();
        return await EditorPolling.EventuallyValueAsync(
            () => page.SnapshotAsync(),
            snapshot => snapshot.TryGetProperty("projectId", out JsonElement project) && project.GetString() == "builtin:demo",
            message: "The built-in demo did not become available through the automation contract.");
    }

    public static Task<JsonElement> SnapshotAsync(this IPage page)
        => page.EvaluateAsync<JsonElement>("() => globalThis.luxelEditorAutomation.snapshot()");

    public static Task<JsonElement> InvokeEditorAsync(this IPage page, string action, string value = "")
        => page.EvaluateAsync<JsonElement>(
            "args => globalThis.luxelEditorAutomation.invoke(args.action, args.value)",
            new { action, value });

    public static Task<JsonElement> RunEditorCommandAsync(this IPage page, string commandId, object? args = null)
        => page.EvaluateAsync<JsonElement>(
            "request => globalThis.luxelEditor.commands.run(request.commandId, request.args)",
            new { commandId, args });

    public static Task<JsonElement> RunEditorMacroAsync(this IPage page, object macro)
        => page.EvaluateAsync<JsonElement>(
            "macro => globalThis.luxelEditor.macros.run(macro)",
            macro);

    public static Task<JsonElement> UpdateEditorKeybindingsAsync(this IPage page, object bindings)
        => page.EvaluateAsync<JsonElement>(
            "bindings => globalThis.luxelEditor.keybindings.update(bindings)",
            bindings);

    public static Task<JsonElement> GetEditorKeybindingsAsync(this IPage page)
        => page.EvaluateAsync<JsonElement>("() => globalThis.luxelEditor.keybindings.get()");

    public static JsonElement Document(this JsonElement snapshot, string path)
        => snapshot.GetProperty("documents").EnumerateArray().Single(document =>
            document.TryGetProperty("path", out JsonElement candidate) && candidate.GetString() == path);

    public static float[] Position(this JsonElement snapshot)
        => snapshot.GetProperty("inspector").GetProperty("position").EnumerateArray().Select(value => value.GetSingle()).ToArray();

    public static float[] MaterialPosition(this JsonElement snapshot)
        => snapshot.GetProperty("material").GetProperty("firstNodePosition").EnumerateArray().Select(value => value.GetSingle()).ToArray();
}

internal static class EditorPolling
{
    public static async Task<T> EventuallyValueAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> predicate,
        int timeoutMilliseconds = 90_000,
        string? message = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        T value = default!;
        while (DateTime.UtcNow < deadline)
        {
            value = await read();
            if (predicate(value)) return value;
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException(message ?? $"Condition was not satisfied within {timeoutMilliseconds} ms. Last value: {value}");
    }
}

internal sealed class EditorPageFailures
{
    public EditorPageFailures(IPage page)
    {
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !IsExpectedSoftwareGpuDiagnostic(message.Text)) ConsoleErrors.Add(message.Text);
        };
        page.PageError += (_, error) =>
        {
            if (!IsExpectedSoftwareGpuDiagnostic(error)) PageErrors.Add(error);
        };
        page.Response += (_, response) =>
        {
            if (response.Status >= 400) FailedResponses.Add($"{response.Status} {response.Url}");
        };
    }

    public List<string> ConsoleErrors { get; } = [];
    public List<string> PageErrors { get; } = [];
    public List<string> FailedResponses { get; } = [];

    public void Clear()
    {
        ConsoleErrors.Clear();
        PageErrors.Clear();
        FailedResponses.Clear();
    }

    public void AssertEmpty()
    {
        Assert.True(ConsoleErrors.Count == 0, $"Console errors:{Environment.NewLine}{string.Join(Environment.NewLine, ConsoleErrors)}");
        Assert.True(PageErrors.Count == 0, $"Page errors:{Environment.NewLine}{string.Join(Environment.NewLine, PageErrors)}");
        Assert.True(FailedResponses.Count == 0, $"Failed responses:{Environment.NewLine}{string.Join(Environment.NewLine, FailedResponses)}");
    }

    private static bool IsExpectedSoftwareGpuDiagnostic(string message)
        => message.Contains("WebGPU device was lost: destroyed: Device was destroyed.", StringComparison.Ordinal)
           || message.Contains("A valid external Instance reference no longer exists.", StringComparison.Ordinal);
}
