namespace Luxel.Animation;

/// <summary>CSS steps(n, jumpTerm) と同じ階段関数。</summary>
public sealed class StepsCurve : ICurve
{
    public int Count { get; }
    public StepPosition Position { get; }

    public StepsCurve(int count, StepPosition position = StepPosition.JumpEnd)
    {
        if (count <= 0) throw new ArgumentException("count > 0", nameof(count));
        Count = count;
        Position = position;
    }

    public float Eval(float t01)
    {
        if (t01 <= 0f) return Position == StepPosition.JumpStart || Position == StepPosition.JumpBoth ? 1f / Count : 0f;
        if (t01 >= 1f) return Position == StepPosition.JumpEnd || Position == StepPosition.JumpNone ? 1f - (Position == StepPosition.JumpNone ? 1f / Count : 0f) : 1f;
        float step = MathF.Floor(t01 * Count);
        return Position switch
        {
            StepPosition.JumpStart => (step + 1f) / Count,
            StepPosition.JumpEnd => step / Count,
            StepPosition.JumpBoth => (step + 1f) / (Count + 1f),
            StepPosition.JumpNone => Count <= 1 ? 0f : step / (Count - 1f),
            _ => step / Count,
        };
    }
}

/// <summary>steps() の jump-term。CSS spec 準拠。</summary>
public enum StepPosition { JumpStart, JumpEnd, JumpBoth, JumpNone }
