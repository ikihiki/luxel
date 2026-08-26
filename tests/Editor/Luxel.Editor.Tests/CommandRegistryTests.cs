using System.Text.Json;
using Luxel.UI;
using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: CommandRegistry (ADR-0013) — 登録/実行/enablement/キーマップ/メニュー合成。GPU 不要。</summary>
public class CommandRegistryTests
{
    [Fact]
    public void KeyGestures_ParseAndFormat()
    {
        Assert.Equal(new KeyGesture(Key.P, Ctrl: true, Shift: true), KeyGestures.Parse("Ctrl+Shift+P"));
        Assert.Equal(new KeyGesture(Key.F3), KeyGestures.Parse("F3"));
        Assert.Equal(new KeyGesture(Key.D1, Ctrl: true), KeyGestures.Parse("Ctrl+1"));
        Assert.Null(KeyGestures.Parse("Ctrl+Nope"));
        Assert.Equal("Ctrl+Shift+P", KeyGestures.Format(new KeyGesture(Key.P, Ctrl: true, Shift: true)));
        Assert.Equal("Ctrl+1", KeyGestures.Format(new KeyGesture(Key.D1, Ctrl: true)));
    }

    [Fact]
    public void Run_ExecutesOnlyWhenEnabled()
    {
        var reg = new CommandRegistry();
        int ran = 0;
        bool enabled = false;
        reg.Register("t.run", "実行", () => ran++, enabled: () => enabled);

        Assert.False(reg.Run("t.run"));
        Assert.Equal(0, ran);
        enabled = true;
        Assert.True(reg.Run("t.run"));
        Assert.Equal(1, ran);
        Assert.False(reg.Run("nope"));
    }

    [Fact]
    public void HandleKey_DispatchesByGesture_ContributionFirst()
    {
        var reg = new CommandRegistry();
        string log = "";
        reg.Register("t.save", "保存", () => log += "base;", key: "Ctrl+S");

        Assert.True(reg.HandleKey(Key.S, KeyModifiers.Ctrl));
        Assert.Equal("base;", log);
        Assert.False(reg.HandleKey(Key.S, KeyModifiers.None));   // 修飾不一致

        // アクティブ doc の寄与が同じキーを持つ → 寄与優先
        var contrib = new[] { new CommandContribution(
            new Command("doc.save", "doc 保存", () => log += "doc;", Gesture: KeyGestures.Parse("Ctrl+S"))) };
        Assert.True(reg.HandleKey(Key.S, KeyModifiers.Ctrl, contrib));
        Assert.Equal("base;doc;", log);
    }

    [Fact]
    public void BuildMenu_PathsBecomeHierarchy_OrderedByOrderThenSeq()
    {
        var reg = new CommandRegistry();
        reg.Register("f.exit", "終了", () => { }, menuPath: "File/終了", order: 99);
        reg.Register("f.save", "保存", () => { }, menuPath: "File/保存", order: 0);
        reg.Register("e.find", "検索", () => { }, menuPath: "Edit/検索");
        reg.Register("f.recent1", "最近 1", () => { }, menuPath: "File/最近使った/one", order: 50);

        var menu = reg.BuildMenu();

        Assert.Equal(["File", "Edit"], menu.Select(n => n.Label).ToArray());
        MenuNode file = menu[0];
        Assert.Equal(["保存", "最近使った", "終了"], file.Children.Select(n => n.Label).ToArray());
        Assert.NotNull(file.Children[0].Command);          // 葉 = コマンド
        Assert.Null(file.Children[1].Command);             // フォルダ
        Assert.Equal("one", file.Children[1].Children[0].Label);
    }

    [Fact]
    public void BuildMenu_MergesActiveDocContributions()
    {
        var reg = new CommandRegistry();
        reg.Register("f.save", "保存", () => { }, menuPath: "File/保存");
        var contrib = new[] { new CommandContribution(
            new Command("g.layout", "整列", () => { }), MenuPath: "Graph/整列") };

        var menu = reg.BuildMenu(contrib);

        Assert.Equal(["File", "Graph"], menu.Select(n => n.Label).ToArray());
        Assert.Equal("整列", menu[1].Children[0].Label);

        // 寄与なしなら Graph は出ない (アクティブ doc 切替で章が消える)
        Assert.Equal(["File"], reg.BuildMenu().Select(n => n.Label).ToArray());
    }

    [Fact]
    public void ToolbarAndPalette_IncludeContributions()
    {
        var reg = new CommandRegistry();
        reg.Register("t.a", "Aaa", () => { }, toolbar: true, order: 1);
        reg.Register("t.b", "Bbb", () => { });
        var contrib = new[] { new CommandContribution(new Command("t.c", "Ccc", () => { }), Toolbar: true, Order: 0) };

        Assert.Equal(["Ccc", "Aaa"], reg.ToolbarCommands(contrib).Select(c => c.Title).ToArray());
        Assert.Equal(["Aaa", "Bbb", "Ccc"], reg.PaletteCommands(contrib).Select(c => c.Title).ToArray());
        Assert.Equal(["Aaa", "Bbb"], reg.PaletteCommands().Select(c => c.Title).ToArray());
    }

    [Fact]
    public void DescriptorsExposeCurrentStateEffectiveGestureAndSurfaces()
    {
        var reg = new CommandRegistry();
        bool enabled = false;
        reg.Register("tools.inspect", "Inspect", () => { }, () => enabled,
            key: "Ctrl+I", menuPath: "Tools/Inspect", toolbar: true);
        reg.SetGestureOverride("tools.inspect", KeyGestures.Parse("Ctrl+K"));

        CommandDescriptor descriptor = Assert.Single(reg.Descriptors());
        Assert.Equal("tools.inspect", descriptor.Id);
        Assert.Equal("Inspect", descriptor.Title);
        Assert.False(descriptor.Enabled);
        Assert.Equal("Ctrl+K", descriptor.EffectiveGestureText);
        Assert.Equal(["Tools/Inspect"], descriptor.MenuPaths);
        Assert.True(descriptor.Toolbar);

        enabled = true;
        Assert.True(reg.Describe("tools.inspect")!.Enabled);
        reg.Register("late.command", "Late", () => { });
        Assert.Contains(reg.PaletteDescriptors(), x => x.Id == "late.command");
    }

    [Fact]
    public void ExecuteReturnsStructuredOutcomesAndRunPreservesExceptionBehavior()
    {
        var reg = new CommandRegistry();
        int ran = 0;
        reg.Register("ok", "OK", () => ran++);
        reg.Register("disabled", "Disabled", () => ran++, enabled: () => false);
        reg.Register("failed", "Failed", () => throw new InvalidOperationException("boom"));

        Assert.Equal(CommandExecutionStatus.Executed, reg.Execute("ok").Status);
        Assert.Equal(1, ran);
        Assert.Equal(CommandExecutionStatus.Disabled, reg.Execute("disabled").Status);
        Assert.Equal(CommandExecutionStatus.NotFound, reg.Execute("missing").Status);
        CommandExecutionResult failed = reg.Execute("failed");
        Assert.Equal(CommandExecutionStatus.Failed, failed.Status);
        Assert.Equal("boom", failed.Message);
        Assert.Throws<InvalidOperationException>(() => reg.Run("failed"));
    }

    [Fact]
    public void KeyGestureSequencesParseFormatAndPreserveSingleStrokeCompatibility()
    {
        KeyGestureSequence sequence = Assert.IsType<KeyGestureSequence>(KeyGestures.ParseSequence("ctrl+k ctrl+s"));
        Assert.Equal(2, sequence.Count);
        Assert.Equal("Ctrl+K Ctrl+S", KeyGestures.Format(sequence));
        Assert.True(KeyGestures.SequenceEqual(sequence, KeyGestures.ParseSequence("Ctrl+K Ctrl+S")));
        Assert.Equal(new KeyGesture(Key.S, Ctrl: true), KeyGestures.ParseSequence("Ctrl+S")![0]);
    }

    [Fact]
    public void ArgumentCommandsApplyDefaultsValidateAndReceiveImmutableJson()
    {
        var reg = new CommandRegistry();
        using JsonDocument defaults = JsonDocument.Parse("{\"mode\":\"safe\"}");
        string? received = null;
        reg.Register("build.run", "Build", invocation => received = invocation.Arguments?.GetRawText(),
            new CommandArgumentSchema("Build options", Required: true,
                DefaultValue: defaults.RootElement.Clone(),
                Validator: value => value?.ValueKind == JsonValueKind.Object ? null : "Expected an object."));

        CommandExecutionResult defaulted = reg.Execute("build.run");
        Assert.Equal(CommandExecutionStatus.Executed, defaulted.Status);
        Assert.Equal("{\"mode\":\"safe\"}", received);
        Assert.Equal("Build options", reg.Describe("build.run")!.ArgumentHelp);
        Assert.True(reg.Describe("build.run")!.PaletteExecutable);

        using JsonDocument invalid = JsonDocument.Parse("42");
        CommandExecutionResult rejected = reg.Execute("build.run", invalid.RootElement);
        Assert.Equal(CommandExecutionStatus.InvalidArguments, rejected.Status);
        Assert.Equal("invalid_arguments", rejected.Code);
        Assert.Equal("Expected an object.", rejected.Message);
    }

    [Fact]
    public void InvocationAndArgumentMetadataCloneJsonAndPreserveLegacyCommandShape()
    {
        CommandInvocationContext invocation;
        CommandArgumentSchema arguments;
        using (JsonDocument supplied = JsonDocument.Parse("{\"value\":7}"))
        using (JsonDocument defaults = JsonDocument.Parse("{\"value\":3}"))
        using (JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\"}"))
        {
            invocation = new CommandInvocationContext("args", supplied.RootElement);
            arguments = new CommandArgumentSchema("Options", Required: true,
                DefaultValue: defaults.RootElement, Schema: schema.RootElement);
        }

        Assert.Equal(7, invocation.Arguments!.Value.GetProperty("value").GetInt32());
        Assert.Equal(3, arguments.DefaultValue!.Value.GetProperty("value").GetInt32());
        Assert.Equal("object", arguments.Schema!.Value.GetProperty("type").GetString());

        var legacy = new Command("legacy", "Legacy", () => { }, () => true,
            new KeyGesture(Key.L, Ctrl: true));
        (string id, string title, Action run, Func<bool>? enabled, KeyGesture? gesture) = legacy;
        Assert.Equal("legacy", id);
        Assert.Equal("Legacy", title);
        Assert.NotNull(run);
        Assert.True(enabled!());
        Assert.Equal(new KeyGesture(Key.L, Ctrl: true), gesture);
    }

    [Fact]
    public void ArgumentValidatorExceptionsProduceFailedResults()
    {
        var reg = new CommandRegistry();
        reg.Register("validate", "Validate", _ => { },
            new CommandArgumentSchema(Validator: _ => throw new InvalidOperationException("validator failed")));

        CommandExecutionResult result = reg.Execute("validate");

        Assert.Equal(CommandExecutionStatus.Failed, result.Status);
        Assert.Equal("failed", result.Code);
        Assert.Equal("validator failed", result.Message);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public void ParameterlessCommandsRejectSuppliedArgumentsInCoreRegistry()
    {
        var reg = new CommandRegistry();
        int ran = 0;
        reg.Register("plain", "Plain", () => ran++);
        using JsonDocument args = JsonDocument.Parse("{\"unexpected\":true}");

        CommandExecutionResult result = reg.Execute("plain", args.RootElement);

        Assert.Equal(CommandExecutionStatus.InvalidArguments, result.Status);
        Assert.Equal("invalid_arguments", result.Code);
        Assert.Contains("does not accept arguments", result.Message);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void RequiredArgumentsWithoutDefaultAreNotPaletteExecutable()
    {
        var reg = new CommandRegistry();
        int ran = 0;
        reg.Register("deploy", "Deploy", _ => ran++,
            new CommandArgumentSchema("Deployment target", Required: true));

        CommandExecutionResult result = reg.Execute("deploy");

        Assert.Equal(CommandExecutionStatus.InvalidArguments, result.Status);
        Assert.Equal(0, ran);
        CommandDescriptor descriptor = reg.Describe("deploy")!;
        Assert.True(descriptor.RequiresArguments);
        Assert.False(descriptor.PaletteExecutable);
    }

    [Fact]
    public void PaletteExecutionReflectsDefaultsAndValidationWithoutInvokingCommands()
    {
        var reg = new CommandRegistry();
        int runs = 0;
        reg.Register("rejects.empty", "Rejects Empty", _ => runs++,
            new CommandArgumentSchema(Validator: value => value.HasValue ? null : "An argument is required."));
        reg.Register("validator.throws", "Throwing Validator", _ => runs++,
            new CommandArgumentSchema(Validator: _ => throw new InvalidOperationException("validator failed")));
        using JsonDocument defaults = JsonDocument.Parse("{\"mode\":\"safe\"}");
        reg.Register("valid.default", "Valid Default", _ => runs++,
            new CommandArgumentSchema(DefaultValue: defaults.RootElement,
                Validator: value => value?.ValueKind == JsonValueKind.Object ? null : "Expected an object."));

        Assert.False(reg.Describe("rejects.empty")!.PaletteExecutable);
        Assert.False(reg.Describe("validator.throws")!.PaletteExecutable);
        Assert.True(reg.Describe("valid.default")!.PaletteExecutable);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void InvocationAndSchemaCloneJsonBeyondSourceDocumentLifetime()
    {
        CommandInvocationContext invocation;
        CommandArgumentSchema schema;
        using (JsonDocument source = JsonDocument.Parse("{\"value\":7}"))
        {
            invocation = new("clone.test", source.RootElement);
            schema = new(DefaultValue: source.RootElement);
        }

        Assert.Equal(7, invocation.Arguments!.Value.GetProperty("value").GetInt32());
        Assert.Equal(7, schema.DefaultValue!.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Version_BumpsOnRegister()
    {
        var reg = new CommandRegistry();
        int v = reg.Version.Value;
        reg.Register("x", "X", () => { });
        Assert.True(reg.Version.Value > v);
    }
}
