using System.Numerics;

namespace Luxel.Animation;

/// <summary>
/// AnimationGraph 評価中に各 path → 値 + 値型情報 を蓄積するスクラッチパッド。
/// BlendNode / AddNode が複数ノードからの値を集約する際に「型ごとの正しい lerp/add」を行うため
/// Track 参照を持って型を解決する。
/// </summary>
public sealed class GraphEvaluator
{
    private readonly Dictionary<string, (object Value, TrackBase Source)> _values = new();

    public IEnumerable<string> Paths => _values.Keys;

    /// <summary>path に値を上書き保存 (override セマンティクス)。</summary>
    public void Set(string path, object value, TrackBase source)
    {
        _values[path] = (value, source);
    }

    /// <summary>path の値と source を取得。</summary>
    public bool TryGet(string path, out object value, out TrackBase source)
    {
        if (_values.TryGetValue(path, out var pair))
        {
            value = pair.Value;
            source = pair.Source;
            return true;
        }
        value = default!;
        source = default!;
        return false;
    }

    /// <summary>蓄積された値を target にすべて Apply してクリア。</summary>
    public void FlushTo(IAnimationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        foreach (var kv in _values)
            target.Apply(kv.Key, kv.Value.Value);
        _values.Clear();
    }

    /// <summary>値型ごとの線形補間 (Quaternion は slerp)。BlendNode が使う。</summary>
    public static object Lerp(object a, object b, float t, TrackBase source) => source switch
    {
        Track<float> => (object)((float)a + ((float)b - (float)a) * t),
        Track<Vector2> => Vector2.Lerp((Vector2)a, (Vector2)b, t),
        Track<Vector3> => Vector3.Lerp((Vector3)a, (Vector3)b, t),
        Track<Vector4> => Vector4.Lerp((Vector4)a, (Vector4)b, t),
        Track<Quaternion> => Quaternion.Slerp((Quaternion)a, (Quaternion)b, t),
        Track<uint> => (object)new RgbaTween((uint)a, (uint)b).Lerp(t),
        _ => t < 0.5f ? a : b,   // 不明型はステップ的に切替
    };

    /// <summary>値型ごとの加算 (additive)。Quaternion は q_base * q_add で合成。AddNode が使う。</summary>
    public static object Add(object baseVal, object delta, float weight, TrackBase source) => source switch
    {
        Track<float> => (object)((float)baseVal + (float)delta * weight),
        Track<Vector2> => (Vector2)baseVal + (Vector2)delta * weight,
        Track<Vector3> => (Vector3)baseVal + (Vector3)delta * weight,
        Track<Vector4> => (Vector4)baseVal + (Vector4)delta * weight,
        Track<Quaternion> => Quaternion.Slerp(Quaternion.Identity, (Quaternion)delta, weight) * (Quaternion)baseVal,
        Track<uint> => baseVal,   // 色の additive はあまり意味がないので base 維持
        _ => baseVal,
    };
}
