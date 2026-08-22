using Luxel.Gallery.Presentation;

namespace Luxel.Gallery;

/// <summary>Native renderer 内の利用者向け日本語。コード、path、ログ本文は呼び出し元の原文を保つ。</summary>
internal static class NativeRenderingLabels
{
    public static string Arguments => GalleryLabels.Arguments;
    public static string Output => GalleryLabels.Output;
    public static string Source => GalleryLabels.Source;
    public const string RuntimeEventsEmpty = "ランタイムのイベントとエラーがここに表示されます。";
    public const string NoStorySelected = "ストーリーが選択されていません。";
    public const string SourceUnavailable = "ソースを表示できません。";

    public static string StoryNotFound(string path) => $"ストーリーが見つかりません: {path}";
}
