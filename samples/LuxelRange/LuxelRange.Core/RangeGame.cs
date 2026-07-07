using System.Numerics;
using Luxel.Settings;

namespace LuxelRange.Core;

/// <summary>ゲームの状態。</summary>
public enum RangeState
{
    /// <summary>タイトル画面。</summary>
    Title,
    /// <summary>プレイ中 (発射できる)。</summary>
    Play,
    /// <summary>結果画面 (スコア + ハイスコア)。</summary>
    Result,
}

/// <summary>
/// 「Luxel Range」のゲームフロー — <see cref="RangeSim"/> (物理・スコア) と <see cref="RangeSettings"/>
/// (ハイスコア永続化) を束ね、Title → Play → Result の状態機械を回す。残弾制: 弾切れ後
/// <see cref="SettleSeconds"/> 待って Result へ (弾/小物が落ち着く猶予)。純ロジック (GPU/実窓非依存)。
/// </summary>
public sealed class RangeGame : IDisposable
{
    /// <summary>弾切れから Result までの待ち秒 (弾/小物の静定猶予)。</summary>
    public const float SettleSeconds = 2f;

    private RangeSim _sim;
    private float _settleTimer = -1f;

    /// <summary>永続設定 (ハイスコア/音量)。</summary>
    public RangeSettings Settings { get; }
    /// <summary>現在の状態。</summary>
    public RangeState State { get; private set; } = RangeState.Title;
    /// <summary>現在のシミュレーション。</summary>
    public RangeSim Sim => _sim;
    /// <summary>現ラウンドの合計スコア。</summary>
    public int Score => _sim.TotalScore;
    /// <summary>ハイスコア。</summary>
    public int HighScore => Settings.HighScore.Value;

    public RangeGame(IFileStore files)
    {
        Settings = new RangeSettings(files);
        _sim = new RangeSim();
    }

    /// <summary>Title/Result → Play。新しいラウンドを開始する (sim を作り直す = 決定的リセット)。</summary>
    public void StartRound()
    {
        _sim.Dispose();
        _sim = new RangeSim();
        _settleTimer = -1f;
        State = RangeState.Play;
    }

    /// <summary>Play 中だけ発射できる。</summary>
    public bool Fire(Vector3 origin, Vector3 direction)
        => State == RangeState.Play && _sim.Fire(origin, direction);

    /// <summary>1 固定ステップ。弾切れ後 <see cref="SettleSeconds"/> 経過で Result へ
    /// (スコア確定 + ハイスコア更新)。</summary>
    public void Step()
    {
        if (State != RangeState.Play) return;
        _sim.StepOnce();
        if (_sim.AmmoLeft == 0 && _settleTimer < 0f) _settleTimer = SettleSeconds;
        if (_settleTimer >= 0f)
        {
            _settleTimer -= RangeSim.FixedDt;
            if (_settleTimer <= 0f)
            {
                State = RangeState.Result;
                Settings.SubmitScore(_sim.TotalScore);
            }
        }
    }

    /// <summary>Result → Title。</summary>
    public void BackToTitle() => State = RangeState.Title;

    public void Dispose() => _sim.Dispose();
}
