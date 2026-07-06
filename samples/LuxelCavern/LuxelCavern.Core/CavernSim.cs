using System.Numerics;
using Luxel.TwoD;

namespace LuxelCavern.Core;

/// <summary>収集物 (コイン / 鍵)。</summary>
public struct Pickup
{
    public Vector2 Pos;
    public float Size;
    public bool IsKey;
    public bool Collected;
}

/// <summary>巡回歩行の敵 (床の端で反転)。接触でダメージ、上から踏むと撃破。</summary>
public sealed class Walker
{
    public Vector2 Pos;
    public Vector2 Size = new(14, 14);
    public float VelX;
    public bool Alive = true;
    public float MinX, MaxX;

    public RectF Box => new(Pos.X, Pos.Y, Size.X, Size.Y);
}

/// <summary>ゲームの帰結。</summary>
public enum CavernResult { Playing, Cleared, Dead }

/// <summary>
/// 「Luxel Cavern」のゲームプレイシミュレーション (純ロジック・GPU 非依存・**固定 dt で決定的**)。
/// プレイヤー物理 (走る/重力/ジャンプ + <see cref="TileMap.Sweep"/> 衝突) に加え、収集物 (コイン/鍵)・
/// 鍵 3 個で開く扉 (ゴール)・トゲ (接触ダメージ)・巡回敵 (接触ダメージ / 踏みつけ撃破) を解決する。
/// HP・無敵時間・ノックバック・被弾シェイク要求を持つ。
/// </summary>
public sealed class CavernSim
{
    public TileMap Map { get; }
    public Vector2 PlayerPos;
    public Vector2 PlayerVel;
    public Vector2 PlayerSize { get; }
    public bool OnGround { get; private set; }
    public bool FacingRight { get; private set; } = true;

    // 物理調整
    public float Gravity = 900f, MoveSpeed = 115f, JumpSpeed = 300f, MaxFallSpeed = 640f;

    // アクション状態
    public int Hp = 3, MaxHp = 3;
    public float InvincibleRemain;
    public int Coins, Keys, RequiredKeys = 3;
    public CavernResult Result = CavernResult.Playing;
    /// <summary>このステップで被弾したか (カメラシェイクの発火口。毎ステップ更新)。</summary>
    public bool ShakeRequested { get; private set; }
    /// <summary>マップ下端より下 = 落下死の閾値。</summary>
    public float KillY;

    public readonly List<Pickup> Pickups = new();
    public readonly List<Walker> Enemies = new();
    public Vector2 DoorPos;
    public Vector2 DoorSize = new(20, 32);
    public bool DoorOpen { get; private set; }

    // 被弾パラメータ
    private const float InvincibleTime = 1.0f;
    private const float KnockbackX = 150f, KnockbackY = 230f, StompBounce = 250f;
    private float _knockbackRemain;

    public CavernSim(TileMap map, Vector2 spawn, Vector2 size)
    {
        Map = map;
        PlayerPos = spawn;
        PlayerSize = size;
        KillY = map.Height * map.TileH + 200f;
    }

    public RectF PlayerBox => new(PlayerPos.X, PlayerPos.Y, PlayerSize.X, PlayerSize.Y);
    public Vector2 PlayerCenter => new(PlayerPos.X + PlayerSize.X * 0.5f, PlayerPos.Y + PlayerSize.Y * 0.5f);
    public bool Invincible => InvincibleRemain > 0f;
    public RectF DoorBox => new(DoorPos.X, DoorPos.Y, DoorSize.X, DoorSize.Y);

    public void Step(float dt, float moveX, bool jumpPressed)
    {
        ShakeRequested = false;
        if (Result != CavernResult.Playing) return;
        if (InvincibleRemain > 0f) InvincibleRemain -= dt;

        MovePlayer(dt, moveX, jumpPressed);
        MoveEnemies(dt);
        ResolvePickups();
        ResolveEnemies();
        ResolveSpikes();
        ResolveDoor();

        if (PlayerPos.Y > KillY) { Hp = 0; Result = CavernResult.Dead; }
    }

    private void MovePlayer(float dt, float moveX, bool jumpPressed)
    {
        moveX = Math.Clamp(moveX, -1f, 1f);
        if (_knockbackRemain > 0f)
        {
            _knockbackRemain -= dt;   // ノックバック中は横入力を無視 (押し戻しを活かす)
        }
        else
        {
            PlayerVel.X = moveX * MoveSpeed;
            if (moveX > 0.01f) FacingRight = true;
            else if (moveX < -0.01f) FacingRight = false;
        }

        if (jumpPressed && OnGround) PlayerVel.Y = -JumpSpeed;
        PlayerVel.Y = MathF.Min(PlayerVel.Y + Gravity * dt, MaxFallSpeed);

        Vector2 delta = PlayerVel * dt;
        Vector2 moved = Map.Sweep(PlayerBox, delta, out bool hitX, out bool hitY);
        PlayerPos += moved;
        if (hitX) PlayerVel.X = 0f;
        OnGround = hitY && delta.Y > 0f;   // 下方向で衝突 = 着地
        if (hitY) PlayerVel.Y = 0f;
    }

    private void MoveEnemies(float dt)
    {
        foreach (Walker w in Enemies)
        {
            if (!w.Alive) continue;
            w.Pos.X += w.VelX * dt;
            if (w.Pos.X <= w.MinX) { w.Pos.X = w.MinX; w.VelX = MathF.Abs(w.VelX); }
            else if (w.Pos.X + w.Size.X >= w.MaxX) { w.Pos.X = w.MaxX - w.Size.X; w.VelX = -MathF.Abs(w.VelX); }
        }
    }

    private void ResolvePickups()
    {
        for (int i = 0; i < Pickups.Count; i++)
        {
            Pickup p = Pickups[i];
            if (p.Collected) continue;
            if (!Overlaps(PlayerBox, new RectF(p.Pos.X, p.Pos.Y, p.Size, p.Size))) continue;
            p.Collected = true;
            Pickups[i] = p;
            if (p.IsKey) { Keys++; if (Keys >= RequiredKeys) DoorOpen = true; }
            else Coins++;
        }
    }

    private void ResolveEnemies()
    {
        foreach (Walker w in Enemies)
        {
            if (!w.Alive || !Overlaps(PlayerBox, w.Box)) continue;
            bool stomp = PlayerVel.Y > 0f && PlayerCenter.Y < w.Pos.Y + w.Size.Y * 0.5f;
            if (stomp)
            {
                w.Alive = false;
                PlayerVel.Y = -StompBounce;   // 踏んで跳ねる
            }
            else
            {
                Damage(w.Pos.X + w.Size.X * 0.5f);
            }
        }
    }

    private void ResolveSpikes()
    {
        foreach ((int x, int y) in TilesUnder(PlayerBox))
            if (Map.Get(x, y) == CavernLevel.Spike)
            {
                Damage(PlayerCenter.X);   // 真上から = 水平方向は向きの逆へ
                return;
            }
    }

    private void ResolveDoor()
    {
        if (DoorOpen && Overlaps(PlayerBox, DoorBox)) Result = CavernResult.Cleared;
    }

    private void Damage(float sourceX)
    {
        if (Invincible || Result != CavernResult.Playing) return;
        Hp--;
        InvincibleRemain = InvincibleTime;
        _knockbackRemain = 0.18f;
        float dir = PlayerCenter.X <= sourceX ? -1f : 1f;   // 発生源から離れる向き
        PlayerVel = new Vector2(dir * KnockbackX, -KnockbackY);
        OnGround = false;
        ShakeRequested = true;
        if (Hp <= 0) Result = CavernResult.Dead;
    }

    private IEnumerable<(int X, int Y)> TilesUnder(RectF box)
    {
        int x0 = (int)MathF.Floor(box.MinX / Map.TileW), x1 = (int)MathF.Floor((box.MaxX - 1e-4f) / Map.TileW);
        int y0 = (int)MathF.Floor(box.MinY / Map.TileH), y1 = (int)MathF.Floor((box.MaxY - 1e-4f) / Map.TileH);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Map.InBounds(x, y))
                    yield return (x, y);
    }

    private static bool Overlaps(RectF a, RectF b)
        => a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY;
}
