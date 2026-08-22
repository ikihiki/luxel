using Luxel.Gallery.Presentation;

namespace Luxel.Gallery;

/// <summary>Native-only phrases; shared Gallery terms live in <see cref="GalleryLabels"/>.</summary>
internal static class NativeGalleryLabels
{
    public const string WindowTitle = "Luxel ギャラリー";
    public const string BrandSubtitle = "コントロール ギャラリー";
    public const string LoadingSummary = "ストーリーを読み込んでいます…";
    public const string EmptyPreviewSummary = "一覧からストーリーを選択してください。";
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

    public static string ThemeName(GalleryAppearance appearance)
        => GalleryLabels.AppearanceLabel(appearance);

    public static string ShellThemeButton(GalleryAppearance appearance)
        => $"画面: {ThemeName(appearance)}";

    public static string PreviewThemeButton(GalleryAppearance appearance)
        => $"プレビュー: {ThemeName(appearance)}";

    public static string ThemeSynchronizationButton(bool synchronized)
        => synchronized ? "テーマ同期: オン" : "テーマ同期: オフ";

    public static string ShellThemeTooltip(GalleryAppearance appearance)
        => $"ギャラリー全体を{ThemeName(Opposite(appearance))}テーマに切り替えます。";

    public static string PreviewThemeTooltip(GalleryAppearance appearance, bool synchronized)
        => synchronized
            ? "プレビューは画面テーマと同期しています。押すと同期を解除してテーマを切り替えます。"
            : $"ストーリープレビューを{ThemeName(Opposite(appearance))}テーマに切り替えます。";

    public static string ThemeSynchronizationTooltip(bool synchronized)
        => synchronized
            ? "画面とプレビューのテーマ同期を解除します。"
            : "プレビューを画面テーマに合わせ、以後の切り替えを同期します。";

    private static GalleryAppearance Opposite(GalleryAppearance appearance)
        => appearance == GalleryAppearance.Dark ? GalleryAppearance.Light : GalleryAppearance.Dark;
}
