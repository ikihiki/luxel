namespace LuxelCavern.Core;

/// <summary>
/// ゲーム全体の状態機械 (Title ⇄ Playing ⇄ Paused、Playing → GameOver/Clear)。<see cref="CavernSim"/> を保持し、
/// Playing 中のみ <see cref="Step"/> でシミュレーションを進め、<see cref="CavernSim.Result"/> を状態へ反映する。
/// UI (タイトル/ポーズ/リザルト) の出し分けと入力ルーティングはこの状態を見て行う (純ロジック・テスト可能)。
/// </summary>
public sealed class GameFlow
{
    public GameState State { get; private set; } = GameState.Title;
    public CavernSim? Sim { get; private set; }
    /// <summary>セーブがあるか (タイトルの「つづきから」表示の判定)。</summary>
    public bool HasSave { get; set; }

    /// <summary>新規開始 (最初から)。</summary>
    public void StartNew()
    {
        Sim = CavernLevel.CreateSim();
        State = GameState.Playing;
    }

    /// <summary>セーブから再開 (つづきから)。</summary>
    public void Continue(CavernSave save)
    {
        CavernSim sim = CavernLevel.CreateSim();
        sim.ApplySave(save);
        Sim = sim;
        State = GameState.Playing;
    }

    /// <summary>ポーズ切替 (Esc)。Playing ⇄ Paused のみ。</summary>
    public void TogglePause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
        else if (State == GameState.Paused) State = GameState.Playing;
    }

    /// <summary>タイトルへ戻る (リザルトから)。</summary>
    public void ToTitle() => State = GameState.Title;

    /// <summary>1 ステップ進める (Playing 中のみ)。クリア/死亡でリザルト状態へ遷移。</summary>
    public void Step(float dt, float moveX, bool jumpPressed)
    {
        if (State != GameState.Playing || Sim is null) return;
        Sim.Step(dt, moveX, jumpPressed);
        if (Sim.Result == CavernResult.Cleared) State = GameState.Clear;
        else if (Sim.Result == CavernResult.Dead) State = GameState.GameOver;
    }
}
