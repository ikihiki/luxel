using System.Text;
using System.Text.Json;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Serialize;

namespace Luxel.Ecs;

/// <summary>
/// ゲーム状態 (ECS <see cref="World"/>) のセーブ/ロード。Friflo 組み込みの <see cref="EntitySerializer"/>
/// (component 名 → 値スキーマ、アーキタイプ非依存 = エンジン更新をまたいで安定) を土台に、
/// バージョン付きラッパを被せる。**文字列 in/out** なのでファイル IO 非依存でテストできる
/// (ファイル層は <c>Luxel.Resources</c> の <c>IVirtualFileSystem</c> 側で薄く繋ぐ)。
///
/// <para><b>保存対象</b>: 純データ component のみ。GPU ハンドル/観測専用の component は
/// Friflo の <c>[ComponentKey(null)]</c> で除外する (例: <see cref="DebugName"/>)。復元後に
/// GPU 資源が要る component は「シーン最初のフレームで遅延生成」の既存規約で再構築する。</para>
/// </summary>
public static class WorldSave
{
    /// <summary>セーブフォーマットの現在バージョン。ロード時の migration 判定に使う。</summary>
    public const int CurrentVersion = 1;

    /// <summary>World を JSON 文字列にする。<c>{ "version":1, "entities":[ ... ] }</c>。</summary>
    public static string Serialize(World world)
    {
        var ser = new EntitySerializer();
        using var ms = new MemoryStream();
        ser.WriteStore(world.Store, ms);
        string entities = Encoding.UTF8.GetString(ms.ToArray());
        return $"{{\"version\":{CurrentVersion},\"entities\":{entities}}}";
    }

    /// <summary>JSON 文字列を World へ復元する。<paramref name="clear"/>=true (既定) は既存 entity を
    /// 全消去してから復元。false は Friflo の <b>pid キー upsert</b> (同 pid は上書き、新 pid は追加) で
    /// マージする。version 不一致でも throw せず読み進める (migration チェーンは v1 は枠のみ —
    /// 将来ここに変換を挟む)。</summary>
    public static void Deserialize(World world, string json, bool clear = true)
    {
        string entities = ExtractEntities(json, out int version);
        _ = version;   // v1: migration 不要。将来 version に応じて entities を変換する。
        if (clear) ClearEntities(world);
        var ser = new EntitySerializer();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(entities));
        ser.ReadIntoStore(world.Store, ms);
    }

    /// <summary>World の全 entity を削除する (復元前のクリア用)。</summary>
    public static void ClearEntities(World world)
    {
        var store = world.Store;
        var all = new List<Entity>(store.Count);
        foreach (var e in store.Entities) all.Add(e);   // 列挙中削除を避けてスナップショット
        foreach (var e in all) e.DeleteEntity();
    }

    /// <summary><c>{version, entities}</c> ラッパから entities 配列 JSON を取り出す。
    /// 生配列 <c>[ ... ]</c> (Friflo 直の旧形式) もそのまま受ける。</summary>
    private static string ExtractEntities(string json, out int version)
    {
        version = CurrentVersion;
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array) return json;   // 生配列 (version 情報なし)
        if (root.TryGetProperty("version", out var v) && v.TryGetInt32(out int ver)) version = ver;
        if (root.TryGetProperty("entities", out var ents)) return ents.GetRawText();
        return "[]";
    }
}
