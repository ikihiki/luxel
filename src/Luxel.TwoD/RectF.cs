namespace Luxel.TwoD;

/// <summary>軸並行の矩形 (左上 <see cref="X"/>,<see cref="Y"/> + 幅 <see cref="W"/> / 高さ <see cref="H"/>)。
/// カメラのワールド境界やデッドゾーン等に使う。</summary>
public readonly record struct RectF(float X, float Y, float W, float H)
{
    /// <summary>左端 (= X)。</summary>
    public float MinX => X;
    /// <summary>上端 (= Y)。</summary>
    public float MinY => Y;
    /// <summary>右端 (= X + W)。</summary>
    public float MaxX => X + W;
    /// <summary>下端 (= Y + H)。</summary>
    public float MaxY => Y + H;
    /// <summary>中心 X。</summary>
    public float CenterX => X + W * 0.5f;
    /// <summary>中心 Y。</summary>
    public float CenterY => Y + H * 0.5f;

    /// <summary>点が矩形内 (境界含む) か。</summary>
    public bool Contains(float px, float py) => px >= X && px <= X + W && py >= Y && py <= Y + H;

    /// <summary>中心 + サイズから作る。</summary>
    public static RectF FromCenter(float cx, float cy, float w, float h) => new(cx - w * 0.5f, cy - h * 0.5f, w, h);
}
