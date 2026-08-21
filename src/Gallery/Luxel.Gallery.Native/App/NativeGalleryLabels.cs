namespace Luxel.Gallery;

/// <summary>Native Gallery の利用者向け日本語。API 識別子と canonical path は変換しない。</summary>
internal static class NativeGalleryLabels
{
    public const string WindowTitle = "Luxel ギャラリー";
    public const string BrandSubtitle = "コントロール ギャラリー";
    public const string Stories = "ストーリー";
    public const string Preview = "プレビュー";
    public const string Arguments = "引数";
    public const string Output = "出力";
    public const string Source = "ソース";
    public const string Tools = "ツール";
    public const string Interactions = "操作";
    public const string Console = "コンソール";
    public const string SearchPlaceholder = "ストーリーを検索";
    public const string SelectStory = "ストーリーを選択";
    public const string LoadingSummary = "ストーリーを読み込んでいます…";
    public const string EmptyPreviewSummary = "一覧からストーリーを選択してください。";
    public const string WarningSummary = "このストーリーには実行環境に関する注意があります。";
    public const string ErrorTitle = "ストーリーを表示できません";
    public const string ErrorSummary = "プレビューの構築中にエラーが発生しました。詳細を確認してストーリーを修正してください。";
    public const string NoPlaySummary = "このストーリーに play はありません。ctx.Play(d => d.Snap()) で登録できます。";
    public const string OutputReady = "準備完了";
    public const string OutputReadySummary = "ストーリーランタイムを利用できます。";
    public const string OutputEmptySummary = "ランタイムのイベントとエラーがここに表示されます。";
    public const string NoStorySelected = "ストーリーが選択されていません。";
    public const string SourceUnavailable = "ソースを表示できません。";
    public const string OpenCanvas = "集中表示にする";
    public const string CloseCanvas = "集中表示を終了";

    public static string ThemeName(GalleryThemeMode mode)
        => mode == GalleryThemeMode.Dark ? "ダーク" : "ライト";

    public static string ShellThemeButton(GalleryThemeMode mode)
        => $"画面: {ThemeName(mode)}";

    public static string PreviewThemeButton(GalleryThemeMode mode)
        => $"プレビュー: {ThemeName(mode)}";

    public static string ThemeSynchronizationButton(bool synchronized)
        => synchronized ? "テーマ同期: オン" : "テーマ同期: オフ";

    public static string ShellThemeTooltip(GalleryThemeMode mode)
        => $"ギャラリー全体を{ThemeName(Opposite(mode))}テーマに切り替えます。";

    public static string PreviewThemeTooltip(GalleryThemeMode mode, bool synchronized)
        => synchronized
            ? "プレビューは画面テーマと同期しています。押すと同期を解除してテーマを切り替えます。"
            : $"ストーリープレビューを{ThemeName(Opposite(mode))}テーマに切り替えます。";

    public static string ThemeSynchronizationTooltip(bool synchronized)
        => synchronized
            ? "画面とプレビューのテーマ同期を解除します。"
            : "プレビューを画面テーマに合わせ、以後の切り替えを同期します。";

    public static string StoryCount(int count) => $"{count} 件のストーリー";

    public static string NavigationSegment(string value) => value switch
    {
        "Start" => "はじめに",
        "Welcome" => "ようこそ",
        "Learn" => "学ぶ",
        "Controls" => "コントロール",
        "Docs" => "ドキュメント",
        "Basic" => "基本",
        "Playground" => "プレイグラウンド",
        "Examples" => "例",
        "States" => "状態",
        "Accessibility" => "アクセシビリティ",
        "Test" => "テスト",
        "Overview" => "概要",
        "Rendering" => "描画",
        "Graphics" => "グラフィックス",
        "Editor" => "エディター",
        "Framework" => "フレームワーク",
        "Platform" => "プラットフォーム",
        "Resources" => "リソース",
        "Scripting" => "スクリプト",
        "Animation" => "アニメーション",
        "Audio" => "オーディオ",
        "Input" => "入力",
        "Particles" => "パーティクル",
        _ => value,
    };

    private static GalleryThemeMode Opposite(GalleryThemeMode mode)
        => mode == GalleryThemeMode.Dark ? GalleryThemeMode.Light : GalleryThemeMode.Dark;
}
