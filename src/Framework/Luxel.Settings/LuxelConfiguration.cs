using System.Text;
using Microsoft.Extensions.Configuration;

namespace Luxel.Settings;

/// <summary>
/// 設定読み込み用の **.NET 標準 <see cref="IConfiguration"/>** を組み立てるヘルパ。
/// 優先順 (後のプロバイダが勝つ): <b>設定ファイル (JSON) &lt; 環境変数 &lt; コマンドライン</b>。
/// これで環境変数 (<c>Section__Key=値</c>) やコマンドライン (<c>--Section:Key=値</c>) が
/// 保存済みファイルを上書きできる (ops による上書きの定石)。
/// <para>ホスト (<c>LuxelHostBuilder</c>) は <c>Host.CreateApplicationBuilder</c> の
/// <see cref="IConfiguration"/> (appsettings + env + cmdline) をそのまま読み口にもできる。</para>
/// </summary>
public static class LuxelConfiguration
{
    /// <summary>設定ファイル + 環境変数 (+ 任意でコマンドライン) をレイヤした <see cref="IConfiguration"/> を作る。</summary>
    /// <param name="files">設定ファイルの読み元 (物理/インメモリ)。</param>
    /// <param name="fileName">設定ファイル名。</param>
    /// <param name="envPrefix">環境変数のプレフィックス (例 <c>"MYGAME_"</c>)。指定するとそのプレフィックス付きだけ読み、
    /// 名前からプレフィックスを剥がす。null/空なら全環境変数を読む。</param>
    /// <param name="commandLineArgs">コマンドライン引数 (<c>--Section:Key=値</c>)。null なら追加しない。</param>
    public static IConfiguration Build(
        IFileStore files, string fileName = "settings.json", string? envPrefix = null, string[]? commandLineArgs = null)
    {
        var builder = new ConfigurationBuilder();
        string? json = files.Exists(fileName) ? files.Read(fileName) : null;
        if (!string.IsNullOrWhiteSpace(json))
            builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));   // 最下層 (保存済みファイル)
        builder.AddEnvironmentVariables(envPrefix ?? string.Empty);                  // 環境変数で上書き
        if (commandLineArgs is { Length: > 0 })
            builder.AddCommandLine(commandLineArgs);                                 // cmdline が最優先
        return builder.Build();
    }
}
