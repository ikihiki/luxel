using Luxel.Gallery.UI;

namespace Luxel.Editor.Gallery;

/// <summary>利用者向け Editor コンポーネントの構造化された日本語 Docs。</summary>
internal static partial class EditorControlDocs
{
    internal static void Register(StoryCatalogBuilder builder, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors)
        => ControlDocsRenderer.Register(builder, descriptors, Pages,
            "日本語で執筆した構造化 Editor コンポーネント Docs。");

    private static ControlDocsPage Page(
        string type,
        string category,
        string summary,
        string useWhen,
        string avoidWhen,
        string usage,
        string state,
        string mainApi,
        string operations,
        string theme,
        string limits,
        IReadOnlyList<ControlDocsAlternative>? alternatives = null,
        IReadOnlyList<ControlDocsKeyboardBinding>? keyboard = null)
    {
        string prefix = $"Controls/{category}/{type}";
        string[] operationParts = Sentences(operations);
        string[] themeParts = Sentences(theme);
        string[] limitParts = Sentences(limits);
        return new ControlDocsPage(
            $"global::Luxel.Controls.{type}",
            type,
            summary,
            [useWhen],
            [avoidWhen],
            alternatives ?? [],
            usage,
            $"`{type}` と、Editor のモデルまたはサービスから渡す表示データ・子ビューで構成します。",
            $"{type} 固有の表示や状態バリエーションは API 表のパラメーターで確認します。",
            state,
            operationParts[0],
            keyboard ?? [],
            $"`{type}` が登録する focus と起動操作だけを利用し、未実装のキーボード経路はメニューや一覧などで補います。",
            new ControlDocsAccessibility(
                $"見える見出し、コマンド名、項目名で `{type}` の目的を示します。",
                $"`{type}` と内部コントロールが公開する semantic role だけを利用します。",
                "選択、無効、変更済みなどの状態は文字または形状でも判断できるようにします。",
                "エディターテーマ上で文字、境界、選択、フォーカスのコントラストを確認します。",
                "ドラッグや表示遷移だけに重要な情報を依存させません。",
                operationParts.Length > 1 ? string.Join(' ', operationParts.Skip(1)) : "完全な支援技術経路は組み込む子コントロールと対象ホストで検証します。"),
            new ControlDocsThemeLayout(
                themeParts[0],
                themeParts.Length > 1 ? themeParts[1] : "Workbench の隣接パネルと境界・余白を揃えます。",
                themeParts.Length > 2 ? string.Join(' ', themeParts.Skip(2)) : "編集内容を確認できる有限の幅と高さを与えます。"),
            new ControlDocsConstraints(
                limitParts[0],
                limitParts.Length > 1 ? limitParts[1] : "モデル、サービス、購読、子ビューの寿命は Editor session 側が管理します。",
                limitParts.Length > 2 ? string.Join(' ', limitParts.Skip(2)) : "Portable Editor ホストで利用でき、外部サービスの能力は構成に依存します。"),
            new ControlDocsApi(
                type,
                mainApi,
                "UiEvent またはコールバックは要求を通知します。モデル変更、永続化、Undo、エラー処理の正本は Editor session 側で更新します。"),
            new ControlDocsStory($"{prefix}/Basic", "基本例", $"{type} の最小構成を実行します。", StoryKind.Basic),
            [new($"{prefix}/Playground", "プレイグラウンド", $"{type} の公開パラメーターを対話的に確認します。", StoryKind.Playground)]);
    }

    private static string[] Sentences(string value)
    {
        string[] parts = value.Split('。', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? [value] : parts.Select(static part => part + "。").ToArray();
    }
}
