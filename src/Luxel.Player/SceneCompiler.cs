using System.Text;
using Luxel.Resources;
using Luxel.SceneEdit;

namespace Luxel.Player;

/// <summary>
/// エディタ形式 (SceneDoc) → ランタイム world への**一方向**構築 (ADR-0015)。
/// コアは空間非依存 (space で分岐するだけ)、実体の構築は space 別バックエンド —
/// M11 は 2D のみ、3D バックエンドは M12 (ToDo/27 GE-9) で足す (原則 5)。
/// </summary>
public static class SceneCompiler
{
    public static Player2DWorld Compile(SceneDoc doc) => doc.Space switch
    {
        SceneSpace.TwoD => Compile2D(doc),
        _ => throw new NotSupportedException("3D バックエンドは M12 (ToDo/27 GE-9) で追加予定"),
    };

    // 2D バックエンド: transform2d を第一級に展開、タイルレイヤは素通し
    private static Player2DWorld Compile2D(SceneDoc doc)
        => new(doc.Entities.Select(e => new PlayerEntity(e)), doc.TileLayers);
}

/// <summary>読み込んだゲーム一式 (プロジェクト宣言 + 開始シーンの world)。</summary>
public sealed record PlayerGame(GameProject Project, Player2DWorld World);

/// <summary>
/// プロジェクトフォルダ (<see cref="IVirtualFileSystem"/>) からの読み込み。ランタイムは
/// 読み取り専用なので VFS を使う (エディタ側の IFileStorage とは別 — 書くのはエディタだけ)。
/// </summary>
public static class PlayerLoader
{
    /// <summary>project.luxel を読む。</summary>
    public static GameProject LoadProject(IVirtualFileSystem fs)
        => GameProjectJson.Deserialize(Text(fs, GameProject.FileName));

    /// <summary>res:// のシーンを読む。</summary>
    public static SceneDoc LoadScene(IVirtualFileSystem fs, string resPath)
        => SceneJson.Deserialize(Text(fs, ResPath.Resolve(resPath)));

    /// <summary>プロジェクトを読み、開始シーンをコンパイルし、csx ビヘイビアを配線して返す。</summary>
    public static PlayerGame LoadStart(IVirtualFileSystem fs)
    {
        GameProject project = LoadProject(fs);
        SceneDoc scene = LoadScene(fs, project.StartScene);
        Player2DWorld world = SceneCompiler.Compile(scene);
        var behaviours = new PlayerBehaviours(fs);
        behaviours.LoadAll(world);
        world.Behaviours = behaviours;
        return new PlayerGame(project, world);
    }

    private static string Text(IVirtualFileSystem fs, string path)
    {
        if (!fs.Exists(path)) throw new FileNotFoundException($"プロジェクトにファイルが無い: {path}", path);
        return Encoding.UTF8.GetString(fs.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult());
    }
}
