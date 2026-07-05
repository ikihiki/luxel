using System.Numerics;

namespace Luxel.Animation;

/// <summary>float 線形補間。</summary>
public readonly struct FloatTween : ITween<float>
{
    public float Begin { get; }
    public float End { get; }
    public FloatTween(float begin, float end) { Begin = begin; End = end; }
    public float Lerp(float p) => Begin + (End - Begin) * p;
}

/// <summary>Vector2 線形補間。</summary>
public readonly struct Vector2Tween : ITween<Vector2>
{
    public Vector2 Begin { get; }
    public Vector2 End { get; }
    public Vector2Tween(Vector2 begin, Vector2 end) { Begin = begin; End = end; }
    public Vector2 Lerp(float p) => Vector2.Lerp(Begin, End, p);
}

/// <summary>Vector3 線形補間。</summary>
public readonly struct Vector3Tween : ITween<Vector3>
{
    public Vector3 Begin { get; }
    public Vector3 End { get; }
    public Vector3Tween(Vector3 begin, Vector3 end) { Begin = begin; End = end; }
    public Vector3 Lerp(float p) => Vector3.Lerp(Begin, End, p);
}

/// <summary>Vector4 (color RGBA 等) 線形補間。</summary>
public readonly struct Vector4Tween : ITween<Vector4>
{
    public Vector4 Begin { get; }
    public Vector4 End { get; }
    public Vector4Tween(Vector4 begin, Vector4 end) { Begin = begin; End = end; }
    public Vector4 Lerp(float p) => Vector4.Lerp(Begin, End, p);
}

/// <summary>uint カラー (RGBA 各 8bit) を Vector4 経由で補間する。</summary>
public readonly struct RgbaTween : ITween<uint>
{
    private readonly Vector4 _begin, _end;
    public RgbaTween(uint begin, uint end)
    {
        _begin = ToVec(begin);
        _end = ToVec(end);
    }
    public uint Lerp(float p) => FromVec(Vector4.Lerp(_begin, _end, p));
    private static Vector4 ToVec(uint c) => new(
        (c & 0xffu) / 255f,
        ((c >> 8) & 0xffu) / 255f,
        ((c >> 16) & 0xffu) / 255f,
        ((c >> 24) & 0xffu) / 255f);
    private static uint FromVec(Vector4 v)
    {
        uint r = (uint)Math.Clamp(v.X * 255f + 0.5f, 0f, 255f);
        uint g = (uint)Math.Clamp(v.Y * 255f + 0.5f, 0f, 255f);
        uint b = (uint)Math.Clamp(v.Z * 255f + 0.5f, 0f, 255f);
        uint a = (uint)Math.Clamp(v.W * 255f + 0.5f, 0f, 255f);
        return r | (g << 8) | (b << 16) | (a << 24);
    }
}

/// <summary>Quaternion を slerp で補間 (回転の最短経路)。</summary>
public readonly struct QuaternionTween : ITween<Quaternion>
{
    public Quaternion Begin { get; }
    public Quaternion End { get; }
    public QuaternionTween(Quaternion begin, Quaternion end) { Begin = begin; End = end; }
    public Quaternion Lerp(float p) => Quaternion.Slerp(Begin, End, p);
}
