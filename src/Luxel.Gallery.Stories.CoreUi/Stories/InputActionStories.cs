using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.Controls;
using Luxel.Input;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InputBindings))]
internal partial class InputBindingsJsonContext : JsonSerializerContext { }

/// <summary>入力アクション、コンテキスト、バインディングを決定的に学ぶStory。</summary>
public static class InputActionStories
{
    [Story("Examples/Input/Actions", Width = 680, Height = 430, Order = 0)]
    public static Widget Actions()
    {
        // docs:begin input-actions-setup
        var source = new FakeInputSource();
        var bus = new InputBus();
        var gameplay = new InputContext("Gameplay");
        var jump = gameplay.Add(new ButtonAction("Jump", KeyCode.Space));
        var move = gameplay.Add(new Axis2DAction("Move"));
        move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
        var stack = new InputStack();
        stack.Push(gameplay);
        // docs:end input-actions-setup

        // docs:begin input-actions-edges
        var jumpTriggered = new Signal<int>(0);
        var jumpReleased = new Signal<int>(0);
        jump.Triggered += () => jumpTriggered.Value++;
        jump.Released += () => jumpReleased.Value++;

        void Tick() { source.Poll(bus); stack.Update(bus); }
        void SetKey(KeyCode key, bool pressed)
        {
            if (pressed) source.PressKey(key); else source.ReleaseKey(key);
            Tick();
        }
        // docs:end input-actions-edges

        Widget KeyButton(string label, KeyCode key) => HStack(6)[
            Button(_ => SetKey(key, true), $"{label} 押下"),
            Button(_ => SetKey(key, false), $"{label} 解放")];

        return Frame(VStack(12, width: 600)[
            Heading("アクションの値とエッジ", 2),
            Text("FakeInputSourceで物理入力を再現し、1回のPollとUpdateを1 tickとして処理します。", 14,
                color: Bind.From(() => UiTheme.T.TextMuted), wrap: TextWrap.Word, width: 590),
            HStack(16)[KeyButton("W", KeyCode.W), KeyButton("D", KeyCode.D)],
            KeyButton("Space", KeyCode.Space),
            Card(VStack(6)[
                Text((Func<string>)(() => $"Move = ({move.Value.Value.X:0.00}, {move.Value.Value.Y:0.00})"), 15),
                Text((Func<string>)(() => $"Jump = {(jump.IsActive.Value ? "押下中" : "未押下")}"), 15),
                Text((Func<string>)(() => $"Triggered = {jumpTriggered.Value} / Released = {jumpReleased.Value}"), 15)]),
            Text("WとDを両方押すと斜め方向が正規化されます。Spaceは押下tickでTriggered、解放tickでReleasedが1回ずつ発火します。", 13,
                color: Bind.From(() => UiTheme.T.TextMuted), wrap: TextWrap.Word, width: 590)]);
    }

    [Story("Examples/Input/ContextStack", Width = 650, Height = 390, Order = 1)]
    public static Widget ContextStack()
    {
        // docs:begin input-context-setup
        var source = new FakeInputSource();
        var bus = new InputBus();
        var gameplay = new InputContext("Gameplay");
        var gameplayConfirm = gameplay.Add(new ButtonAction("Confirm", KeyCode.Enter));
        var menu = new InputContext("Menu");
        var menuConfirm = menu.Add(new ButtonAction("Confirm", KeyCode.Enter));
        var stack = new InputStack();
        stack.Push(gameplay);
        stack.Push(menu);
        // docs:end input-context-setup

        var menuSuspended = new Signal<bool>(false);
        var result = new Signal<string>("Enterを送ると、最上位のMenuが先に処理します。");

        // docs:begin input-context-routing
        void TapEnter()
        {
            source.PressKey(KeyCode.Enter); source.Poll(bus); stack.Update(bus);
            result.Value = menuConfirm.IsActive.Value
                ? "MenuがEnterを消費しました。Gameplayには届きません。"
                : gameplayConfirm.IsActive.Value
                    ? "Menuは停止中なので、GameplayがEnterを受け取りました。"
                    : "Enterを受け取ったコンテキストはありません。";
            source.ReleaseKey(KeyCode.Enter); source.Poll(bus); stack.Update(bus);
        }
        // docs:end input-context-routing

        // docs:begin input-context-suspension
        void ToggleMenu()
        {
            menuSuspended.Value = !menuSuspended.Value;
            stack.SetSuspended(menu, menuSuspended.Value);
            result.Value = menuSuspended.Value
                ? "Menuを停止しました。次のEnterはGameplayへ届きます。"
                : "Menuを再開しました。再び最優先でEnterを処理します。";
        }
        // docs:end input-context-suspension

        return Frame(VStack(14, width: 550)[
            Heading("コンテキストの優先順位", 2),
            Text("InputStackは最後にPushしたコンテキストから評価します。上位のアクションが使用したキーは消費され、下位には渡りません。", 14,
                wrap: TextWrap.Word, width: 540, color: Bind.From(() => UiTheme.T.TextMuted)),
            HStack(10)[
                Button(_ => TapEnter(), "Enterを送る"),
                Button(_ => ToggleMenu(), (Func<string>)(() => menuSuspended.Value ? "Menuを再開" : "Menuを停止"))],
            Card(VStack(6)[
                Text((Func<string>)(() => $"1. Menu: {(menuSuspended.Value ? "停止中" : "有効（最上位）")}"), 14),
                Text("2. Gameplay: 有効（下位）", 14),
                Text($"{result}", 14, wrap: TextWrap.Word, width: 500)])]);
    }

    [Story("Examples/Input/Bindings", Width = 680, Height = 440, Order = 2)]
    public static Widget Bindings()
    {
        // docs:begin input-bindings-setup
        var source = new FakeInputSource();
        var bus = new InputBus();
        var context = new InputContext("Gameplay");
        var jump = context.Add(new ButtonAction("Jump", KeyCode.Space));
        var stack = new InputStack();
        stack.Push(context);
        // docs:end input-bindings-setup

        var current = new Signal<KeyCode>(KeyCode.Space);
        var result = new Signal<string>("現在のJumpはSpaceです。");
        var json = new Signal<string>(Serialize(KeyCode.Space));

        // docs:begin input-bindings-apply
        void ApplyBinding(KeyCode key)
        {
            string serialized = Serialize(key);
            InputBindings loaded = JsonSerializer.Deserialize(serialized, InputBindingsJsonContext.Default.InputBindings)!;
            InputBindingsApplier.Apply(loaded, context);
            current.Value = jump.Keys.Single();
            json.Value = serialized;
            result.Value = $"JSONを読み込み、Jumpを{current.Value}へ再設定しました。";
        }
        // docs:end input-bindings-apply

        // docs:begin input-bindings-simulate
        void Simulate(KeyCode key)
        {
            source.PressKey(key); source.Poll(bus); stack.Update(bus);
            bool activated = jump.IsActive.Value;
            source.ReleaseKey(key); source.Poll(bus); stack.Update(bus);
            result.Value = activated
                ? $"{key}でJumpが発火しました。"
                : $"{key}は現在のJumpバインドではありません。";
        }
        // docs:end input-bindings-simulate

        return Frame(VStack(12, width: 600)[
            Heading("バインディングと再設定", 2),
            Text("アクション名をゲーム側の契約として固定し、物理キーとの対応だけをJSONで保存・読み込みします。", 14,
                wrap: TextWrap.Word, width: 590, color: Bind.From(() => UiTheme.T.TextMuted)),
            HStack(10)[
                Button(_ => ApplyBinding(KeyCode.Space), "JumpをSpaceへ設定"),
                Button(_ => ApplyBinding(KeyCode.Enter), "JumpをEnterへ設定")],
            HStack(10)[
                Button(_ => Simulate(KeyCode.Space), "Spaceを試す"),
                Button(_ => Simulate(KeyCode.Enter), "Enterを試す")],
            Text((Func<string>)(() => $"現在のバインド: Jump → {current.Value}"), 15),
            Text($"{result}", 14, color: Bind.From(() => UiTheme.T.TextMuted)),
            Card(Text($"{json}", 12, wrap: TextWrap.Word, width: 520))]);

        // docs:begin input-bindings-json
        static string Serialize(KeyCode key) => JsonSerializer.Serialize(new InputBindings
        {
            Actions = { ["Jump"] = new InputBindingEntry { Kind = "button", Keys = [key.ToString()] } }
        }, InputBindingsJsonContext.Default.InputBindings);
        // docs:end input-bindings-json
    }
}
