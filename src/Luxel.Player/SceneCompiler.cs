using System.Text;
using System.Numerics;
using Luxel;
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
    public static IPlayerWorld Compile(SceneDoc doc) => doc.Space switch
    {
        SceneSpace.TwoD => Compile2D(doc),
        SceneSpace.ThreeD => Compile3D(doc),
        _ => throw new NotSupportedException($"未対応のシーン空間: {doc.Space}"),
    };

    // 2D バックエンド: transform2d を第一級に展開、タイルレイヤは素通し
    public static Player2DWorld Compile2D(SceneDoc doc)
        => new(doc.Entities.Select(e => new PlayerEntity(e)), doc.TileLayers);

    // 3D バックエンド: transform3d / mesh3d / camera3d を Player3DWorld へ展開
    public static Player3DWorld Compile3D(SceneDoc doc)
    {
        OrbitCamera camera = new(new Vector3(0, 0.4f, 0), yaw: 0.72f, pitch: 0.42f, distance: 8f,
            fovYRadians: 1.05f, aspect: 16f / 9f, near: 0.05f, far: 100f);
        foreach (SceneEntity e in doc.Entities)
        {
            SceneComponent? c = e.Component("camera3d");
            if (c is null) continue;
            camera.Target = c.Get("target")?.AsVec3() ?? camera.Target;
            camera.Distance = MathF.Max(0.1f, c.Get("distance")?.AsFloat() ?? camera.Distance);
            camera.Yaw = c.Get("yaw")?.AsFloat() ?? camera.Yaw;
            camera.Pitch = c.Get("pitch")?.AsFloat() ?? camera.Pitch;
            break;
        }
        return new Player3DWorld(doc.Entities.Select(e => new PlayerEntity(e)), camera);
    }
}

/// <summary>読み込んだゲーム一式 (プロジェクト宣言 + 開始シーンの world)。</summary>
public sealed record PlayerGame(GameProject Project, IPlayerWorld World)
{
    public Player2DWorld World2D => World as Player2DWorld ?? throw new InvalidOperationException("開始シーンは 2D ではありません");
    public Player3DWorld World3D => World as Player3DWorld ?? throw new InvalidOperationException("開始シーンは 3D ではありません");
}

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
        IPlayerWorld world = SceneCompiler.Compile(scene);
        ValidateAssets(fs, world);
        var behaviours = new PlayerBehaviours(fs);
        behaviours.LoadAll(world);
        world.Behaviours = behaviours;
        return new PlayerGame(project, world);
    }

    private static void ValidateAssets(IVirtualFileSystem fs, IPlayerWorld world)
    {
        if (world is not Player3DWorld w3) return;
        foreach (string asset in w3.MeshAssets)
        {
            if (!asset.StartsWith("res://", StringComparison.Ordinal)) continue;
            string path = ResPath.Resolve(asset);
            if (!fs.Exists(path)) w3.MarkMissingAsset(asset);
        }
    }

    private static string Text(IVirtualFileSystem fs, string path)
    {
        if (!fs.Exists(path)) throw new FileNotFoundException($"プロジェクトにファイルが無い: {path}", path);
        return Encoding.UTF8.GetString(fs.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult());
    }
}
