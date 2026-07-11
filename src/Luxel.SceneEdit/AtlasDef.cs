using System.Text.Json.Nodes;

namespace Luxel.SceneEdit;

/// <summary>
/// スプライトアトラス/タイルセットの定義 (`*.atlas.json`、ADR-0015)。画像 (res:// の png) を
/// **均等グリッド**でタイルに切る最小形 — タイル番号は左上から行優先で 1 始まり (0 = 空の予約)。
/// SceneCompiler (GE-3) がこれから TileSet/SpriteAtlas を構築する。名前付きスプライト矩形や
/// 非均等アトラスは必要になったら足す。プロパティは PropertyGrid で直接編集できる形 (public get/set)。
/// </summary>
public sealed class AtlasDef
{
    /// <summary>元画像への res:// 参照。</summary>
    public string Image { get; set; } = "";

    /// <summary>タイル 1 個の px 幅。</summary>
    public int TileWidth { get; set; } = 16;

    /// <summary>タイル 1 個の px 高さ。</summary>
    public int TileHeight { get; set; } = 16;
}

/// <summary><see cref="AtlasDef"/> ⇄ JSON の決定的往復 (整形規則は <see cref="SceneJson"/> と同じ)。</summary>
public static class AtlasDefJson
{
    public static string Serialize(AtlasDef a)
    {
        var root = new JsonObject
        {
            ["image"] = a.Image,
            ["tileWidth"] = a.TileWidth,
            ["tileHeight"] = a.TileHeight,
        };
        return root.ToJsonString(SceneJson.Options) + "\n";
    }

    public static AtlasDef Deserialize(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("atlas JSON のルートがオブジェクトでない");
        string image = (string?)root["image"] ?? throw new FormatException("image が無い");
        if (image.Length > 0) _ = ResPath.Resolve(image);   // 空は「未設定」を許す (エディタで後から埋める)
        int tw = (int?)root["tileWidth"] ?? 16;
        int th = (int?)root["tileHeight"] ?? 16;
        if (tw <= 0 || th <= 0) throw new FormatException($"タイルサイズが不正: {tw}x{th}");
        return new AtlasDef { Image = image, TileWidth = tw, TileHeight = th };
    }
}
