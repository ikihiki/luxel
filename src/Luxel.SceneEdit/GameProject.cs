using System.Text.Json.Nodes;

namespace Luxel.SceneEdit;

/// <summary>
/// ゲームプロジェクトの宣言 (`project.luxel` の内容、ADR-0015)。プロジェクト = フォルダで、
/// 中身は規約配置 (scenes/ + assets/ + scripts/ 等) — このレコードはフォルダに 1 個の
/// メタデータだけを持つ。入力バインド宣言等は GE-3 (Player) で足す。
/// </summary>
public sealed record GameProject(
    string Name,
    string StartScene,
    int WindowWidth = 1280,
    int WindowHeight = 720)
{
    /// <summary>プロジェクトメタファイルの規約名。</summary>
    public const string FileName = "project.luxel";
}

/// <summary>
/// `res://` 参照 (プロジェクトフォルダ相対) の解決。Luxel.Resources の EmbeddedResourceSource と
/// 同じスキームをプロジェクトフォルダに対して使う — シーン/スキーマ/プロジェクトの全アセット
/// 参照はこの形式に統一する (png/wav/tmj/glb を同列に)。
/// </summary>
public static class ResPath
{
    public const string Scheme = "res://";

    public static bool Is(string path) => path.StartsWith(Scheme, StringComparison.Ordinal);

    /// <summary>res:// をプロジェクトフォルダ相対パス ('/' 区切り) に解決する。
    /// フォルダ外への脱出 (..) や絶対パス化は拒否。</summary>
    public static string Resolve(string resPath)
    {
        if (!Is(resPath)) throw new ArgumentException($"res:// 参照でない: {resPath}");
        string rel = resPath[Scheme.Length..];
        if (rel.Length == 0) throw new ArgumentException("res:// の後が空");
        if (rel.Contains('\\')) throw new ArgumentException($"区切りは '/' のみ: {resPath}");
        if (rel.StartsWith('/')) throw new ArgumentException($"先頭 '/' は不可: {resPath}");
        foreach (string seg in rel.Split('/'))
            if (seg is "" or "." or "..")
                throw new ArgumentException($"不正なパスセグメント: {resPath}");
        return rel;
    }
}

/// <summary><see cref="GameProject"/> ⇄ JSON の決定的往復 (整形規則は <see cref="SceneJson"/> と同じ)。</summary>
public static class GameProjectJson
{
    public static string Serialize(GameProject p)
    {
        var root = new JsonObject
        {
            ["name"] = p.Name,
            ["startScene"] = p.StartScene,
            ["window"] = new JsonObject
            {
                ["width"] = p.WindowWidth,
                ["height"] = p.WindowHeight,
            },
        };
        return root.ToJsonString(SceneJson.Options) + "\n";
    }

    public static GameProject Deserialize(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("project.luxel のルートがオブジェクトでない");
        string start = (string?)root["startScene"] ?? throw new FormatException("startScene が無い");
        _ = ResPath.Resolve(start);   // res:// 形式をここで検証
        var window = root["window"] as JsonObject;
        return new GameProject(
            (string?)root["name"] ?? throw new FormatException("name が無い"),
            start,
            (int?)window?["width"] ?? 1280,
            (int?)window?["height"] ?? 720);
    }
}
