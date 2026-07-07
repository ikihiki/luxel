namespace LuxelCavern.Core;

/// <summary>
/// ゲーム全体の状態機械 (ToDo 19 の状態遷移)。Stage A ではまだ <see cref="Title"/> のみ実装。
/// <code>
/// Title ──はじめる──→ Playing ⇄ Paused (Esc)
///   │                    ├─ HP 0 / 落下 → GameOver → Title
///   └─つづきから           └─ 扉に到達  → Clear    → Title
/// </code>
/// </summary>
public enum GameState
{
    Title,
    Settings,
    Playing,
    Paused,
    GameOver,
    Clear,
}
