using System.Numerics;

namespace LuxelRange.Core;

/// <summary>そのフレームに起きたゲーム上の出来事の種別。演出 (パーティクル) と SE の両方を駆動する。</summary>
public enum RangeEventKind
{
    /// <summary>弾を発射した。</summary>
    Shot,
    /// <summary>薄板ターゲットに命中した。</summary>
    TargetHit,
    /// <summary>動く的 (Fox) に命中した。</summary>
    FoxHit,
    /// <summary>小物をボーナスゾーンへ入れた。</summary>
    BonusScored,
}

/// <summary>ゲームイベント (種別 + 発生位置)。位置は命中パーティクルの発生点に使う。</summary>
public readonly record struct RangeEvent(RangeEventKind Kind, Vector3 Position);

/// <summary>SE キュー (クリップ種別)。exe が実際の音を鳴らす — この対応表は純ロジックでテスト可能。</summary>
public enum RangeSfx
{
    Fire,
    Hit,
    FoxHit,
    Bonus,
}

/// <summary>ゲームイベント列を SE キュー列へ写す (Cavern の SfxDetector と同じ役割の純ロジック)。</summary>
public static class RangeSfxDetector
{
    /// <summary><paramref name="events"/> を SE キューへ写して <paramref name="into"/> に追加する。</summary>
    public static void Detect(IReadOnlyList<RangeEvent> events, List<RangeSfx> into)
    {
        foreach (RangeEvent e in events)
            into.Add(e.Kind switch
            {
                RangeEventKind.Shot => RangeSfx.Fire,
                RangeEventKind.TargetHit => RangeSfx.Hit,
                RangeEventKind.FoxHit => RangeSfx.FoxHit,
                RangeEventKind.BonusScored => RangeSfx.Bonus,
                _ => RangeSfx.Hit,
            });
    }
}
