using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Settings;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **永続化デモ** — ゲーム状態のセーブ/ロード (<see cref="WorldSave"/>) と設定ストア (<see cref="SettingsStore"/>)。
/// どちらも実物の API を決定的に (wall-clock 非依存) 動かして snap する。
/// </summary>
public static class PersistenceStories
{
    private const float CW = 460, RowH = 56, Box = 22;

    /// <summary>セーブ → 箱を動かす → ロードで元に戻る、を 3 行で見せる (① と ③ が一致)。</summary>
    [Story("Demos/Framework/SaveLoad", Height = 300, Order = 151)]
    public static Widget SaveLoad(StoryContext ctx)
    {
        float[] start = { 40, 140, 240, 340 };
        using var world = new World();
        var boxes = new List<Entity>();
        foreach (float x in start)
            boxes.Add(world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(x, 0, 0))));

        float[] saved = ReadX(world);
        string json = WorldSave.Serialize(world);              // セーブ (JSON 文字列)

        for (int i = 0; i < boxes.Count; i++)                  // 箱を右へ動かす
            world.Set(boxes[i], new LocalTransform(Matrix4x4.CreateTranslation(start[i] + 60, 0, 0)));
        float[] moved = ReadX(world);

        WorldSave.Deserialize(world, json);                    // ロード → 元の位置へ
        float[] loaded = ReadX(world);

        return ctx.Snap(VStack(6)[
            Muted("① セーブ時の位置"),
            Frame(Canvas2D(CW, RowH, draw: s => DrawBoxes(s, saved, Color2D.Rgba(80, 220, 120)))),
            Muted("② 箱を動かした後"),
            Frame(Canvas2D(CW, RowH, draw: s => DrawBoxes(s, moved, Color2D.Rgba(240, 190, 90)))),
            Muted("③ ロード後 — ① に戻る"),
            Frame(Canvas2D(CW, RowH, draw: s => DrawBoxes(s, loaded, Color2D.Rgba(90, 200, 240))))
        ]);
    }

    /// <summary>設定は SettingsStore の Signal に直結 — 保存済みの値 (0.65 / on) が Slider/Switch に反映される。</summary>
    [Story("Demos/Framework/Settings", Height = 220, Order = 152)]
    public static Widget Settings(StoryContext ctx)
    {
        var files = new InMemoryFileStore();
        files.Write("settings.json", "{ \"volume\": 0.65, \"fullscreen\": true }");   // 保存済み設定
        var store = SettingsStore.LoadFrom(files, "settings.json");

        return ctx.Snap(Frame(VStack(12)[
            Muted("設定は SettingsStore の Signal に直結。Slider/Switch がそのまま双方向バインドし、保存値が反映される。"),
            HStack(12)[Label("音量"), Slider(store.Get("volume", 0.5f))],
            HStack(12)[Label("フルスクリーン"), Switch(store.Get("fullscreen", false))]
        ]));
    }

    private static float[] ReadX(World w)
    {
        var xs = new List<float>();
        foreach (var e in w.Store.Entities) xs.Add(e.GetComponent<LocalTransform>().Matrix.Translation.X);
        xs.Sort();
        return xs.ToArray();
    }

    private static void DrawBoxes(Scene2D s, float[] xs, uint color)
    {
        foreach (float x in xs) s.FillRoundedRect(color, x, (RowH - Box) / 2, Box, Box, 4);
    }
}
