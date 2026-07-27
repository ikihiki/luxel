using System.Numerics;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace Luxel.Typography;

/// <summary>
/// OpenTypeフォントのシェーピング、メトリクス、TrueType輪郭を提供する。
/// **シェーピングは HarfBuzz** (カーニング/リガチャ/合字/複雑文字対応 — 単純な1文字1グリフ変換ではない)、
/// アウトラインはOpenTypeのglyf/locaテーブルを直接読む (TrueType輪郭。CFF/OTFは未対応)。
/// 描画backendとの統合は専用adapter assemblyが担う。TTCはfontIndexで番号指定。
/// スレッド所有: シェーピングバッファを再利用するため、1 インスタンスは 1 スレッド (UI 島) から使うこと。
/// </summary>
public sealed class VectorFont : IDisposable
{
    private readonly GCHandle _pin;
    private readonly Blob _blob;
    private readonly Face _face;
    private readonly HbFont _font;
    private readonly HbBuffer _buffer = new();
    private readonly GlyfOutlines _glyf;
    private readonly Dictionary<uint, GlyphOutline?> _cache = new();   // glyph id → 輪郭 (フォント単位, 合成解決済み)
    private ColorGlyphs? _color;            // COLR/CPAL (カラー絵文字)。遅延ロード、無ければ null
    private bool _colorLoaded;
    private readonly Dictionary<uint, ColorLayer[]?> _layerCache = new();   // glyph id → レイヤ列 (非カラーは null)
    private readonly float _ascentUnits;
    private readonly float _heightUnits;   // ascender - descender (スケール規約の分母 — 旧実装と同じ)
    private bool _disposed;

    /// <summary>TextLayout.Get のキャッシュ (フォントはスレッド所有なので島安全)。</summary>
    internal Dictionary<TextLayout.LayoutKey, TextLayout> LayoutCache { get; } = new();

    /// <summary>TTF/TTC を読み込む。TTC (フォントコレクション) は fontIndex で番号指定。</summary>
    public VectorFont(byte[] ttf, int fontIndex = 0)
    {
        _pin = GCHandle.Alloc(ttf, GCHandleType.Pinned);
        _blob = new Blob(_pin.AddrOfPinnedObject(), ttf.Length, MemoryMode.ReadOnly);
        _face = new Face(_blob, fontIndex);   // TTC の面選択は HarfBuzz が行う
        _font = new HbFont(_face);            // 既定スケール = unitsPerEm (座標はフォント単位)
        if (!_font.TryGetHorizontalFontExtents(out FontExtents ext))
            throw new NotSupportedException("フォントの水平メトリクスを取得できません");
        _ascentUnits = ext.Ascender;
        _heightUnits = ext.Ascender - ext.Descender;   // descender は負
        _glyf = new GlyfOutlines(_face);
    }

    public static VectorFont Load(string path, int fontIndex = 0) => new(File.ReadAllBytes(path), fontIndex);

    /// <summary>
    /// システムフォントを探して読み込む (既定はラテン)。既定候補がない環境では、
    /// MSBuild が出力した共有 fonts/BIZUDGothic-Regular.ttf を決定的な fallback として使う。
    /// </summary>
    public static VectorFont LoadSystem(params string[] candidates)
    {
        string[] names = candidates.Length > 0 ? candidates
            : ["arial.ttf", "segoeui.ttf", "consola.ttf", "tahoma.ttf", "verdana.ttf"];
        string? path = FindSystemFont(names);
        if (path is not null) return Load(path);

        if (candidates.Length == 0 && FindManagedFont("BIZUDGothic-Regular.ttf") is { } managed)
            return Load(managed);

        throw new FileNotFoundException("利用可能なフォントが見つかりません: " + string.Join(", ", names));
    }

    /// <summary>日本語フォント (かな/漢字対応) を探し、無ければ管理対象 Noto Sans JP を読み込む。</summary>
    public static VectorFont LoadSystemJapanese()
    {
        string[] names = ["yumin.ttf", "YuGothM.ttc", "meiryo.ttc", "msgothic.ttc", "BIZ-UDGothicR.ttc"];
        string? path = FindSystemFont(names) ?? FindManagedFont("BIZUDGothic-Regular.ttf");
        return path is not null
            ? Load(path)
            : throw new FileNotFoundException("利用可能な日本語フォントが見つかりません: " + string.Join(", ", names));
    }

    private static string? FindSystemFont(IEnumerable<string> names)
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(dir)) return null;
        foreach (string name in names)
        {
            string path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string? FindManagedFont(string fileName)
    {
        string? configured = Environment.GetEnvironmentVariable("LUXEL_FONT_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string configuredPath = Path.Combine(configured, fileName);
            if (File.Exists(configuredPath)) return configuredPath;
        }

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "fonts", fileName);
        return File.Exists(bundledPath) ? bundledPath : null;
    }

    /// <summary>px 高さ (ascent−descent 基準 — 旧実装と同じ規約) → フォント単位のスケール。</summary>
    internal float Scale(float pixelHeight) => pixelHeight / _heightUnits;

    /// <summary>テキストを HarfBuzz でシェーピングする (バッファ再利用 — 所有スレッドからのみ)。</summary>
    private HbBuffer Shape(string text)
    {
        _buffer.ClearContents();
        _buffer.AddUtf16(text);
        _buffer.GuessSegmentProperties();   // 向き/スクリプト/言語を内容から推定
        _font.Shape(_buffer);
        return _buffer;
    }

    /// <summary>テキストの自然サイズ (1 行) を返す。幅=シェーピング後の advance 合計、高さ=ascent−descent (=pixelHeight)。</summary>
    public (float width, float height) Measure(string text, float pixelHeight)
    {
        if (string.IsNullOrEmpty(text)) return (0, pixelHeight);
        float scale = Scale(pixelHeight);
        HbBuffer buf = Shape(text);
        ReadOnlySpan<GlyphPosition> pos = buf.GetGlyphPositionSpan();
        float w = 0;
        foreach (ref readonly GlyphPosition p in pos) w += p.XAdvance;
        return (w * scale, pixelHeight);
    }

    /// <summary>ベースライン位置 (上端からの距離, px)。テキストノードの配置に使う。</summary>
    public float Ascent(float pixelHeight) => _ascentUnits * Scale(pixelHeight);

    // ---- TextLayout 用の内部 API (同一アセンブリ) ----

    /// <summary>1 run をシェーピングし px 単位のグリフ列を返す (バッファ再利用のためコピーを返す)。</summary>
    internal ShapedGlyph[] ShapeRun(string text, float pixelHeight)
    {
        if (string.IsNullOrEmpty(text)) return [];
        float scale = Scale(pixelHeight);
        HbBuffer buf = Shape(text);
        ReadOnlySpan<GlyphInfo> infos = buf.GetGlyphInfoSpan();
        ReadOnlySpan<GlyphPosition> pos = buf.GetGlyphPositionSpan();
        var run = new ShapedGlyph[infos.Length];
        for (int i = 0; i < infos.Length; i++)
            run[i] = new ShapedGlyph(infos[i].Codepoint, (int)infos[i].Cluster,
                pos[i].XAdvance * scale, pos[i].XOffset * scale, pos[i].YOffset * scale,
                pos[i].YAdvance * scale);
        return run;
    }

    /// <summary>コードポイントを収載しているか (フォールバック判定用。.notdef は不収載扱い)。</summary>
    public bool HasGlyph(int codepoint) => _font.TryGetGlyph((uint)codepoint, out uint gid) && gid != 0;

    /// <summary>コードポイント → グリフ ID (cmap 直引き。カラーグリフ検査等)。</summary>
    public bool TryGetGlyph(int codepoint, out uint glyphId)
        => _font.TryGetGlyph((uint)codepoint, out glyphId) && glyphId != 0;

    /// <summary>カラーグリフ (COLR v0) のレイヤ列を取得する。カラー情報が無いグリフは false。
    /// <see cref="ColorLayer.Foreground"/> のレイヤはテキスト色 (呼び出し側の色) で描く。</summary>
    public bool TryGetColorLayers(uint glyphId, out ColorLayer[] layers)
    {
        if (!_colorLoaded)
        {
            _colorLoaded = true;
            _color = ColorGlyphs.TryCreate(_face);
        }
        if (_color is null) { layers = []; return false; }
        if (!_layerCache.TryGetValue(glyphId, out ColorLayer[]? found))
        {
            found = _color.Read(glyphId);
            _layerCache[glyphId] = found;
        }
        layers = found ?? [];
        return found is not null;
    }

    /// <summary>解決済みTrueType輪郭を取得する。描画backend adapterから利用する。</summary>
    internal GlyphOutline? GetOutline(uint glyphId)
    {
        if (_cache.TryGetValue(glyphId, out GlyphOutline? cached)) return cached;
        GlyphOutline? outline = _glyf.Read(glyphId);
        _cache[glyphId] = outline;
        return outline;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buffer.Dispose();
        _font.Dispose();
        _face.Dispose();
        _blob.Dispose();
        if (_pin.IsAllocated) _pin.Free();
    }

    /// <summary>1 グリフの解決済み輪郭 (フォント単位, y-up)。ContourEnds は各輪郭の末尾 index (含む)。</summary>
    internal sealed record GlyphOutline(Vector2[] Points, bool[] OnCurve, int[] ContourEnds);

    /// <summary>
    /// OpenType の glyf/loca/head テーブルを直接読む TrueType 輪郭リーダ。
    /// テーブルは HarfBuzz の <see cref="Face.ReferenceTable"/> から取得する (TTC の面解決込み)。
    /// 合成グリフ (composite) は平行移動 + 2x2 変換を適用して展開する。CFF (OTF) は未対応。
    /// </summary>
    private sealed class GlyfOutlines
    {
        private readonly byte[] _glyf;
        private readonly byte[] _loca;
        private readonly bool _longLoca;
        private readonly int _numGlyphs;

        public GlyfOutlines(Face face)
        {
            byte[]? head = Table(face, "head");
            byte[]? glyf = Table(face, "glyf");
            byte[]? loca = Table(face, "loca");
            byte[]? maxp = Table(face, "maxp");
            if (head is null || glyf is null || loca is null || maxp is null)
                throw new NotSupportedException("TrueType 輪郭 (glyf/loca) を持つフォントのみ対応です (CFF/OTF は未対応)");
            _glyf = glyf;
            _loca = loca;
            _longLoca = ReadI16(head, 50) == 1;   // indexToLocFormat
            _numGlyphs = ReadU16(maxp, 4);
        }

        private static byte[]? Table(Face face, string tag)
        {
            using Blob b = face.ReferenceTable(Tag.Parse(tag));
            return b.Length == 0 ? null : b.AsSpan().ToArray();
        }

        public GlyphOutline? Read(uint glyphId) => Read(glyphId, 0);

        private GlyphOutline? Read(uint glyphId, int depth)
        {
            if (glyphId >= _numGlyphs || depth > 5) return null;
            (int start, int end) = Loca(glyphId);
            if (end <= start || end > _glyf.Length) return null;   // 空グリフ (スペース等)
            ReadOnlySpan<byte> g = _glyf.AsSpan(start, end - start);
            int numContours = ReadI16(g, 0);
            return numContours >= 0 ? ReadSimple(g, numContours) : ReadComposite(g, depth);
        }

        private (int start, int end) Loca(uint gid)
        {
            if (_longLoca)
                return ((int)ReadU32(_loca, (int)gid * 4), (int)ReadU32(_loca, ((int)gid + 1) * 4));
            return (ReadU16(_loca, (int)gid * 2) * 2, ReadU16(_loca, ((int)gid + 1) * 2) * 2);
        }

        private static GlyphOutline? ReadSimple(ReadOnlySpan<byte> g, int numContours)
        {
            if (numContours == 0) return null;
            int p = 10;   // numberOfContours(2) + bbox(8)
            var ends = new int[numContours];
            for (int i = 0; i < numContours; i++) { ends[i] = ReadU16(g, p); p += 2; }
            int nPts = ends[numContours - 1] + 1;
            p += 2 + ReadU16(g, p);   // instructionLength + instructions

            // フラグ (RLE: bit3 = 次バイト回数の繰り返し)
            var flags = new byte[nPts];
            for (int i = 0; i < nPts;)
            {
                byte f = g[p++];
                flags[i++] = f;
                if ((f & 0x08) != 0)
                {
                    int rep = g[p++];
                    for (int r = 0; r < rep && i < nPts; r++) flags[i++] = f;
                }
            }

            var pts = new Vector2[nPts];
            var on = new bool[nPts];
            int x = 0;
            for (int i = 0; i < nPts; i++)
            {
                byte f = flags[i];
                if ((f & 0x02) != 0) { int d = g[p++]; x += (f & 0x10) != 0 ? d : -d; }   // X_SHORT (bit4 = 正)
                else if ((f & 0x10) == 0) { x += ReadI16(g, p); p += 2; }                 // SAME_X でなければ i16
                pts[i].X = x;
                on[i] = (f & 0x01) != 0;
            }
            int y = 0;
            for (int i = 0; i < nPts; i++)
            {
                byte f = flags[i];
                if ((f & 0x04) != 0) { int d = g[p++]; y += (f & 0x20) != 0 ? d : -d; }   // Y_SHORT (bit5 = 正)
                else if ((f & 0x20) == 0) { y += ReadI16(g, p); p += 2; }
                pts[i].Y = y;
            }
            return new GlyphOutline(pts, on, ends);
        }

        private GlyphOutline? ReadComposite(ReadOnlySpan<byte> g, int depth)
        {
            var pts = new List<Vector2>();
            var on = new List<bool>();
            var ends = new List<int>();
            int p = 10;
            while (true)
            {
                int flags = ReadU16(g, p);
                uint childId = ReadU16(g, p + 2);
                p += 4;
                float dx, dy;
                if ((flags & 0x0001) != 0) { dx = ReadI16(g, p); dy = ReadI16(g, p + 2); p += 4; }   // WORDS
                else { dx = (sbyte)g[p]; dy = (sbyte)g[p + 1]; p += 2; }
                if ((flags & 0x0002) == 0) { dx = 0; dy = 0; }   // 点合わせ (非 XY) は未対応 — 平行移動なし扱い

                float a = 1, b = 0, c = 0, d = 1;
                if ((flags & 0x0008) != 0) { a = d = F2Dot14(g, p); p += 2; }                          // WE_HAVE_A_SCALE
                else if ((flags & 0x0040) != 0) { a = F2Dot14(g, p); d = F2Dot14(g, p + 2); p += 4; }  // X_AND_Y_SCALE
                else if ((flags & 0x0080) != 0)                                                        // TWO_BY_TWO
                { a = F2Dot14(g, p); b = F2Dot14(g, p + 2); c = F2Dot14(g, p + 4); d = F2Dot14(g, p + 6); p += 8; }

                if (Read(childId, depth + 1) is GlyphOutline child)
                {
                    int baseIdx = pts.Count;
                    foreach (Vector2 q in child.Points)
                        pts.Add(new Vector2(a * q.X + c * q.Y + dx, b * q.X + d * q.Y + dy));
                    on.AddRange(child.OnCurve);
                    foreach (int e in child.ContourEnds) ends.Add(baseIdx + e);
                }

                if ((flags & 0x0020) == 0) break;   // MORE_COMPONENTS
            }
            return pts.Count == 0 ? null : new GlyphOutline(pts.ToArray(), on.ToArray(), ends.ToArray());
        }

        private static float F2Dot14(ReadOnlySpan<byte> s, int o) => ReadI16(s, o) / 16384f;
        private static ushort ReadU16(ReadOnlySpan<byte> s, int o) => (ushort)((s[o] << 8) | s[o + 1]);
        private static short ReadI16(ReadOnlySpan<byte> s, int o) => (short)((s[o] << 8) | s[o + 1]);
        private static uint ReadU32(ReadOnlySpan<byte> s, int o)
            => (uint)((s[o] << 24) | (s[o + 1] << 16) | (s[o + 2] << 8) | s[o + 3]);
    }

    /// <summary>
    /// COLR v0 + CPAL のリーダ (カラー絵文字 — Segoe UI Emoji 等)。
    /// カラーグリフ = 「色付きレイヤ (通常の輪郭グリフ + パレット色) の重なり」なので、
    /// ベクターパイプラインではレイヤ毎に色付きパスとして描くだけでよい。
    /// COLR v1 (グラデーション等) は未対応 — v1 フォントでも v0 レコードがあればそれを使う。
    /// </summary>
    private sealed class ColorGlyphs
    {
        private readonly byte[] _colr;
        private readonly int _numBase, _baseOff, _layerOff;
        private readonly uint[] _palette;   // パレット 0 の RGBA (little-endian 0xAABBGGRR パック)

        private ColorGlyphs(byte[] colr, byte[] cpal)
        {
            _colr = colr;
            _numBase = ReadU16(colr, 2);
            _baseOff = (int)ReadU32(colr, 4);
            _layerOff = (int)ReadU32(colr, 8);

            int entries = ReadU16(cpal, 2);
            int recordsOff = (int)ReadU32(cpal, 8);
            int first = ReadU16(cpal, 12);   // colorRecordIndices[0] = パレット 0 の先頭レコード
            _palette = new uint[entries];
            for (int i = 0; i < entries; i++)
            {
                int o = recordsOff + (first + i) * 4;   // ColorRecord = BGRA
                byte b = cpal[o], g = cpal[o + 1], r = cpal[o + 2], a = cpal[o + 3];
                _palette[i] = (uint)(r | (g << 8) | (b << 16) | (a << 24));
            }
        }

        public static ColorGlyphs? TryCreate(Face face)
        {
            byte[]? colr = Table(face, "COLR");
            byte[]? cpal = Table(face, "CPAL");
            if (colr is null || cpal is null || colr.Length < 14 || cpal.Length < 16) return null;
            if (ReadU16(cpal, 4) < 1) return null;   // パレット 0 必須
            try { return new ColorGlyphs(colr, cpal); }
            catch { return null; }   // 壊れたテーブルはカラー無し扱い
        }

        /// <summary>base glyph レコード (glyphID 昇順) を二分探索してレイヤ列を返す。無ければ null。</summary>
        public ColorLayer[]? Read(uint glyphId)
        {
            int lo = 0, hi = _numBase - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int o = _baseOff + mid * 6;
                ushort gid = ReadU16(_colr, o);
                if (gid == glyphId)
                {
                    int first = ReadU16(_colr, o + 2);
                    int count = ReadU16(_colr, o + 4);
                    var layers = new ColorLayer[count];
                    for (int i = 0; i < count; i++)
                    {
                        int lr = _layerOff + (first + i) * 4;
                        ushort layerGid = ReadU16(_colr, lr);
                        ushort pal = ReadU16(_colr, lr + 2);
                        layers[i] = pal == 0xFFFF || pal >= _palette.Length
                            ? new ColorLayer(layerGid, 0, Foreground: true)   // 0xFFFF = テキスト色
                            : new ColorLayer(layerGid, _palette[pal], Foreground: false);
                    }
                    return layers;
                }
                if (gid < glyphId) lo = mid + 1; else hi = mid - 1;
            }
            return null;
        }

        private static byte[]? Table(Face face, string tag)
        {
            using Blob b = face.ReferenceTable(Tag.Parse(tag));
            return b.Length == 0 ? null : b.AsSpan().ToArray();
        }

        private static ushort ReadU16(ReadOnlySpan<byte> s, int o) => (ushort)((s[o] << 8) | s[o + 1]);
        private static uint ReadU32(ReadOnlySpan<byte> s, int o)
            => (uint)((s[o] << 24) | (s[o + 1] << 16) | (s[o + 2] << 8) | s[o + 3]);
    }
}

/// <summary>カラーグリフ (COLR v0) のレイヤ 1 枚。<see cref="Foreground"/> = パレット 0xFFFF
/// (テキスト色で描く — レイヤ色は未指定)。Rgba は little-endian 0xAABBGGRR パック。</summary>
public readonly record struct ColorLayer(uint GlyphId, uint Rgba, bool Foreground);

/// <summary>シェーピング済みグリフ 1 つ (px 単位)。Cluster = 元テキストの char index (LTR 単調)。</summary>
internal readonly record struct ShapedGlyph(
    uint GlyphId,
    int Cluster,
    float XAdvance,
    float XOffset,
    float YOffset,
    float YAdvance = 0);
