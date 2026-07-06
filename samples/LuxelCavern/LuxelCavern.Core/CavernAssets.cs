using Luxel.Typography;

namespace LuxelCavern.Core;

/// <summary>
/// ゲーム同梱アセットの解決口。<b>cwd 非依存</b> — 常に <see cref="AppContext.BaseDirectory"/>
/// (実行ファイルの隣) を基準にするので、publish フォルダをリポジトリ外へコピーしても解決できる。
/// フォントは <c>assets/fonts/</c> に同梱 (csproj の Content コピーで exe 隣へ配置)。
/// </summary>
public static class CavernAssets
{
    /// <summary>本文/UI フォント (BIZ UDGothic — 日本語かな漢字 + ラテン)。システムフォント非依存。</summary>
    public const string BodyFont = "BIZUDGothic-Regular.ttf";

    /// <summary>exe 隣の <c>assets/</c> 基準にサブパスを解決する。</summary>
    public static string Path(string relative) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "assets", relative);

    /// <summary>同梱フォントの実パス。</summary>
    public static string FontPath(string fileName) => Path(System.IO.Path.Combine("fonts", fileName));

    /// <summary>同梱の本文フォントを <see cref="VectorFont"/> として読む。見つからなければ
    /// <see cref="FileNotFoundException"/> — publish でフォントがコピーされていない穴を早期に検出する。</summary>
    public static VectorFont LoadBodyFont()
    {
        string path = FontPath(BodyFont);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"同梱フォントが見つかりません: {path} " +
                "(csproj の Content コピー、または publish のアセット同梱を確認してください)", path);
        return VectorFont.Load(path);
    }
}
