using System.Numerics;
using Luxel.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// プレイヤーの物理シミュレーション (純ロジック・GPU 非依存・**固定 dt で決定的**)。走る + 重力 + ジャンプを
/// タイルマップの <see cref="TileMap.Sweep"/> 衝突で解決する。実時間ホスト (GameScene/exe) は
/// これを FixedUpdate で回して描画補間し、Gallery ストーリーは固定ステップで回して golden にする。
/// </summary>
public sealed class CavernSim
{
    public TileMap Map { get; }
    /// <summary>プレイヤー AABB の左上 (ワールド px)。</summary>
    public Vector2 PlayerPos;
    public Vector2 PlayerVel;
    public Vector2 PlayerSize { get; }
    /// <summary>接地しているか (下方向の衝突で更新)。</summary>
    public bool OnGround { get; private set; }
    /// <summary>向き (右=true)。スプライト反転/演出用。</summary>
    public bool FacingRight { get; private set; } = true;

    // 調整パラメータ (px/秒)
    public float Gravity = 900f;
    public float MoveSpeed = 115f;
    public float JumpSpeed = 300f;
    /// <summary>落下上限速度 (トンネリング防止)。</summary>
    public float MaxFallSpeed = 640f;

    public CavernSim(TileMap map, Vector2 spawn, Vector2 size)
    {
        Map = map;
        PlayerPos = spawn;
        PlayerSize = size;
    }

    /// <summary>現在のプレイヤー AABB。</summary>
    public RectF PlayerBox => new(PlayerPos.X, PlayerPos.Y, PlayerSize.X, PlayerSize.Y);

    /// <summary>プレイヤー中心 (カメラ追従用)。</summary>
    public Vector2 PlayerCenter => new(PlayerPos.X + PlayerSize.X * 0.5f, PlayerPos.Y + PlayerSize.Y * 0.5f);

    /// <summary>固定 dt で 1 ステップ進める。<paramref name="moveX"/>∈[-1,1]、
    /// <paramref name="jumpPressed"/> = このステップでジャンプ入力があったか (接地時のみ発動)。</summary>
    public void Step(float dt, float moveX, bool jumpPressed)
    {
        moveX = Math.Clamp(moveX, -1f, 1f);
        PlayerVel.X = moveX * MoveSpeed;
        if (moveX > 0.01f) FacingRight = true;
        else if (moveX < -0.01f) FacingRight = false;

        if (jumpPressed && OnGround) PlayerVel.Y = -JumpSpeed;
        PlayerVel.Y = MathF.Min(PlayerVel.Y + Gravity * dt, MaxFallSpeed);

        Vector2 delta = PlayerVel * dt;
        Vector2 moved = Map.Sweep(PlayerBox, delta, out bool hitX, out bool hitY);
        PlayerPos += moved;

        if (hitX) PlayerVel.X = 0f;
        OnGround = hitY && delta.Y > 0f;   // 下方向で衝突 = 着地
        if (hitY) PlayerVel.Y = 0f;
    }
}
