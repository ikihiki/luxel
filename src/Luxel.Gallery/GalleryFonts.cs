using Luxel.Typography;

namespace Luxel.Gallery;

/// <summary>
/// リポジトリ共有フォント (<c>assets/fonts/</c>) のローダ。
/// Gallery / e2e はシステムフォントに依存せず、clone すればどのマシン・CI でも同じ字形が出る
/// (golden の再現性)。フォントはいずれも glyf アウトライン (VectorFont は CFF/OTF 非対応) の
/// SIL OFL フォント: BIZ UDGothic (本文, 日本語+ラテン) と UDEV Gothic (等幅, コードエディタ)。
/// 解決順: cwd 相対 → cwd から Luxel.slnx を上方探索したルート相対 → exe 隣 (publish 用)。
/// </summary>
public static class GalleryFonts
{
    /// <summary>ホスト/本文フォント (BIZ UDGothic Regular — 日本語かな漢字 + ラテンをカバー)。</summary>
    public const string Regular = "BIZUDGothic-Regular.ttf";

    /// <summary>太字 (BIZ UDGothic Bold)。</summary>
    public const string Bold = "BIZUDGothic-Bold.ttf";

    /// <summary>等幅 (UDEV Gothic — JetBrains Mono 由来のラテン等幅 + BIZ UDGothic 由来の日本語)。
    /// コードエディタ (TextEditorView の Mono 変種) は単一フォントで描く (フォールバック無し) ため、日本語コメントもこれで等幅表示する。</summary>
    public const string Mono = "UDEVGothic-Regular.ttf";

    private const string RelDir = "assets/fonts";

    /// <summary>同梱フォントを VectorFont として読む。</summary>
    public static VectorFont Load(string fileName) => VectorFont.Load(Resolve(fileName));

    /// <summary>同梱フォントの実パスを解決する。見つからなければ FileNotFoundException。</summary>
    public static string Resolve(string fileName)
    {
        // 1) cwd 相対 (リポジトリルートで dotnet run した e2e ランナー)
        string cwdPath = Path.Combine(Environment.CurrentDirectory, RelDir, fileName);
        if (File.Exists(cwdPath)) return cwdPath;

        // 2) cwd から上方に Luxel.slnx を探し、そのルート相対 (dotnet test の bin 配下 cwd 等)
        for (string? d = Environment.CurrentDirectory; d is not null; d = Path.GetDirectoryName(d))
            if (File.Exists(Path.Combine(d, "Luxel.slnx")))
            {
                string rootPath = Path.Combine(d, RelDir, fileName);
                if (File.Exists(rootPath)) return rootPath;
                break;
            }

        // 3) exe 隣の fonts (スタンドアロン publish 用 — Luxel.FontAssets.targets が Content コピー)
        string appPath = Path.Combine(AppContext.BaseDirectory, "fonts", fileName);
        if (File.Exists(appPath)) return appPath;

        throw new FileNotFoundException(
            $"同梱フォントが見つかりません: {fileName} " +
            $"(探索: cwd/{RelDir}, Luxel.slnx ルート/{RelDir}, {appPath})");
    }
}
