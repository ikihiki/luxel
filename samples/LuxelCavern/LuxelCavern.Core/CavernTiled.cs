using System.Numerics;
using System.Text.Json;
using Luxel.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// Tiled (.tmj) JSON のパース (純ロジック)。タイル層は <see cref="TileMap.FromTiledJson"/> に委譲 (Q07 のドッグフード)、
/// オブジェクト層 (<c>objectgroup</c>) は本ゲーム固有のエンティティ (coin/key/door/walker/flyer/checkpoint/torch)
/// として <see cref="CavernSim"/> に流し込む。**JSON の取得は <see cref="CavernLevelLoader"/> が
/// <see cref="Luxel.Resources.ResourceSystem"/> 経由で行う** — ここは受け取った文字列を解釈するだけ。
/// gid = <see cref="TileSet"/> の id に一致させてある。
/// </summary>
public static class CavernTiled
{
    /// <summary>.tmj のタイル層だけからマップを組む (エンティティ抜き。物理テスト等が使う)。</summary>
    public static TileMap BuildMap(TileSet ts, string json) => TileMap.FromTiledJson(ts, json);

    /// <summary>
    /// .tmj からタイル + オブジェクト層のエンティティを読み <see cref="CavernSim"/> を組む。
    /// スポーン/プレイヤーサイズはコード側から渡す (Tiled 非対象)。松明位置は <paramref name="torches"/> へ。
    /// </summary>
    public static CavernSim BuildSim(string json, TileSet ts, Vector2 spawn, Vector2 playerSize, out Vector2[] torches)
    {
        TileMap map = TileMap.FromTiledJson(ts, json);
        var sim = new CavernSim(map, spawn, playerSize);
        var torchList = new List<Vector2>();

        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement layer in doc.RootElement.GetProperty("layers").EnumerateArray())
        {
            if (!layer.TryGetProperty("type", out JsonElement lt) || lt.GetString() != "objectgroup") continue;
            foreach (JsonElement o in layer.GetProperty("objects").EnumerateArray())
            {
                string type = o.TryGetProperty("type", out JsonElement te) ? te.GetString() ?? "" : "";
                float x = F(o, "x"), y = F(o, "y"), w = F(o, "width"), h = F(o, "height");
                var pos = new Vector2(x, y);
                switch (type)
                {
                    case "coin":
                        sim.Pickups.Add(new Pickup { Pos = pos, Size = w > 0 ? w : 10, IsKey = false });
                        break;
                    case "key":
                        sim.Pickups.Add(new Pickup { Pos = pos, Size = w > 0 ? w : 12, IsKey = true });
                        break;
                    case "door":
                        sim.DoorPos = pos;
                        if (w > 0 && h > 0) sim.DoorSize = new Vector2(w, h);
                        break;
                    case "walker":
                        sim.Enemies.Add(new Walker { Pos = pos, VelX = P(o, "velX", -40f), MinX = P(o, "minX", x - 40f), MaxX = P(o, "maxX", x + 40f) });
                        break;
                    case "flyer":
                        sim.Flyers.Add(new Flyer { Home = pos, AmpX = P(o, "ampX", 40f), AmpY = P(o, "ampY", 22f), Freq = P(o, "freq", 1.2f) });
                        break;
                    case "checkpoint":
                        var cp = new Checkpoint { Pos = pos };
                        if (w > 0 && h > 0) cp.Size = new Vector2(w, h);
                        sim.Checkpoints.Add(cp);
                        break;
                    case "torch":
                        torchList.Add(pos);
                        break;
                }
            }
        }
        torches = torchList.ToArray();
        return sim;
    }

    /// <summary>松明位置だけを拾う (演出レイヤ用の軽量パス)。</summary>
    public static Vector2[] ParseTorches(string json)
    {
        var list = new List<Vector2>();
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement layer in doc.RootElement.GetProperty("layers").EnumerateArray())
        {
            if (!layer.TryGetProperty("type", out JsonElement lt) || lt.GetString() != "objectgroup") continue;
            foreach (JsonElement o in layer.GetProperty("objects").EnumerateArray())
                if (o.TryGetProperty("type", out JsonElement te) && te.GetString() == "torch")
                    list.Add(new Vector2(F(o, "x"), F(o, "y")));
        }
        return list.ToArray();
    }

    private static float F(JsonElement o, string name)
        => o.TryGetProperty(name, out JsonElement v) ? (float)v.GetDouble() : 0f;

    /// <summary>オブジェクトのカスタムプロパティ (Tiled の properties 配列) を float で読む。</summary>
    private static float P(JsonElement o, string name, float fallback)
    {
        if (o.TryGetProperty("properties", out JsonElement props))
            foreach (JsonElement p in props.EnumerateArray())
                if (p.TryGetProperty("name", out JsonElement n) && n.GetString() == name
                    && p.TryGetProperty("value", out JsonElement val))
                    return (float)val.GetDouble();
        return fallback;
    }
}
