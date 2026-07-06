using System.Numerics;

namespace Luxel.TwoD;

/// <summary>
/// 2D カメラの高レベルコントローラ (ゲームフィールの中核)。ターゲット追従 (デッドゾーン + 指数平滑)、
/// ワールド境界クランプ、画面シェイク、ズームのスムーズ遷移を持つ純ロジック。GPU 非依存・決定的。
///
/// <para>毎フレーム <see cref="Target"/> を与えて <see cref="Update"/> し、<see cref="Camera"/> で
/// <see cref="Camera2D"/> (RetainedCanvas/Scene2D へ渡す affine) を得る。FixedUpdate でも可変 dt でも
/// 正しく動く (dt を貰って指数平滑で進めるだけ — フレームレート非依存)。</para>
/// </summary>
public sealed class CameraRig2D
{
    /// <summary>目標ズーム (実効値 <see cref="EffectiveZoom"/> はこれへ指数平滑)。</summary>
    public float Zoom = 1f;
    /// <summary>追従対象のワールド座標 (毎フレーム外から与える)。</summary>
    public Vector2 Target;
    /// <summary>クランプ範囲 (null = 無制限)。画面端がこの矩形を出ないようにする。</summary>
    public RectF? WorldBounds;
    /// <summary>中央デッドゾーンの幅・高さ (この矩形内のターゲット移動ではカメラは動かない)。</summary>
    public Vector2 Deadzone;
    /// <summary>追従の時定数 (秒)。大きいほど緩慢。0 = 即時。</summary>
    public float Smoothing = 0.15f;
    /// <summary>ズーム追従の時定数 (秒)。0 = 即時。</summary>
    public float ZoomSmoothing = 0.15f;

    private Vector2 _pos;      // 実効カメラ中心 (平滑後・クランプ後)
    private float _zoom = 1f;  // 実効ズーム
    private bool _init;

    // シェイク状態 (固定シード xorshift、Random/wall-clock 非使用)
    private float _shakeAmp, _shakeDur, _shakeRemain;
    private ulong _shakeState = DefaultSeed;
    private Vector2 _shakeOffset;
    private const ulong DefaultSeed = 0x9E3779B97F4A7C15;

    /// <summary>実効カメラ中心 (シェイク前、クランプ後)。診断/テスト用。</summary>
    public Vector2 Position => _pos;
    /// <summary>実効ズーム。</summary>
    public float EffectiveZoom => _zoom;
    /// <summary>現在のシェイクオフセット (診断用)。</summary>
    public Vector2 ShakeOffset => _shakeOffset;
    /// <summary>シェイク中か。</summary>
    public bool IsShaking => _shakeRemain > 0;

    /// <summary>実効値をターゲット/目標ズームへ即座に合わせる (初期化・テレポート時に追従の飛びを消す)。</summary>
    public void SnapToTarget()
    {
        _pos = Target;
        _zoom = Zoom;
        _init = true;
    }

    /// <summary>画面シェイクを加える。<paramref name="seed"/>≠0 で乱数状態を固定 (決定性)。
    /// 重ね掛けは振幅加算・残り時間は長い方を採用。</summary>
    public void Shake(float amplitude, float duration, ulong seed = 0)
    {
        if (amplitude <= 0 || duration <= 0) return;
        _shakeAmp += amplitude;
        _shakeDur = MathF.Max(_shakeDur, duration);
        _shakeRemain = MathF.Max(_shakeRemain, duration);
        if (seed != 0) _shakeState = seed;
    }

    /// <summary>1 フレーム進める。<paramref name="viewportW"/>/<paramref name="viewportH"/> は境界クランプの
    /// 可視ワールドサイズ算出に使う (screen/zoom)。</summary>
    public void Update(float dt, float viewportW, float viewportH)
    {
        if (!_init) { SnapToTarget(); }

        // === 追従: デッドゾーンを出た分だけ goal を動かす ===
        float dzX = Deadzone.X * 0.5f, dzY = Deadzone.Y * 0.5f;
        float goalX = _pos.X, goalY = _pos.Y;
        float dx = Target.X - _pos.X, dy = Target.Y - _pos.Y;
        if (dx > dzX) goalX = Target.X - dzX;
        else if (dx < -dzX) goalX = Target.X + dzX;
        if (dy > dzY) goalY = Target.Y - dzY;
        else if (dy < -dzY) goalY = Target.Y + dzY;

        // === 指数平滑 (フレームレート非依存: 1 - exp(-dt/tau)) ===
        _pos.X += (goalX - _pos.X) * Alpha(dt, Smoothing);
        _pos.Y += (goalY - _pos.Y) * Alpha(dt, Smoothing);
        _zoom += (Zoom - _zoom) * Alpha(dt, ZoomSmoothing);

        // === 境界クランプ ===
        if (WorldBounds is RectF wb) _pos = Clamp(_pos, wb, viewportW, viewportH, _zoom);

        // === シェイク (減衰、duration 後は厳密に 0) ===
        UpdateShake(dt);
    }

    /// <summary>現在状態 → <see cref="Camera2D"/> affine (シェイクオフセットを含む)。</summary>
    public Camera2D Camera(float screenW, float screenH)
        => Camera2D.Create(_zoom, _pos + _shakeOffset, (uint)screenW, (uint)screenH);

    private static float Alpha(float dt, float tau)
        => tau <= 0f ? 1f : 1f - MathF.Exp(-dt / tau);

    private static Vector2 Clamp(Vector2 pos, RectF wb, float vpW, float vpH, float zoom)
    {
        float halfW = vpW * 0.5f / MathF.Max(zoom, 1e-4f);
        float halfH = vpH * 0.5f / MathF.Max(zoom, 1e-4f);
        // ワールドが可視幅より小さい軸は中央固定、そうでなければ画面端が境界を出ないようクランプ
        float x = wb.W <= halfW * 2f ? wb.CenterX : Math.Clamp(pos.X, wb.MinX + halfW, wb.MaxX - halfW);
        float y = wb.H <= halfH * 2f ? wb.CenterY : Math.Clamp(pos.Y, wb.MinY + halfH, wb.MaxY - halfH);
        return new Vector2(x, y);
    }

    private void UpdateShake(float dt)
    {
        if (_shakeRemain <= 0f)
        {
            _shakeOffset = Vector2.Zero;
            _shakeAmp = 0f;
            return;
        }
        _shakeRemain -= dt;
        if (_shakeRemain <= 0f)
        {
            _shakeRemain = 0f;
            _shakeOffset = Vector2.Zero;   // duration 後は厳密に 0 へ戻る
            _shakeAmp = 0f;
            return;
        }
        float decay = _shakeDur > 0f ? _shakeRemain / _shakeDur : 0f;   // 線形減衰 1→0
        _shakeOffset = new Vector2(NextNoise(), NextNoise()) * (_shakeAmp * decay);
    }

    /// <summary>xorshift64 で [-1,1) のノイズを 1 サンプル。</summary>
    private float NextNoise()
    {
        ulong s = _shakeState;
        s ^= s << 13;
        s ^= s >> 7;
        s ^= s << 17;
        _shakeState = s;
        // 上位 53bit を [0,1) に、そして [-1,1) へ
        double unit = (s >> 11) * (1.0 / (1UL << 53));
        return (float)(unit * 2.0 - 1.0);
    }
}
