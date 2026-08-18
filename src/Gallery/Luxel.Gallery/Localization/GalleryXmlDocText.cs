namespace Luxel.Gallery;

/// <summary>Resolves XML documentation identities to Gallery-owned Japanese display text.</summary>
public static class GalleryXmlDocText
{
    private static readonly IReadOnlyDictionary<string, string> Japanese =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["xml:T:Luxel.Controls.NavigationView"] = "固定されたナビゲーションペインと、選択中の項目に対応するコンテンツを表示します。ナビゲーション状態は呼び出し側が所有します。",
            ["xml:P:Luxel.Controls.NavigationView.Navigation"] = "コンテンツと共有するナビゲーション状態です。",
            ["xml:P:Luxel.Controls.NavigationView.Items"] = "ナビゲーションペインに表示する移動先の一覧です。",
            ["xml:P:Luxel.Controls.NavigationView.ShowBackButton"] = "戻るボタンの行を表示するかどうかを指定します。",
            ["xml:P:Luxel.Controls.NavigationView.PaneWidth"] = "展開時のナビゲーションペインの幅をピクセル単位で指定します。",
            ["xml:P:Luxel.Controls.NavigationView.ItemHeight"] = "各ナビゲーション項目の高さをピクセル単位で指定します。",
            ["xml:P:Luxel.Controls.NavigationView.PaneBackground"] = "ペインの背景色です。未設定時は現在のテーマのサーフェス色を使用します。",
            ["xml:P:Luxel.Controls.NavigationView.ItemForeground"] = "通常項目の前景色です。未設定時はテーマの控えめなテキスト色を使用します。",
            ["xml:P:Luxel.Controls.NavigationView.SelectedBackground"] = "選択項目の背景色です。未設定時はテーマの代替サーフェス色を使用します。",
            ["xml:P:Luxel.Controls.NavigationView.SelectedForeground"] = "選択項目の前景色です。未設定時はテーマのプライマリ色を使用します。",
        };

    /// <summary>Returns the registered Japanese text, or the original XML summary when no translation exists.</summary>
    public static string Resolve(string key, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        fallback ??= string.Empty;
        return Japanese.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    internal static IReadOnlyDictionary<string, string> Entries => Japanese;
}
