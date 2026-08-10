using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Luxel.SceneEdit;

/// <summary>
/// <see cref="SceneDoc"/> ⇄ JSON の**決定的**往復 (ADR-0015)。決定性の規則:
/// キー順固定 (コンポーネントフィールドはモデル側で名前順ソート済み)・インデント 2・
/// 改行 LF・非 ASCII 素通し (日本語名が読める)・数値は最短往復表現。
/// 値は形ベース (<see cref="SceneValueKind"/>) でそのまま書く/読むため、スキーマに無い
/// コンポーネントも劣化なく往復する (未知保全)。タイルレイヤのセルは行ごとの CSV 文字列
/// (git diff が行単位で読める)。
/// </summary>
public static class SceneJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(SceneDoc doc)
    {
        var entities = new JsonArray();
        foreach (SceneEntity e in doc.Entities)
        {
            var comps = new JsonArray();
            foreach (SceneComponent c in e.Components)
            {
                var o = new JsonObject { ["type"] = c.Type };
                foreach (SceneField f in c.Fields) o[f.Name] = WriteValue(f.Value);
                comps.Add(o);
            }
            entities.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["name"] = e.Name,
                ["components"] = comps,
            });
        }
        var layers = new JsonArray();
        foreach (TileLayer l in doc.TileLayers)
        {
            var rows = new JsonArray();
            for (int y = 0; y < l.Height; y++)
                rows.Add(string.Join(',', Enumerable.Range(0, l.Width).Select(x => l.Cell(x, y))));
            layers.Add(new JsonObject
            {
                ["id"] = l.Id,
                ["name"] = l.Name,
                ["tileSet"] = l.TileSet,
                ["cellSize"] = l.CellSize,
                ["width"] = l.Width,
                ["height"] = l.Height,
                ["cells"] = rows,
            });
        }
        var root = new JsonObject
        {
            ["space"] = doc.Space == SceneSpace.TwoD ? "2d" : "3d",
            ["entities"] = entities,
            ["tileLayers"] = layers,
        };
        return root.ToJsonString(Options) + "\n";
    }

    public static SceneDoc Deserialize(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("シーン JSON のルートがオブジェクトでない");
        string space = (string?)root["space"] ?? throw new FormatException("space が無い");
        SceneSpace sp = space switch
        {
            "2d" => SceneSpace.TwoD,
            "3d" => SceneSpace.ThreeD,
            _ => throw new FormatException($"未知の space: {space}"),
        };
        var entities = new List<SceneEntity>();
        foreach (JsonNode? en in root["entities"] as JsonArray ?? [])
        {
            var e = (JsonObject)en!;
            var comps = new List<SceneComponent>();
            foreach (JsonNode? cn in e["components"] as JsonArray ?? [])
            {
                var c = (JsonObject)cn!;
                string type = (string?)c["type"] ?? throw new FormatException("コンポーネントに type が無い");
                var fields = new List<SceneField>();
                foreach (KeyValuePair<string, JsonNode?> kv in c)
                {
                    if (kv.Key == "type") continue;
                    fields.Add(new SceneField(kv.Key, ReadValue(kv.Value)));
                }
                comps.Add(SceneComponent.Of(type, fields));
            }
            entities.Add(SceneEntity.Of(
                (int)(e["id"] ?? throw new FormatException("エンティティに id が無い")),
                (string?)e["name"] ?? "",
                comps));
        }
        var layers = new List<TileLayer>();
        foreach (JsonNode? ln in root["tileLayers"] as JsonArray ?? [])
        {
            var l = (JsonObject)ln!;
            int width = (int)l["width"]!;
            int height = (int)l["height"]!;
            var cells = new int[width * height];
            var rows = (JsonArray)l["cells"]!;
            if (rows.Count != height) throw new FormatException($"cells 行数 {rows.Count} が height {height} に一致しない");
            for (int y = 0; y < height; y++)
            {
                string[] cols = ((string)rows[y]!).Split(',');
                if (cols.Length != width) throw new FormatException($"cells 行 {y} の列数 {cols.Length} が width {width} に一致しない");
                for (int x = 0; x < width; x++) cells[y * width + x] = int.Parse(cols[x]);
            }
            layers.Add(TileLayer.Of((int)l["id"]!, (string?)l["name"] ?? "", (string?)l["tileSet"] ?? "",
                (float)l["cellSize"]!, width, height, cells));
        }
        return SceneDoc.Of(sp, entities, layers);
    }

    // ---- 値 (形ベース) ----

    internal static JsonNode? WriteValue(SceneValue v)
    {
        (double x, double y, double z, double w) = v.Components;
        return v.Kind switch
        {
            SceneValueKind.Bool => JsonValue.Create(v.AsBool()),
            SceneValueKind.Number => JsonValue.Create(x),
            SceneValueKind.Text => JsonValue.Create(v.AsText()),
            SceneValueKind.Vec2 => new JsonArray(x, y),
            SceneValueKind.Vec3 => new JsonArray(x, y, z),
            SceneValueKind.Vec4 => new JsonArray(x, y, z, w),
            SceneValueKind.Raw => JsonNode.Parse(v.AsRaw()),
            _ => throw new ArgumentOutOfRangeException(nameof(v)),
        };
    }

    internal static SceneValue ReadValue(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue val:
                if (val.TryGetValue(out bool b)) return SceneValue.Of(b);
                if (val.TryGetValue(out double d)) return SceneValue.Of(d);
                if (val.TryGetValue(out string? s)) return SceneValue.Of(s!);
                break;
            case JsonArray arr when arr.Count is >= 2 and <= 4 && arr.All(IsNumber):
                double x = (double)arr[0]!, y = (double)arr[1]!;
                return arr.Count switch
                {
                    2 => SceneValue.Vec(x, y),
                    3 => SceneValue.Vec(x, y, (double)arr[2]!),
                    _ => SceneValue.Vec(x, y, (double)arr[2]!, (double)arr[3]!),
                };
        }
        // どの形でもない — 原文 (コンパクト正規形) のまま保全
        return SceneValue.Raw(node?.ToJsonString() ?? "null");
    }

    private static bool IsNumber(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double _);
}
