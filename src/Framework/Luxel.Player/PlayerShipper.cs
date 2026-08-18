using System.Diagnostics;

namespace Luxel.Player;

/// <summary>
/// 出荷 (ToDo 27 GE-6): プロジェクトフォルダを配布可能な自己完結フォルダにする —
/// ① `dotnet publish Luxel.Player.App` (self-contained、shaders は targets が同梱)
/// ② プロジェクト内容を出力の <c>project/</c> へコピー (exe は引数省略時にここを読む規約)。
/// **v1 は dotnet SDK 前提** (ADR-0015 の選択肢 (a) — 開発機/エディタから呼ぶ想定。
/// 事前ビルド済み player の同梱コピー方式は Studio の配布形態が決まったら)。
/// Studio シェル (GE-7) の「出荷」メニューはこれを呼ぶ。
/// </summary>
public static class PlayerShipper
{
    /// <summary>出荷フォルダ内でプロジェクト内容を置く規約サブフォルダ。</summary>
    public const string ProjectSubdir = "project";

    /// <summary>publish + コンテンツコピー。成功で出力フォルダを返す (失敗は例外、publish ログ込み)。</summary>
    /// <param name="playerAppProject">Luxel.Player.App の csproj パス (リポジトリ内)。</param>
    /// <param name="projectFolder">出荷するゲームプロジェクトフォルダ。</param>
    /// <param name="outDir">出力フォルダ (無ければ作成、既存 project/ は入れ替え)。</param>
    /// <param name="selfContained">自己完結 publish を行う場合は <see langword="true"/>。</param>
    public static string Ship(string playerAppProject, string projectFolder, string outDir, bool selfContained = true)
    {
        if (!File.Exists(playerAppProject)) throw new FileNotFoundException(playerAppProject);
        if (!Directory.Exists(projectFolder)) throw new DirectoryNotFoundException(projectFolder);
        Directory.CreateDirectory(outDir);

        var psi = new ProcessStartInfo("dotnet",
            $"publish \"{playerAppProject}\" -c Release -r win-x64 {(selfContained ? "--self-contained" : "--no-self-contained")} -o \"{outDir}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("dotnet publish を起動できない");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"dotnet publish 失敗 (exit {proc.ExitCode}):\n{stdout}\n{stderr}");

        CopyProject(projectFolder, Path.Combine(outDir, ProjectSubdir));
        return outDir;
    }

    /// <summary>プロジェクト内容を出荷フォルダの project/ へコピーする (既存は入れ替え)。
    /// publish を伴わない純粋部 — 単体テスト対象。</summary>
    public static void CopyProject(string projectFolder, string destProjectDir)
    {
        if (Directory.Exists(destProjectDir)) Directory.Delete(destProjectDir, recursive: true);
        foreach (string file in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(projectFolder, file);
            string dest = Path.Combine(destProjectDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
        if (!File.Exists(Path.Combine(destProjectDir, SceneEdit.GameProject.FileName)))
            throw new InvalidOperationException($"プロジェクトに {SceneEdit.GameProject.FileName} が無い: {projectFolder}");
    }
}
