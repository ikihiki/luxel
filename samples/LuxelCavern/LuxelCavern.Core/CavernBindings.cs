using Luxel.Input;

namespace LuxelCavern.Core;

/// <summary>再割当可能なゲーム操作。</summary>
public enum CavernBind { Left, Right, Jump }

/// <summary>リバインド用に「直近に押された生キー」を 1 つ取り出す口。実窓の入力源 (KeyboardSource) が実装する。</summary>
public interface IKeyCapture
{
    /// <summary>直近に押されたキーを取り出してクリアする (無ければ null)。</summary>
    KeyCode? TakePressed();
}

/// <summary>
/// キーバインド (プライマリキー) を <see cref="CavernSettings"/> と <see cref="InputAction"/> の間で橋渡しする純ロジック。
/// 矢印キーは固定セカンダリとして常に束ねる (リバインドをミスっても動ける安全策 + メニューナビと共通)。テスト可能。
/// </summary>
public static class CavernBindings
{
    /// <summary>設定のプライマリキー + 固定セカンダリ (矢印) を移動/ジャンプアクションに反映する。</summary>
    public static void Apply(Axis1DAction move, ButtonAction jump, CavernSettings s)
    {
        move.ButtonPairs.Clear();
        move.ButtonPairs.Add((s.BindRight.Value, s.BindLeft.Value));   // プライマリ (Positive=右, Negative=左)
        move.ButtonPairs.Add((KeyCode.Right, KeyCode.Left));           // 固定セカンダリ (矢印)

        jump.Keys.Clear();
        jump.Keys.Add(s.BindJump.Value);
        jump.Keys.Add(KeyCode.Up);
    }

    /// <summary>指定操作のプライマリキーを再割当する (<see cref="SettingsStore.AutoSave"/> が永続化)。</summary>
    public static void Rebind(CavernSettings s, CavernBind bind, KeyCode key)
    {
        switch (bind)
        {
            case CavernBind.Left: s.BindLeft.Value = key; break;
            case CavernBind.Right: s.BindRight.Value = key; break;
            case CavernBind.Jump: s.BindJump.Value = key; break;
        }
    }

    /// <summary>現在割り当てられているプライマリキー。</summary>
    public static KeyCode Current(CavernSettings s, CavernBind bind) => bind switch
    {
        CavernBind.Left => s.BindLeft.Value,
        CavernBind.Right => s.BindRight.Value,
        _ => s.BindJump.Value,
    };

    /// <summary>UI 表示ラベル。</summary>
    public static string Label(CavernBind bind) => bind switch
    {
        CavernBind.Left => "左移動",
        CavernBind.Right => "右移動",
        _ => "ジャンプ",
    };
}
