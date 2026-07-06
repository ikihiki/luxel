using System.Numerics;

namespace Luxel.Particles;

/// <summary>生存パーティクルの SoA span を受け取り速度等を加工するフック (乱流・引力等はゲーム側で実装する)。</summary>
public delegate void ParticleForce(ParticleSpans spans, float dt);

/// <summary>フォースフック用に生存領域だけを切り出した SoA span。<see cref="Count"/> 個が有効。</summary>
public readonly ref struct ParticleSpans
{
    internal ParticleSpans(ParticleBuffer b)
    {
        Count = b.Count;
        PosX = b.PosX.AsSpan(0, b.Count);
        PosY = b.PosY.AsSpan(0, b.Count);
        PosZ = b.PosZ.AsSpan(0, b.Count);
        VelX = b.VelX.AsSpan(0, b.Count);
        VelY = b.VelY.AsSpan(0, b.Count);
        VelZ = b.VelZ.AsSpan(0, b.Count);
        Age = b.Age.AsSpan(0, b.Count);
        LifeMax = b.LifeMax.AsSpan(0, b.Count);
    }

    public readonly int Count;
    public readonly Span<float> PosX, PosY, PosZ, VelX, VelY, VelZ, Age, LifeMax;
}

/// <summary>
/// 標準パーティクルシステム。エミッタ (<see cref="Emit"/> バースト / <see cref="SetEmission"/> 連続) +
/// 寿命/速度/重力/抗力を <see cref="IParticleSimulator"/> で毎ステップ積分する。座標は Vector3
/// (2D は z=0)。決定的 (固定シード xorshift + 固定 dt)。描画は各バックエンド (.TwoD / .ThreeD) が
/// <see cref="Buffer"/> を読んで行う — このクラスは描画非依存。
/// </summary>
public sealed class ParticleSystem
{
    private readonly ParticleBuffer _buffer;
    private readonly IParticleSimulator _sim;
    private Xorshift64 _rng;
    private Vector3 _emitPos;
    private float _emitRate;
    private float _emitAccum;
    private uint _emitTint = 0xFFFFFFFF;

    public ParticleSystem(ParticleConfig config, int capacity, ulong seed, IParticleSimulator? simulator = null)
    {
        Config = config;
        _buffer = new ParticleBuffer(capacity);
        _rng = new Xorshift64(seed);
        _sim = simulator ?? new CpuParticleSimulator();
    }

    /// <summary>現在の設定 (差し替え可 — ライブ編集で使う)。</summary>
    public ParticleConfig Config { get; set; }

    /// <summary>生存数。</summary>
    public int Alive => _buffer.Count;
    public int Capacity => _buffer.Capacity;

    /// <summary>描画バックエンドが読む SoA バッファ。</summary>
    public ParticleBuffer Buffer => _buffer;

    /// <summary>毎ステップ積分前に速度等を加工するフック (省略可)。</summary>
    public ParticleForce? Forces { get; set; }

    /// <summary>指定位置から <paramref name="count"/> 個を即時放出する (容量超過分は無視 — 発生順を崩さない)。
    /// <paramref name="tint"/> は per-particle 色 (設定色に乗算、既定 白 = 変化なし) — 1 システムから色違いのバーストを出せる。</summary>
    public void Emit(Vector3 pos, int count, uint tint = 0xFFFFFFFF)
    {
        for (int i = 0; i < count; i++)
        {
            if (!Spawn(pos, tint)) break;
        }
    }

    /// <summary>位置 <paramref name="pos"/> から毎秒 <paramref name="rate"/> 個の連続放出を設定する
    /// (<see cref="Update"/> で dt を積算して放出)。rate=0 で停止。<paramref name="tint"/> は per-particle 色。</summary>
    public void SetEmission(Vector3 pos, float rate, uint tint = 0xFFFFFFFF)
    {
        _emitPos = pos;
        _emitRate = MathF.Max(0f, rate);
        _emitTint = tint;
    }

    /// <summary>連続放出を止める (発生済みは寿命まで残る)。</summary>
    public void StopEmission() => _emitRate = 0f;

    /// <summary>1 ステップ進める: 連続放出 → フォースフック → 積分 (寿命切れ除去)。</summary>
    public void Update(float dt)
    {
        if (_emitRate > 0f)
        {
            _emitAccum += _emitRate * dt;
            int n = (int)_emitAccum;
            _emitAccum -= n;
            for (int i = 0; i < n; i++)
                if (!Spawn(_emitPos, _emitTint)) { _emitAccum = 0f; break; }
        }

        Forces?.Invoke(new ParticleSpans(_buffer), dt);
        _sim.Step(_buffer, Config, dt);
    }

    /// <summary>全消去。</summary>
    public void Clear() => _buffer.Clear();

    private bool Spawn(Vector3 pos, uint tint)
    {
        if (_buffer.Count >= _buffer.Capacity) return false;
        int i = _buffer.Count++;

        float life = MathF.Max(1e-4f, Config.Life.Sample(ref _rng));
        float speed = Config.Speed.Sample(ref _rng);
        float size = Config.Size.Sample(ref _rng);

        float vx, vy, vz;
        if (Config.Spherical)
        {
            // +Y 軸まわりの円錐内 (半角 = Spread、π で全球) の一様方向 (3D バースト用)
            float cosMin = MathF.Cos(MathF.Min(MathF.PI, Config.SpreadRadians));
            float cosTheta = 1f - _rng.NextFloat() * (1f - cosMin);
            float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
            float phi = _rng.NextFloat() * MathF.Tau;
            vx = sinTheta * MathF.Cos(phi) * speed;
            vy = cosTheta * speed;
            vz = sinTheta * MathF.Sin(phi) * speed;
        }
        else
        {
            // XY 平面: BaseAngle ± Spread
            float angle = Config.BaseAngle + _rng.NextSigned() * Config.SpreadRadians;
            vx = MathF.Cos(angle) * speed;
            vy = MathF.Sin(angle) * speed;
            vz = 0f;
        }

        _buffer.PosX[i] = pos.X;
        _buffer.PosY[i] = pos.Y;
        _buffer.PosZ[i] = pos.Z;
        _buffer.VelX[i] = vx;
        _buffer.VelY[i] = vy;
        _buffer.VelZ[i] = vz;
        _buffer.Age[i] = 0f;
        _buffer.LifeMax[i] = life;
        _buffer.Size[i] = size;
        _buffer.Tint[i] = tint;
        return true;
    }
}
