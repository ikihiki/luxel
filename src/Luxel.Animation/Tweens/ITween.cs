namespace Luxel.Animation;

/// <summary>progress (0..1) → 型 T の値への補間。`begin` と `end` を実装が保持する。純粋関数。</summary>
public interface ITween<T>
{
    T Lerp(float progress);
}
