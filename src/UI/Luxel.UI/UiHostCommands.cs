using System.Text.Json;
using Luxel.Diagnostics;

namespace Luxel.UI;

/// <summary>
/// 標準の UI 操作を <see cref="EngineCommands"/> に登録するヘルパ (エンジン側)。
/// リスナー(DevTools)は操作名 + <see cref="JsonElement"/> 引数を Enqueue するだけで UI を駆動できる。
/// 入力は app スレッドの <see cref="EngineCommands.Drain"/> で適用される (単一書込者)。
/// </summary>
public static class UiHostCommands
{
    public static void RegisterDefaults(EngineCommands cmds, UiHost host)
    {
        cmds.Register("click", a => GestureClick(host, a));
        cmds.Register("pointerdown", a => host.PointerDown(F(a, "x"), F(a, "y")));
        cmds.Register("pointerup", a => host.PointerUp(F(a, "x"), F(a, "y")));
        cmds.Register("pointermove", a => host.PointerMove(F(a, "x"), F(a, "y")));
        cmds.Register("context", a => host.ContextClick(F(a, "x"), F(a, "y")));
        cmds.Register("wheel", a => host.Wheel(F(a, "x"), F(a, "y"), F(a, "delta")));
        cmds.Register("keydown", a => host.KeyDown(ParseKey(S(a, "key")), B(a, "shift"), B(a, "ctrl"), B(a, "alt")));
        cmds.Register("char", a => host.Char(S(a, "text")));
        cmds.Register("compose", a => host.Compose(new ImeComposition(S(a, "text"), I(a, "caret"), I(a, "targetStart"), I(a, "targetLen"))));
        cmds.Register("commit", a => host.Commit(S(a, "text")));
        cmds.Register("resize", a => host.Resize(F(a, "w"), F(a, "h")));
        cmds.Register("focusnext", _ => host.FocusNext());
        cmds.Register("focusprev", _ => host.FocusPrev());
    }

    /// <summary>複数 UiHost (<see cref="UiRegistry"/>) 向けの登録。op は単一版と同名で、
    /// 任意の "ui" フィールド (index 数値 or 登録名文字列) が対象 host を選ぶ (省略時は 0 番)。
    /// ui.set は "ui" 名で host 側 (<see cref="UiHost.DebugName"/>) が判別する。</summary>
    public static void RegisterDefaults(EngineCommands cmds, UiRegistry registry)
    {
        UiHost? T(object? a) => Resolve(registry, a);
        cmds.Register("click", a => GestureClick(T(a), a));
        cmds.Register("pointerdown", a => T(a)?.PointerDown(F(a, "x"), F(a, "y")));
        cmds.Register("pointerup", a => T(a)?.PointerUp(F(a, "x"), F(a, "y")));
        cmds.Register("pointermove", a => T(a)?.PointerMove(F(a, "x"), F(a, "y")));
        cmds.Register("context", a => T(a)?.ContextClick(F(a, "x"), F(a, "y")));
        cmds.Register("wheel", a => T(a)?.Wheel(F(a, "x"), F(a, "y"), F(a, "delta")));
        cmds.Register("keydown", a => T(a)?.KeyDown(ParseKey(S(a, "key")), B(a, "shift"), B(a, "ctrl"), B(a, "alt")));
        cmds.Register("char", a => T(a)?.Char(S(a, "text")));
        cmds.Register("compose", a => T(a)?.Compose(new ImeComposition(S(a, "text"), I(a, "caret"), I(a, "targetStart"), I(a, "targetLen"))));
        cmds.Register("commit", a => T(a)?.Commit(S(a, "text")));
        cmds.Register("focusnext", a => T(a)?.FocusNext());
        cmds.Register("focusprev", a => T(a)?.FocusPrev());
        // ui.set は UiHost の購読経路 (Luxel.UiSetRequest) へ — "ui" 名一致の host だけが適用する
        cmds.Register("ui.set", a => { if (a is JsonElement el) EngineDiagnostics.Emit("Luxel.UiSetRequest", el); });
    }

    /// <summary>remote の semantic click を実ポインタと同じ down/up gesture として送る。
    /// DocumentTabs や SurfaceView のように pointer capture を使う対象も通常クリックで動作する。</summary>
    private static void GestureClick(UiHost? host, object? a)
    {
        if (host is null) return;
        float x = F(a, "x"), y = F(a, "y");
        host.PointerDown(x, y);
        host.PointerUp(x, y);
    }

    /// <summary>"ui" フィールド (index 数値 / 名前文字列, 省略時 0 番) で registry から対象 host を引く。</summary>
    private static UiHost? Resolve(UiRegistry registry, object? a)
    {
        JsonElement e = E(a);
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("ui", out JsonElement ui))
            return ui.ValueKind switch
            {
                JsonValueKind.Number when ui.TryGetInt32(out int i) => registry.GetByIndex(i),
                JsonValueKind.String => registry.GetByName(ui.GetString() ?? ""),
                _ => null,
            };
        return registry.GetByIndex(0);
    }

    /// <summary>ブラウザのキー名 (KeyboardEvent.key) を論理 <see cref="Key"/> へ。未知は None。</summary>
    public static Key ParseKey(string name) => name switch
    {
        "Tab" => Key.Tab,
        "Enter" => Key.Enter,
        " " or "Space" or "Spacebar" => Key.Space,
        "Escape" or "Esc" => Key.Escape,
        "ArrowLeft" => Key.Left,
        "ArrowRight" => Key.Right,
        "ArrowUp" => Key.Up,
        "ArrowDown" => Key.Down,
        "Home" => Key.Home,
        "End" => Key.End,
        "Backspace" => Key.Backspace,
        "Delete" or "Del" => Key.Delete,
        "PageUp" => Key.PageUp,
        "PageDown" => Key.PageDown,
        "A" or "a" => Key.A,
        "B" or "b" => Key.B,
        "C" or "c" => Key.C,
        "D" or "d" => Key.D,
        "E" or "e" => Key.E,
        "I" or "i" => Key.I,
        "V" or "v" => Key.V,
        "X" or "x" => Key.X,
        "Y" or "y" => Key.Y,
        "Z" or "z" => Key.Z,
        _ => Key.None,
    };

    // ---- JsonElement 引数読み出し (欠損は既定) ----
    private static JsonElement E(object? a) => a is JsonElement e ? e : default;

    private static float F(object? a, string name)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(name, out JsonElement v)
           && v.TryGetSingle(out float f) ? f : 0f;

    private static int I(object? a, string name)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(name, out JsonElement v)
           && v.TryGetInt32(out int i) ? i : 0;

    private static string S(object? a, string name)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(name, out JsonElement v)
           && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool B(object? a, string name)
        => E(a).ValueKind == JsonValueKind.Object && E(a).TryGetProperty(name, out JsonElement v)
           && (v.ValueKind == JsonValueKind.True);
}
