using System.Numerics;
using System.Text.Json;
using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.Input;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Native Galleryとbrowser-WASM runtimeで共有するinput stories。</summary>
public static class InputActionStories
{
    // docs:begin input-window-actions
    [Story("Examples/Input/WindowActions", Width = 680, Height = 430, Order = 0)]
    public static Widget WindowActions(StoryContext ctx)
    {
        IStoryInputRuntime? runtime = ctx.Get<IStoryInputRuntime>();
        var bus = new InputBus();
        var gameplay = new InputContext("Gameplay");
        var move = gameplay.Add(new Axis2DAction("Move"));
        move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
        move.ButtonQuads.Add((KeyCode.Up, KeyCode.Down, KeyCode.Left, KeyCode.Right));
        var fire = gameplay.Add(new ButtonAction("Fire", KeyCode.Mouse0));
        var stack = new InputStack();
        stack.Push(gameplay);

        var vector = new Signal<Vector2>(Vector2.Zero);
        var focused = new Signal<bool>(runtime?.IsFocused ?? false);
        var fireHeld = new Signal<bool>(false);
        var pressCount = new Signal<int>(0);
        var releaseCount = new Signal<int>(0);
        fire.Triggered += () => pressCount.Value++;
        fire.Released += () => releaseCount.Value++;

        Widget meter = Canvas2D(560, 170, animate: (scene, _) =>
        {
            if (runtime is not null)
            {
                runtime.Poll(bus);
                stack.Update(bus);
                vector.Value = move.Value.Value;
                fireHeld.Value = fire.Value.Value;
                focused.Value = runtime.IsFocused;
            }

            uint panel = Color2D.Rgba(25, 32, 45, 255);
            uint accent = fireHeld.Value ? Color2D.Rgba(251, 146, 60, 255) : Color2D.Rgba(96, 165, 250, 255);
            scene.FillRoundedRect(panel, 0, 0, 560, 170, 14);
            scene.FillCircle(Color2D.Rgba(148, 163, 184, 255), 280, 85, 105);
            scene.FillCircle(panel, 280, 85, 102);
            scene.FillCircle(accent, 280 + vector.Value.X * 95, 85 - vector.Value.Y * 55, fireHeld.Value ? 18 : 13);
        });

        string Capability() => runtime is null
            ? "Physical input service: unavailable (deterministic docs/catalog fallback)"
            : focused.Value ? "Focused: keyboard and pointer actions are active" : "Click the preview to focus input";

        return Frame(VStack(12, width: 600)[
            Heading("Window actions", 2),
            Text("Click this preview, then use WASD / arrow keys. Hold the left pointer button for Fire.", 14,
                color: Bind.From(() => UiTheme.T.TextMuted), wrap: TextWrap.Word, width: 590),
            meter,
            HStack(20)[
                Text((Func<string>)(() => $"Move: {vector.Value.X:0.00}, {vector.Value.Y:0.00}"), 14),
                Text((Func<string>)(() => $"Fire: {(fireHeld.Value ? "held" : "idle")}"), 14),
                Text((Func<string>)(() => $"Pressed: {pressCount.Value}  Released: {releaseCount.Value}"), 14)],
            Text((Func<string>)Capability, 12, color: Bind.From(() => UiTheme.T.TextMuted))]);
    }
    // docs:end input-window-actions

    // docs:begin input-context-stack
    [Story("Examples/Input/ContextStack", Width = 620, Height = 360, Order = 1)]
    public static Widget ContextStack()
    {
        var source = new FakeInputSource();
        var bus = new InputBus();
        var gameplay = new InputContext("Gameplay");
        var gameplayConfirm = gameplay.Add(new ButtonAction("Confirm", KeyCode.Enter));
        var menu = new InputContext("Menu");
        var menuConfirm = menu.Add(new ButtonAction("Confirm", KeyCode.Enter));
        var stack = new InputStack();
        stack.Push(gameplay);
        stack.Push(menu);
        var menuSuspended = new Signal<bool>(false);
        var result = new Signal<string>("Press the simulated Enter button.");

        void Tap()
        {
            source.PressKey(KeyCode.Enter); source.Poll(bus); stack.Update(bus);
            result.Value = menuConfirm.IsActive.Value ? "Menu consumed Enter" : gameplayConfirm.IsActive.Value ? "Gameplay received Enter" : "No context received Enter";
            source.ReleaseKey(KeyCode.Enter); source.Poll(bus); stack.Update(bus);
        }

        void ToggleMenu()
        {
            menuSuspended.Value = !menuSuspended.Value;
            stack.SetSuspended(menu, menuSuspended.Value);
            result.Value = menuSuspended.Value ? "Menu suspended; Gameplay is next" : "Menu resumed; it has priority";
        }

        return Frame(VStack(14, width: 520)[
            Heading("Context priority and consumption", 2),
            Text("The last pushed context is evaluated first. Active actions consume their bindings before lower contexts run.", 14,
                wrap: TextWrap.Word, width: 510, color: Bind.From(() => UiTheme.T.TextMuted)),
            HStack(10)[Button(_ => Tap(), "Simulate Enter"), Button(_ => ToggleMenu(), (Func<string>)(() => menuSuspended.Value ? "Resume Menu" : "Suspend Menu"))],
            Card(VStack(6)[
                Text((Func<string>)(() => $"Menu: {(menuSuspended.Value ? "suspended" : "active / top")}"), 14),
                Text("Gameplay: active / lower", 14),
                Text($"{result}", 15)])]);
    }
    // docs:end input-context-stack

    // docs:begin input-bindings-story
    [Story("Examples/Input/Bindings", Width = 650, Height = 390, Order = 2)]
    public static Widget Bindings()
    {
        var context = new InputContext("Gameplay");
        var jump = context.Add(new ButtonAction("Jump", KeyCode.Space));
        var current = new Signal<string>("Space");
        var json = new Signal<string>(Serialize(KeyCode.Space));

        void Rebind()
        {
            KeyCode key = current.Value == "Space" ? KeyCode.Enter : KeyCode.Space;
            var bindings = new InputBindings
            {
                Actions = { ["Jump"] = new InputBindingEntry { Kind = "button", Keys = [key.ToString()] } }
            };
            InputBindingsApplier.Apply(bindings, context);
            current.Value = jump.Keys.Single().ToString();
            json.Value = JsonSerializer.Serialize(bindings, new JsonSerializerOptions { WriteIndented = true });
        }

        return Frame(VStack(12, width: 560)[
            Heading("Bindings and rebinding", 2),
            Text("Bindings are data. Apply the JSON-shaped model to an existing context instead of branching game code by platform.", 14,
                wrap: TextWrap.Word, width: 550, color: Bind.From(() => UiTheme.T.TextMuted)),
            HStack(12)[Text((Func<string>)(() => $"Jump → {current.Value}"), 16), Button(_ => Rebind(), "Toggle Space / Enter")],
            Card(Text($"{json}", 12, wrap: TextWrap.Word, width: 500))]);

        static string Serialize(KeyCode key) => JsonSerializer.Serialize(new InputBindings
        {
            Actions = { ["Jump"] = new InputBindingEntry { Kind = "button", Keys = [key.ToString()] } }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
    // docs:end input-bindings-story
}
