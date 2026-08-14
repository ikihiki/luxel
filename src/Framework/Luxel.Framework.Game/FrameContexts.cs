namespace Luxel.Framework.Game;

/// <summary>Fixed-step simulation context. Variable frame delta is intentionally unavailable.</summary>
public readonly record struct FixedUpdateContext(
    long Frame,
    double TotalSeconds,
    float FixedDeltaSeconds);

public readonly record struct UpdateContext(FrameTime Time);
