using System.Numerics;

public sealed class Player
{
    public Vector2 Position { get; set; } = new(96, 112);
    public float Speed { get; set; } = 180;

    public void Tick(float deltaSeconds) => Position += Vector2.UnitX * Speed * deltaSeconds;
}
