namespace LuxelCavern.Core;

/// <summary>鳴らす効果音の種類 (sim の出来事に対応)。</summary>
public enum CavernSfxCue { Jump, Land, Coin, Key, Defeat, Hurt, Checkpoint, Clear }

/// <summary>
/// <see cref="CavernSim"/> の 1 ステップ後の状態から「このフレームに鳴らす SE」を割り出す純ロジック
/// (GPU/オーディオ非依存・テスト可能)。前フレームとの差分 (コイン/鍵/HP の増減) と sim の per-step フラグ
/// (ジャンプ/着地/チェックポイント/撃破/クリア) を見る。sim を差し替えたら <see cref="Reset"/> で基準を取り直す
/// (ロード直後などに誤発火しないよう、最初の <see cref="Detect"/> は基準取りのみで無音)。
/// </summary>
public sealed class CavernSfxDetector
{
    private bool _primed;
    private int _prevCoins, _prevKeys, _prevHp;
    private CavernResult _prevResult;

    /// <summary>sim 差し替え時に基準を無効化 (次の <see cref="Detect"/> で取り直す)。</summary>
    public void Reset() => _primed = false;

    /// <summary>このステップに鳴らす SE を <paramref name="into"/> に追加する。</summary>
    public void Detect(CavernSim sim, List<CavernSfxCue> into)
    {
        if (!_primed) { Sync(sim); _primed = true; return; }   // 初回は基準取りのみ (無音)

        if (sim.JumpedThisStep) into.Add(CavernSfxCue.Jump);
        if (sim.LandedThisStep) into.Add(CavernSfxCue.Land);
        for (int i = _prevCoins; i < sim.Coins; i++) into.Add(CavernSfxCue.Coin);
        for (int i = _prevKeys; i < sim.Keys; i++) into.Add(CavernSfxCue.Key);
        for (int i = 0; i < sim.DefeatsThisStep.Count; i++) into.Add(CavernSfxCue.Defeat);
        if (sim.Hp < _prevHp) into.Add(CavernSfxCue.Hurt);
        if (sim.CheckpointThisStep) into.Add(CavernSfxCue.Checkpoint);
        if (sim.Result == CavernResult.Cleared && _prevResult != CavernResult.Cleared) into.Add(CavernSfxCue.Clear);

        Sync(sim);
    }

    private void Sync(CavernSim sim)
    {
        _prevCoins = sim.Coins;
        _prevKeys = sim.Keys;
        _prevHp = sim.Hp;
        _prevResult = sim.Result;
    }
}
