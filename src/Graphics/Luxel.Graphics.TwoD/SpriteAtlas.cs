using System.Text.Json;

namespace Luxel.Graphics.TwoD;

/// <summary>アトラス内の 1 スプライト: ソーステクスチャ上の px 矩形 + ピボット(px, スプライト左上基準)。
/// ピボットは描画基準点 — <see cref="Scene2D.DrawSprite"/> は指定ワールド座標にこの点を合わせる
/// (足元 = (w/2, h) 等、キャラの接地点合わせに使う)。</summary>
public readonly record struct SpriteRect(int X, int Y, int W, int H, float PivotX, float PivotY)
{
    /// <summary>ピボット省略時は左上 (0,0)。</summary>
    public SpriteRect(int x, int y, int w, int h) : this(x, y, w, h, 0f, 0f) { }
}

/// <summary>
/// 1 テクスチャ (アトラス) に複数スプライトを詰め、名前 → <see cref="SpriteRect"/> で参照するデータ。
/// JSON 定義 (<see cref="FromJson"/>) を読む側に徹し、パッキングはしない (外部ツールの出力を使う)。
/// <para>ソースの GPU 情報 (<see cref="SrcIndex"/> / <see cref="SrcStride"/> / <see cref="Width"/>) は
/// テクスチャを bindless バッファへアップロードした後に <see cref="Bind"/> で注入する — アトラスデータ自体は
/// GPU 非依存でロード/テスト可能。</para>
/// </summary>
public sealed class SpriteAtlas
{
    private readonly Dictionary<string, SpriteRect> _sprites;

    public SpriteAtlas(string textureUri, IEnumerable<KeyValuePair<string, SpriteRect>> sprites)
    {
        TextureUri = textureUri;
        _sprites = new Dictionary<string, SpriteRect>(sprites);
    }

    /// <summary>スプライトが詰められたソーステクスチャの uri (別途ロード/アップロードする)。</summary>
    public string TextureUri { get; }

    /// <summary>名前 → スプライト矩形。</summary>
    public IReadOnlyDictionary<string, SpriteRect> Sprites => _sprites;

    /// <summary>登録スプライト名の列挙。</summary>
    public IEnumerable<string> Names => _sprites.Keys;

    // ---- GPU バインド (テクスチャアップロード後に Bind で設定) ----

    /// <summary>ソース RGBA8 バッファの bindless index。</summary>
    public uint SrcIndex { get; private set; }
    /// <summary>ソースの行ピッチ (px) = アトラス全幅。</summary>
    public uint SrcStride { get; private set; }
    /// <summary>アトラス全体の幅 (px)。</summary>
    public int Width { get; private set; }
    /// <summary>アトラス全体の高さ (px)。</summary>
    public int Height { get; private set; }

    /// <summary>テクスチャを密 (行ピッチ = 幅) に bindless バッファへアップロードした後、その index と寸法を注入する。</summary>
    public void Bind(uint srcIndex, int atlasWidth, int atlasHeight)
    {
        SrcIndex = srcIndex;
        Width = atlasWidth;
        Height = atlasHeight;
        SrcStride = (uint)atlasWidth;
    }

    public SpriteRect this[string name] => _sprites[name];
    public bool TryGet(string name, out SpriteRect rect) => _sprites.TryGetValue(name, out rect);
    public bool Contains(string name) => _sprites.ContainsKey(name);

    /// <summary>スプライトを (ピボットを基準ワールド座標に合わせ) <paramref name="scale"/> 倍で描いたときの
    /// dest 矩形を返す (GPU 非依存 — レイアウト計算/テスト用)。</summary>
    public RectF DestRect(string name, float x, float y, float scale = 1f)
    {
        SpriteRect r = _sprites[name];
        return new RectF(x - r.PivotX * scale, y - r.PivotY * scale, r.W * scale, r.H * scale);
    }

    /// <summary>
    /// JSON 定義からアトラスを構築する。形式:
    /// <code>{ "texture": "sprites.png",
    ///   "sprites": { "player_idle_0": { "x":0,"y":0,"w":32,"h":32,"px":16,"py":32 } } }</code>
    /// <c>px</c>/<c>py</c> (ピボット) は省略可 (既定 0)。
    /// </summary>
    public static SpriteAtlas FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        string texture = root.TryGetProperty("texture", out JsonElement tex) && tex.ValueKind == JsonValueKind.String
            ? tex.GetString()!
            : throw new FormatException("SpriteAtlas JSON: 必須プロパティ \"texture\" がありません。");

        var sprites = new List<KeyValuePair<string, SpriteRect>>();
        if (root.TryGetProperty("sprites", out JsonElement spr) && spr.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in spr.EnumerateObject())
            {
                JsonElement e = p.Value;
                int x = ReqInt(e, "x", p.Name), y = ReqInt(e, "y", p.Name);
                int w = ReqInt(e, "w", p.Name), h = ReqInt(e, "h", p.Name);
                float px = OptFloat(e, "px"), py = OptFloat(e, "py");
                sprites.Add(new(p.Name, new SpriteRect(x, y, w, h, px, py)));
            }
        }
        return new SpriteAtlas(texture, sprites);

        static int ReqInt(JsonElement e, string prop, string sprite)
            => e.TryGetProperty(prop, out JsonElement v) && v.TryGetInt32(out int i)
                ? i
                : throw new FormatException($"SpriteAtlas JSON: スプライト \"{sprite}\" に整数プロパティ \"{prop}\" がありません。");

        static float OptFloat(JsonElement e, string prop)
            => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : 0f;
    }
}
