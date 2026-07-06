namespace Luxel.Particles;

/// <summary>
/// 決定的 xorshift64 乱数 (StrudelKit 方式)。<see cref="System.Random"/> は使わない
/// (割り当てゼロ + シード注入で golden 決定的)。0 シードは既定シードへ丸める。
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
