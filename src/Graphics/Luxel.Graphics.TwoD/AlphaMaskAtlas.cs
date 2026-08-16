namespace Luxel.Graphics.TwoD;

/// <summary>R8 アルファマスクのサンプリング方法。</summary>
public enum MaskSampling : uint
{
    /// <summary>最寄りの texel を使用する。ピクセル整列済みグリフ等の 1:1 描画向け。</summary>
    Nearest = 0,
    /// <summary>4 texel を線形補間する。拡縮や小数座標での描画向け。</summary>
    Linear = 1,
}

/// <summary>R8 アトラス内のソース矩形 (texel 単位)。</summary>
public readonly record struct MaskRect(int X, int Y, int Width, int Height);

/// <summary>
/// GPU の bindless バッファに置かれた R8 アルファマスクアトラスを表す。
/// 各 texel は 0=透明、255=完全被覆で、各行は 4 byte 境界へパディングする。
/// GPU バッファの所有権は持たず、アップロード後に <see cref="Bind"/> で参照情報を設定する。
/// </summary>
public sealed class AlphaMaskAtlas
{
    /// <summary>ソース R8 バッファの bindless index。</summary>
    public uint SrcIndex { get; private set; }

    /// <summary>R8 バッファの行ピッチ (byte)。必ず 4 の倍数。</summary>
    public uint RowStrideBytes { get; private set; }

    /// <summary>アトラスの有効幅 (texel)。</summary>
    public int Width { get; private set; }

    /// <summary>アトラスの有効高さ (texel)。</summary>
    public int Height { get; private set; }

    /// <summary>GPU バッファへの参照情報が設定済みか。</summary>
    public bool IsBound { get; private set; }

    /// <summary>密な R8 行を格納するために必要な、4 byte 境界へ丸めた行ピッチを返す。</summary>
    public static int RequiredRowStride(int width)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return checked((width + 3) & ~3);
    }

    /// <summary>
    /// R8 データをアップロード済みの bindless バッファへ関連付ける。
    /// <paramref name="rowStrideBytes"/> を省略した場合は <see cref="RequiredRowStride"/> を使用する。
    /// </summary>
    public void Bind(uint srcIndex, int width, int height, int? rowStrideBytes = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = rowStrideBytes ?? RequiredRowStride(width);
        if (stride < width || (stride & 3) != 0)
            throw new ArgumentOutOfRangeException(nameof(rowStrideBytes), "R8 row stride must cover the width and be a multiple of four bytes.");

        SrcIndex = srcIndex;
        Width = width;
        Height = height;
        RowStrideBytes = checked((uint)stride);
        IsBound = true;
    }

    internal void Validate(MaskRect source)
    {
        if (!IsBound) throw new InvalidOperationException("The alpha-mask atlas must be bound before drawing.");
        if (source.X < 0 || source.Y < 0 || source.Width <= 0 || source.Height <= 0 ||
            source.X > Width - source.Width || source.Y > Height - source.Height)
            throw new ArgumentOutOfRangeException(nameof(source), "The source rectangle must be inside the alpha-mask atlas.");
    }
}
