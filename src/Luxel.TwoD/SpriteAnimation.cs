namespace Luxel.TwoD;

/// <summary>
/// アトラスのフレーム列 (名前プレフィクス + 連番) を fps で進める決定的アニメーション。
/// フレーム名は <c>Prefix + index</c> (例 <c>"player_run_"</c> + <c>0,1,2,…</c> → <c>"player_run_0"</c>)。
/// 時刻は <see cref="Update"/> で固定 dt を積算し、<see cref="Frame"/> = floor(経過秒 × fps)。
/// wall-clock を持たない — 呼び出し側が固定 dt を渡す (テスト/golden の決定性)。
/// </summary>
public sealed class SpriteAnimation
{
    private float _time;

    public SpriteAnimation(string prefix, int frameCount, float fps, bool loop = true)
    {
        if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount), "フレーム数は 1 以上。");
        if (fps <= 0f) throw new ArgumentOutOfRangeException(nameof(fps), "fps は正。");
        Prefix = prefix;
        FrameCount = frameCount;
        Fps = fps;
        Loop = loop;
    }

    public string Prefix { get; }
    public int FrameCount { get; }
    public float Fps { get; }
    /// <summary>true = 末尾で先頭へ戻る。false = 末尾フレームで停止 (再生一回)。</summary>
    public bool Loop { get; }

    /// <summary>経過時間 (秒)。<see cref="Reset"/> で 0 に戻る。</summary>
    public float Time => _time;

    /// <summary>現在のフレーム番号。</summary>
    public int Frame => FrameAt(_time, Fps, FrameCount, Loop);

    /// <summary>現在のフレーム名 (<see cref="Prefix"/> + <see cref="Frame"/>)。</summary>
    public string FrameName => Prefix + Frame;

    /// <summary>非ループ再生が末尾に達したか。</summary>
    public bool Finished => !Loop && _time * Fps >= FrameCount - 1;

    /// <summary>固定 dt (秒) だけ進める。</summary>
    public void Update(float dt) => _time += dt;

    /// <summary>先頭 (時刻 0) へ戻す。</summary>
    public void Reset() => _time = 0f;

    /// <summary>指定時刻でのフレーム番号 (GPU 非依存の純関数 — テスト用)。
    /// ループ時は剰余で巡回、非ループ時は末尾で飽和。</summary>
    public static int FrameAt(float time, float fps, int frameCount, bool loop)
    {
        int f = (int)MathF.Floor(MathF.Max(0f, time) * fps);
        if (loop) return ((f % frameCount) + frameCount) % frameCount;
        return Math.Clamp(f, 0, frameCount - 1);
    }
}
