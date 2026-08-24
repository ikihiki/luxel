using Luxel.Gallery.UI;

namespace Luxel.UI.Gallery;

/// <summary>利用者向け UI コンポーネントの構造化された日本語 Docs 登録。</summary>
internal static partial class UiControlDocs
{
    private const string AuthoredSource = "日本語で執筆した構造化コンポーネント Docs。";

    internal static void Register(StoryCatalogBuilder builder, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors)
        => ControlDocsRenderer.Register(builder, descriptors, Pages, AuthoredSource);

    private static IEnumerable<ControlDocsPage> Pages
    {
        get
        {
            foreach (ControlDocsPage page in CollectionsPages) yield return page;
            foreach (ControlDocsPage page in InputPages) yield return page;
            foreach (ControlDocsPage page in LayoutPages) yield return page;
            foreach (ControlDocsPage page in OverlayPages) yield return page;
            foreach (ControlDocsPage page in RenderingPages) yield return page;
            foreach (ControlDocsPage page in TextPages) yield return page;
            foreach (ControlDocsPage page in EditorPages) yield return page;
        }
    }

    private static ControlDocsPage Page(
        string type,
        string category,
        string title,
        string summary,
        IReadOnlyList<string> useWhen,
        IReadOnlyList<string> avoidWhen,
        string usage,
        string state,
        string mainApi,
        string operations,
        string theme,
        string limits,
        string api,
        IReadOnlyList<ControlDocsAlternative>? alternatives = null,
        IReadOnlyList<ControlDocsKeyboardBinding>? keyboard = null,
        IReadOnlyList<ControlDocsStory>? related = null,
        string? componentType = null)
    {
        string prefix = $"Controls/{category}/{api}";
        string[] operationParts = Sentences(operations);
        string[] themeParts = Sentences(theme);
        string[] limitParts = Sentences(limits);
        var relatedStories = new List<ControlDocsStory>
        {
            new($"{prefix}/Playground", "プレイグラウンド", $"{title} の公開パラメーターを対話的に確認します。", StoryKind.Playground),
        };
        if (related is not null) relatedStories.AddRange(related);

        return new ControlDocsPage(
            componentType ?? $"global::Luxel.Controls.{type}",
            title,
            summary,
            useWhen,
            avoidWhen,
            alternatives ?? [],
            usage,
            $"`{title}` 本体と、型付きパラメーターで渡す子要素または表示データから構成します。",
            $"{title} 固有の表示・状態バリエーションは API 表のパラメーターで確認します。",
            state,
            operationParts[0],
            keyboard ?? [],
            DefaultFocus(category, title),
            new ControlDocsAccessibility(
                $"見えるラベルまたは周囲の説明で `{title}` の目的を示します。",
                $"`{title}` が実装する semantic role だけを利用し、追加の意味を前提にしません。",
                "選択、展開、無効などの状態は、色だけに依存せずラベルや周囲の情報でも判断できるようにします。",
                "文字、境界、状態色は利用テーマ上で十分なコントラストを確認します。",
                "アニメーションがある場合は重要情報を動きだけに依存させません。",
                operationParts.Length > 1 ? string.Join(' ', operationParts.Skip(1)) : "追加の支援技術契約は対象ホストと実装で確認します。"),
            new ControlDocsThemeLayout(
                themeParts[0],
                themeParts.Length > 1 ? themeParts[1] : "親の制約と周囲のレイアウト規則に従って配置します。",
                themeParts.Length > 2 ? string.Join(' ', themeParts.Skip(2)) : "固有寸法と Widget 共通の幅・高さ指定を組み合わせます。"),
            new ControlDocsConstraints(
                limitParts[0],
                limitParts.Length > 1 ? limitParts[1] : "外部 Signal、モデル、資源の寿命は呼び出し側が管理します。",
                limitParts.Length > 2 ? string.Join(' ', limitParts.Skip(2)) : "対応範囲は利用する描画・入力・資源ホストに従います。"),
            new ControlDocsApi(
                api,
                mainApi,
                $"イベントまたはコールバックがある場合は API 表の署名に従い、状態変更の正本は『状態の所有』に記した所有者へ反映します。"),
            new ControlDocsStory($"{prefix}/Basic", "基本例", $"{title} の最小構成を実行します。", StoryKind.Basic),
            relatedStories);
    }

    private static string DefaultFocus(string category, string title) => category switch
    {
        "Layout" or "Rendering" => $"`{title}` 自身がフォーカスを登録しない場合、操作可能な子要素だけがフォーカスを管理します。起動や閉じる動作は追加しません。",
        "Overlay" => $"`{title}` の起動元と閉じ方は open state と UiHost の overlay policy に従い、未実装のフォーカス移動を前提にしません。",
        _ => $"`{title}` のフォーカス参加、起動キー、閉じる要求は実装が登録した範囲だけを利用します。",
    };

    private static string[] Sentences(string value)
    {
        string[] parts = value.Split('。', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? [value] : parts.Select(static part => part + "。").ToArray();
    }
}
