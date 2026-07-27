namespace Luxel.Mathematics;

/// <summary>
/// 割り当てなしで動作する決定的xorshift64疑似乱数生成器。
/// 同じseedから同じ系列を生成し、0 seedは既定seedへ丸める。
/// </summary>
public struct Xorshift64
{
    private ulong _state;
    private const ulong DefaultSeed = 0x9E3779B97F4A7C15;

    public Xorshift64(ulong seed) => _state = seed == 0 ? DefaultSeed : seed;

    /// <summary>次の 64bit。</summary>
    public ulong NextULong()
    {
        ulong s = _state;
        s ^= s << 13;
        s ^= s >> 7;
        s ^= s << 17;
        _state = s;
        return s;
    }

    /// <summary>[0,1) の float (上位 53bit)。</summary>
    public float NextFloat() => (float)((NextULong() >> 11) * (1.0 / (1UL << 53)));

    /// <summary>[min,max) の一様乱数。</summary>
    public float NextRange(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>[-1,1) の float。</summary>
    public float NextSigned() => NextFloat() * 2f - 1f;
}
