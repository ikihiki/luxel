namespace Luxel.Input;

/// <summary>
/// 一群のアクションをまとめる論理的な入力マップ (例: "Menu", "Gameplay")。
/// <see cref="InputStack"/> に Push すると Update 対象になる。
/// 同じキーが上位 context にもある場合、上位が active になった時点で下位は入力を受け取らない (consume 伝播)。
/// </summary>
public sealed class InputContext
{
    public string Name { get; }
    public List<InputAction> Actions { get; } = new();

    public InputContext(string name) { Name = name; }

    /// <summary>action を追加して返す (chainable)。</summary>
    public T Add<T>(T action) where T : InputAction
    {
        Actions.Add(action);
        return action;
    }
}
