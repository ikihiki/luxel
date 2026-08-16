using System.Runtime.CompilerServices;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;

namespace Luxel.Typography.TwoD;

/// <summary>フォントラスタライザーが生成した、ベースライン相対の R8 グリフマスク。</summary>
public readonly record struct GlyphMaskBitmap(
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    byte[] Coverage);

/// <summary>プラットフォームのフォントエンジンを使ってヒンティング済みグリフを生成する契約。</summary>
public interface IGlyphMaskRasterizer : IDisposable
{
    bool TryRasterize(
        VectorFont font,
        uint glyphId,
        float physicalPixelHeight,
        byte horizontalPhase,
        out GlyphMaskBitmap bitmap);
}

/// <summary>Scene2D へグリフマスクを追加する描画器。</summary>
public interface IGlyphMaskSceneRenderer
{
    bool TryAppendGlyph(
        VectorFont font,
        Scene2D scene,
        uint glyphId,
        float x,
        float baselineY,
        float pixelHeight,
        uint color);
}

/// <summary>フォント単位で小サイズ文字用のマスク描画器を登録する。</summary>
public static class GlyphMaskRendering
{
    private sealed class Entry(IGlyphMaskSceneRenderer renderer)
    {
        public IGlyphMaskSceneRenderer Renderer { get; } = renderer;
    }

    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<VectorFont, Entry> Renderers = new();

    public static IDisposable Register(VectorFont font, IGlyphMaskSceneRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(renderer);
        lock (Gate)
        {
            Renderers.Remove(font);
            Renderers.Add(font, new Entry(renderer));
        }
        return new Registration(font, renderer);
    }

    internal static bool TryAppend(
        VectorFont font,
        Scene2D scene,
        uint glyphId,
        float x,
        float baselineY,
        float pixelHeight,
        uint color)
    {
        lock (Gate)
            return Renderers.TryGetValue(font, out Entry? entry)
                && entry.Renderer.TryAppendGlyph(font, scene, glyphId, x, baselineY, pixelHeight, color);
    }

    private sealed class Registration(VectorFont font, IGlyphMaskSceneRenderer renderer) : IDisposable
    {
        private VectorFont? _font = font;

        public void Dispose()
        {
            VectorFont? value = Interlocked.Exchange(ref _font, null);
            if (value is null) return;
            lock (Gate)
            {
                if (Renderers.TryGetValue(value, out Entry? entry) && ReferenceEquals(entry.Renderer, renderer))
                    Renderers.Remove(value);
            }
        }
    }
}

/// <summary>
/// 小さいグリフを R8 アトラスへキャッシュして Scene2D のマスクとして描く。
/// アトラスが満杯、ラスタライズ不能、またはしきい値より大きい場合は false を返し、
/// 呼び出し側がベクター輪郭へフォールバックできるようにする。
/// </summary>
public sealed class GpuGlyphMaskRenderer2D : IGlyphMaskSceneRenderer, IDisposable
{
    private const int AtlasSize = 1024;
    private const int Padding = 1;
    private readonly GpuDevice _device;
    private readonly IGlyphMaskRasterizer _rasterizer;
    private readonly Dictionary<GlyphKey, CachedGlyph> _glyphs = new();
    private readonly HashSet<GlyphKey> _emptyGlyphs = [];
    private readonly List<AtlasPage> _pages = [];
    private bool _disposed;

    public GpuGlyphMaskRenderer2D(GpuDevice device, IGlyphMaskRasterizer rasterizer)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
    }

    /// <summary>論理 px から物理 px への倍率。</summary>
    public float RenderScale { get; set; } = 1f;

    /// <summary>マスク描画を使う最大の物理文字高。それより大きい文字はベクター描画する。</summary>
    public float MaxPhysicalPixelHeight { get; set; } = 22f;

    /// <summary>アトラスページの上限。超えたグリフはベクター描画へ戻す。</summary>
    public int MaxPages { get; set; } = 8;

    public bool TryAppendGlyph(
        VectorFont font,
        Scene2D scene,
        uint glyphId,
        float x,
        float baselineY,
        float pixelHeight,
        uint color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float scale = RenderScale;
        float physicalHeight = pixelHeight * scale;
        if (!(scale > 0) || !float.IsFinite(scale) || physicalHeight > MaxPhysicalPixelHeight || physicalHeight < 1)
            return false;

        int heightKey = Math.Max(1, (int)MathF.Round(physicalHeight));
        float xPhysical = x * scale;
        int wholeX = (int)MathF.Floor(xPhysical);
        int phase = (int)MathF.Round((xPhysical - wholeX) * 4f);
        if (phase == 4)
        {
            wholeX++;
            phase = 0;
        }

        var key = new GlyphKey(font, glyphId, heightKey, (byte)phase);
        if (_emptyGlyphs.Contains(key)) return true;
        if (!_glyphs.TryGetValue(key, out CachedGlyph cached))
        {
            if (!_rasterizer.TryRasterize(font, glyphId, heightKey, (byte)phase, out GlyphMaskBitmap bitmap))
                return false;
            if (bitmap.Width == 0 || bitmap.Height == 0)
            {
                _emptyGlyphs.Add(key);
                return true;
            }
            if (bitmap.Coverage.Length != checked(bitmap.Width * bitmap.Height))
                throw new InvalidOperationException("Glyph mask coverage must be tightly packed R8 data.");
            if (!TryCache(bitmap, out cached))
                return false;
            _glyphs.Add(key, cached);
        }

        float left = (wholeX + cached.OriginX) / scale;
        float top = (MathF.Round(baselineY * scale) + cached.OriginY) / scale;
        AlphaMaskAtlas atlas = cached.Page.Atlas;
        scene.MaskSubRect(
            atlas.SrcIndex, atlas.RowStrideBytes,
            checked((uint)cached.Source.X), checked((uint)cached.Source.Y),
            checked((uint)cached.Source.Width), checked((uint)cached.Source.Height),
            left, top, cached.Source.Width / scale, cached.Source.Height / scale,
            color, MaskSampling.Nearest);
        return true;
    }

    private bool TryCache(GlyphMaskBitmap bitmap, out CachedGlyph cached)
    {
        foreach (AtlasPage page in _pages)
            if (page.TryAdd(bitmap, out cached)) return true;

        if (_pages.Count >= MaxPages)
        {
            cached = default;
            return false;
        }

        var added = new AtlasPage(_device, AtlasSize, Padding);
        _pages.Add(added);
        return added.TryAdd(bitmap, out cached);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (AtlasPage page in _pages) page.Dispose();
        _pages.Clear();
        _glyphs.Clear();
        _emptyGlyphs.Clear();
        _rasterizer.Dispose();
    }

    private readonly record struct GlyphKey(VectorFont Font, uint GlyphId, int PhysicalHeight, byte Phase)
    {
        public bool Equals(GlyphKey other) => ReferenceEquals(Font, other.Font) && GlyphId == other.GlyphId
            && PhysicalHeight == other.PhysicalHeight && Phase == other.Phase;
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Font), GlyphId, PhysicalHeight, Phase);
    }

    private readonly record struct CachedGlyph(AtlasPage Page, MaskRect Source, int OriginX, int OriginY);

    private sealed class AtlasPage : IDisposable
    {
        private readonly GpuBuffer _buffer;
        private readonly int _size;
        private readonly int _padding;
        private int _x;
        private int _y;
        private int _rowHeight;

        public AtlasPage(GpuDevice device, int size, int padding)
        {
            _size = size;
            _padding = padding;
            _buffer = device.Malloc((ulong)(size * size), GpuMemoryKind.HostMapped);
            _buffer.Span<byte>(size * size).Clear();
            Atlas = new AlphaMaskAtlas();
            Atlas.Bind(_buffer.BindlessIndex, size, size, size);
        }

        public AlphaMaskAtlas Atlas { get; }

        public bool TryAdd(GlyphMaskBitmap bitmap, out CachedGlyph cached)
        {
            int packedWidth = bitmap.Width + _padding * 2;
            int packedHeight = bitmap.Height + _padding * 2;
            if (packedWidth > _size || packedHeight > _size)
            {
                cached = default;
                return false;
            }
            if (_x + packedWidth > _size)
            {
                _x = 0;
                _y += _rowHeight;
                _rowHeight = 0;
            }
            if (_y + packedHeight > _size)
            {
                cached = default;
                return false;
            }

            int targetX = _x + _padding;
            int targetY = _y + _padding;
            Span<byte> atlas = _buffer.Span<byte>(_size * _size);
            for (int row = 0; row < bitmap.Height; row++)
                bitmap.Coverage.AsSpan(row * bitmap.Width, bitmap.Width)
                    .CopyTo(atlas.Slice((targetY + row) * _size + targetX, bitmap.Width));

            var source = new MaskRect(targetX, targetY, bitmap.Width, bitmap.Height);
            cached = new CachedGlyph(this, source, bitmap.OriginX, bitmap.OriginY);
            _x += packedWidth;
            _rowHeight = Math.Max(_rowHeight, packedHeight);
            return true;
        }

        public void Dispose() => _buffer.Dispose();
    }
}
