namespace Luxel.Particles;

/// <summary>
/// パーティクルの SoA (Structure-of-Arrays) 固定長バッファ。GC ゼロ (配列は capacity で確保しきり)。
/// 生存パーティクルは <c>[0, Count)</c> に**発生順で連続**して並ぶ (死亡は積分器が前方詰めで除去 — 順序が
/// 変わらないので描画順 = 発生順が安定し golden が決定的)。配列は将来 GPU バッファへそのまま写せる素の float[]。
/// </summary>
public sealed class ParticleBuffer
{
    public ParticleBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "容量は正。");
        Capacity = capacity;
        PosX = new float[capacity];
        PosY = new float[capacity];
        PosZ = new float[capacity];
        VelX = new float[capacity];
        VelY = new float[capacity];
        VelZ = new float[capacity];
        Age = new float[capacity];
        LifeMax = new float[capacity];
        Size = new float[capacity];
    }

    public int Capacity { get; }
    /// <summary>生存数 (連続領域 [0,Count) が有効)。</summary>
    public int Count { get; internal set; }

    public float[] PosX { get; }
    public float[] PosY { get; }
    public float[] PosZ { get; }
    public float[] VelX { get; }
    public float[] VelY { get; }
    public float[] VelZ { get; }
    /// <summary>経過秒。</summary>
    public float[] Age { get; }
    /// <summary>寿命 (秒)。Age ≥ LifeMax で死亡。</summary>
    public float[] LifeMax { get; }
    /// <summary>放出時にサンプルした基本サイズ (寿命カーブが無いとき使う)。</summary>
    public float[] Size { get; }

    /// <summary>全消去 (再利用)。</summary>
    public void Clear() => Count = 0;
}
