namespace Luxel.Gallery.Presentation;

/// <summary>Canonical Japanese terminology shared by every Gallery host.</summary>
public static class GalleryLabels
{
    public const string Stories = "ストーリー";
    public const string Documentation = "ドキュメント";
    public const string Preview = "プレビュー";
    public const string Arguments = "引数";
    public const string Output = "出力";
    public const string Source = "ソース";
    public const string Actions = "操作";
    public const string Theme = "テーマ";
    public const string Settings = "設定";
    public const string Search = "検索";
    public const string SearchStories = "ストーリーを検索";
    public const string NoStories = "ストーリーが見つかりません";
    public const string Loading = "読み込み中";
    public const string Warning = "警告";
    public const string Error = "エラー";
    public const string Copy = "コピー";
    public const string Copied = "コピーしました";
    public const string OpenCanvas = "キャンバスを開く";
    public const string CloseCanvas = "キャンバスを閉じる";
    public const string SynchronizePreviewTheme = "プレビューのテーマを同期";

    private static readonly IReadOnlyDictionary<string, string> RouteGroups =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Start"] = "はじめに",
            ["Tutorials"] = "チュートリアル",
            ["Learn"] = "学ぶ",
            ["Controls"] = "コントロール",
            ["Examples"] = "例",
            ["Reference"] = "リファレンス",
            ["Internals"] = "内部",
            ["Gallery"] = "ギャラリー",
            ["Overview"] = "概要",
            ["Docs"] = Documentation,
            ["Basic"] = "基本",
            ["Playground"] = "プレイグラウンド",
            ["States"] = "状態",
            ["Accessibility"] = "アクセシビリティ",
            ["Test"] = "テスト",
            ["Layout"] = "レイアウト",
            ["Input"] = "入力",
            ["Text"] = "テキスト",
            ["Collections"] = "コレクション",
            ["Overlay"] = "オーバーレイ",
            ["Rendering"] = "レンダリング",
            ["Editor"] = "エディター",
            ["Infrastructure"] = "インフラストラクチャ",
            ["Animation"] = "アニメーション",
            ["Apps"] = "アプリ",
            ["Embeds"] = "埋め込み",
            ["RealWindow"] = "実ウィンドウ",
            ["Audio"] = "オーディオ",
            ["Typography"] = "タイポグラフィ",
            ["Resources"] = "リソース",
            ["Scripting"] = "スクリプティング",
            ["Graphics"] = "グラフィックス",
            ["Framework"] = "フレームワーク",
            ["Production"] = "プロダクション",
            ["Assets"] = "アセット",
            ["Physics"] = "物理",
            ["Internal"] = "内部",
        };

    /// <summary>Returns a Japanese structural label while leaving unknown/API identifiers unchanged.</summary>
    public static string RouteGroupLabel(string canonicalSegment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSegment);
        return RouteGroups.GetValueOrDefault(canonicalSegment, canonicalSegment);
    }

    public static string AppearanceLabel(GalleryAppearance appearance) => appearance switch
    {
        GalleryAppearance.System => "システム",
        GalleryAppearance.Light => "ライト",
        GalleryAppearance.Dark => "ダーク",
        _ => throw new ArgumentOutOfRangeException(nameof(appearance), appearance, null),
    };

    public static string StoryKindLabel(StoryKind kind) => kind switch
    {
        StoryKind.Docs => Documentation,
        StoryKind.Basic => "基本",
        StoryKind.Playground => "プレイグラウンド",
        StoryKind.Example => "例",
        StoryKind.State => "状態",
        StoryKind.AccessibilityFixture => "アクセシビリティ",
        StoryKind.TestFixture => "テスト",
        _ => Stories,
    };

    public static string CompatibilityLabel(GalleryCompatibility compatibility) => compatibility switch
    {
        GalleryCompatibility.BrowserSafe => "ブラウザー / Native",
        GalleryCompatibility.NativeOnly => "Native のみ",
        _ => throw new ArgumentOutOfRangeException(nameof(compatibility), compatibility, null),
    };
}
