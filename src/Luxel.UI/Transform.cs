namespace Luxel.UI;

/// <summary>transform 成分プロパティ名 (fluent Transition の対象指定用 — TF)。
/// <code>.Transition(new(0.15f), Transform.ScaleX, Transform.ScaleY)</code></summary>
public static class Transform
{
    public const string TranslateX = "TranslateX";
    public const string TranslateY = "TranslateY";
    public const string ScaleX = "ScaleX";
    public const string ScaleY = "ScaleY";
    public const string Rotate = "Rotate";
}

/// <summary><see cref="Widget.WireTransform"/> の合成ハンドル — コントロール固有の一様スケール
/// (Button.Scale 等) を transform 成分と同じ行列に合成するための口。</summary>
public sealed class TransformHandle
{
    internal float ExtraScale = 1f;
    internal Action? Recompose;

    /// <summary>一様スケールを合成する (ScaleX/Y と乗算)。</summary>
    public void SetExtraScale(float s)
    {
        if (ExtraScale == s) return;
        ExtraScale = s;
        Recompose?.Invoke();
    }
}
