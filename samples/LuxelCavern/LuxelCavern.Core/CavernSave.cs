using System.Text.Json;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム進捗のセーブデータ (チェックポイント復活位置 + HP/コイン/鍵 + 収集/撃破/通過フラグ)。JSON 往復。
/// 「つづきから」= 新規 <see cref="CavernLevel.CreateSim"/> に <see cref="CavernSim.ApplySave"/> で流し込む。
/// ファイル IO の場所 (%APPDATA%) は呼び出し側 (exe) の責務 — ここは純データ + 直列化。
/// </summary>
public sealed class CavernSave
{
    public int Version { get; set; } = 1;
    public float PlayerX { get; set; }
    public float PlayerY { get; set; }
    public int Hp { get; set; }
    public int Coins { get; set; }
    public int Keys { get; set; }
    public bool[] PickupsCollected { get; set; } = [];
    public bool[] WalkersAlive { get; set; } = [];
    public bool[] FlyersAlive { get; set; } = [];
    public bool[] CheckpointsReached { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this);

    public static CavernSave FromJson(string json)
        => JsonSerializer.Deserialize<CavernSave>(json) ?? throw new FormatException("CavernSave: 不正な JSON。");
}
