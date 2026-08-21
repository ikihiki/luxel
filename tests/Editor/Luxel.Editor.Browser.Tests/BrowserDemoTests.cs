using Luxel.Editor.Browser;
using Luxel.Workbench;

namespace Luxel.Editor.Browser.Tests;

public sealed class BrowserDemoTests
{
    private static BrowserDemoSeed Seed() => new(new Dictionary<string, string>
    {
        ["luxel.project.json"] = "seed-project",
        ["Scripts/Player.cs"] = "seed-script",
        ["README.md"] = "seed-readme"
    });

    [Fact]
    public void EnsureSeededOnlyWritesAnEmptyWorkspace()
    {
        var storage = new MemoryFileStorage();
        BrowserDemoSeed seed = Seed();

        Assert.True(seed.EnsureSeeded(storage));
        storage.Write("Scripts/Player.cs", "edited");
        Assert.False(seed.EnsureSeeded(storage));

        Assert.Equal("edited", storage.Read("Scripts/Player.cs"));
    }

    [Fact]
    public void ResetRemovesExtraFilesAndRestoresEverySeedFile()
    {
        var storage = new MemoryFileStorage();
        BrowserDemoSeed seed = Seed();
        seed.EnsureSeeded(storage);
        storage.Write("Scripts/Player.cs", "edited");
        storage.Write("Scratch.tmp", "extra");

        seed.Reset(storage);

        Assert.Equal(seed.Files.Keys.Order(StringComparer.Ordinal), storage.List().Order(StringComparer.Ordinal));
        Assert.Equal("seed-script", storage.Read("Scripts/Player.cs"));
        Assert.False(storage.Exists("Scratch.tmp"));
    }

    [Fact]
    public void AutomationContractUsesStableIdentifiers()
    {
        Assert.Equal("luxelEditorState", BrowserAutomationContract.StateObject);
        Assert.Equal("luxelEditorAutomation.invoke", BrowserAutomationContract.InvokeFunction);
        Assert.Equal(BrowserAutomationContract.Actions.Count, BrowserAutomationContract.Actions.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("reset-demo", BrowserAutomationContract.Actions);
        Assert.Contains("change-layout", BrowserAutomationContract.Actions);
        Assert.DoesNotContain(BrowserAutomationContract.Actions, action => action.Contains(' '));
    }
}
