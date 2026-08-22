using Luxel.Controls;

namespace Luxel.Gallery.UI;

/// <summary>Structured, host-neutral documentation for one generated production control.</summary>
public sealed record ControlDocsPage(
    string ComponentType,
    string Title,
    string Summary,
    IReadOnlyList<string> UseWhen,
    IReadOnlyList<string> AvoidWhen,
    IReadOnlyList<ControlDocsAlternative> Alternatives,
    string UsageSnippet,
    string Anatomy,
    string Variants,
    string StateOwnership,
    string PointerInteraction,
    IReadOnlyList<ControlDocsKeyboardBinding> KeyboardBindings,
    string FocusActivationDismissal,
    ControlDocsAccessibility Accessibility,
    ControlDocsThemeLayout ThemeLayout,
    ControlDocsConstraints Constraints,
    ControlDocsApi Api,
    ControlDocsStory PrimaryStory,
    IReadOnlyList<ControlDocsStory> RelatedStories);

/// <summary>An alternative or adjacent component and the decision boundary for choosing it.</summary>
public sealed record ControlDocsAlternative(string Component, string Description);

/// <summary>One semantic keyboard binding rendered as a Markdown table row.</summary>
public sealed record ControlDocsKeyboardBinding(string Keys, string Action);

/// <summary>Accessibility contract and known limits for a control.</summary>
public sealed record ControlDocsAccessibility(
    string Name,
    string Semantics,
    string State,
    string Contrast,
    string Motion,
    string Limitations);

/// <summary>Theme, layout, and sizing guidance for a control.</summary>
public sealed record ControlDocsThemeLayout(string Theme, string Layout, string Sizing);

/// <summary>Operational constraints, lifecycle ownership, and platform requirements.</summary>
public sealed record ControlDocsConstraints(string Constraints, string Lifecycle, string Platforms);

/// <summary>Generated API identity plus authored usage and event contracts.</summary>
public sealed record ControlDocsApi(string Identity, string Highlights, string EventContracts);

/// <summary>A canonical Gallery story link with its expected execution kind.</summary>
public sealed record ControlDocsStory(string Path, string Label, string Description, StoryKind ExpectedKind);

/// <summary>Registers and renders structured component documentation with one shared Japanese template.</summary>
public static class ControlDocsRenderer
{
    /// <summary>The stable H2 order shared by UI, Editor, and Particle control docs.</summary>
    public static IReadOnlyList<string> HeadingOrder { get; } =
    [
        "## 概要",
        "## 使う場面・避ける場面",
        "## 主な使用例",
        "## バリエーション・状態とその所有",
        "## 操作・キーボード・アクセシビリティ",
        "## テーマ・レイアウト・サイズ",
        "## 制約・ライフサイクル・対応プラットフォーム",
        "## パラメーター・イベント・API",
        "## 関連する例・コンポーネント",
    ];

    public static void Register(
        StoryCatalogBuilder builder,
        IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors,
        IEnumerable<ControlDocsPage> pages,
        string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        Dictionary<string, GeneratedComponentStoryDescriptor> byType = descriptors
            .ToDictionary(static descriptor => descriptor.ComponentType, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ControlDocsPage page in pages)
        {
            if (!seen.Add(page.ComponentType))
                throw new InvalidOperationException($"Docs model contains duplicate component identity: {page.ComponentType}");
            if (!byType.TryGetValue(page.ComponentType, out GeneratedComponentStoryDescriptor? descriptor))
                throw new InvalidOperationException($"Docs 対象のコンポーネントが見つかりません: {page.ComponentType}");
            Validate(page, descriptor);
            builder.Add(new StoryInfo(descriptor.DocsPath, _ => Render(page, descriptor), Source: source), replaceGenerated: true);
        }
    }

    public static StoryResult Render(ControlDocsPage page, GeneratedComponentStoryDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(descriptor);
        Validate(page, descriptor);

        var result = new StoryResult(4600, 2);
        result.AppendLiteral($"# {page.Title}\n\n## 概要\n\n{page.Summary}\n\n## 使う場面・避ける場面\n\n### 使う場面\n\n");
        AppendBullets(result, page.UseWhen);
        result.AppendLiteral("\n### 避ける場面\n\n");
        AppendBullets(result, page.AvoidWhen);
        if (page.Alternatives.Count > 0)
        {
            result.AppendLiteral("\n### 代替・関連コンポーネント\n\n");
            foreach (ControlDocsAlternative alternative in page.Alternatives)
                result.AppendLiteral($"- `{alternative.Component}` — {alternative.Description}\n");
        }

        result.AppendLiteral("\n## 主な使用例\n\n");
        result.AppendFormatted(StoryReference.To(page.PrimaryStory.Path));
        result.AppendLiteral($"\n\n```csharp\n{page.UsageSnippet}\n```\n\n## バリエーション・状態とその所有\n\n### 構造\n\n{page.Anatomy}\n\n### バリエーション\n\n{page.Variants}\n\n### 状態の所有\n\n{page.StateOwnership}\n\n## 操作・キーボード・アクセシビリティ\n\n### ポインター操作\n\n{page.PointerInteraction}\n\n### キーボード\n\n| キー | 操作 |\n| --- | --- |\n");
        if (page.KeyboardBindings.Count == 0)
        {
            result.AppendLiteral("| — | 専用のキーボード割り当てはありません。 |\n");
        }
        else
        {
            foreach (ControlDocsKeyboardBinding binding in page.KeyboardBindings)
                result.AppendLiteral($"| {EscapeTable(binding.Keys)} | {EscapeTable(binding.Action)} |\n");
        }

        result.AppendLiteral($"\n### フォーカス・起動・閉じ方\n\n{page.FocusActivationDismissal}\n\n### アクセシビリティ\n\n- **名前:** {page.Accessibility.Name}\n- **意味:** {page.Accessibility.Semantics}\n- **状態:** {page.Accessibility.State}\n- **コントラスト:** {page.Accessibility.Contrast}\n- **モーション:** {page.Accessibility.Motion}\n- **既知の制約:** {page.Accessibility.Limitations}\n\n## テーマ・レイアウト・サイズ\n\n- **テーマ:** {page.ThemeLayout.Theme}\n- **レイアウト:** {page.ThemeLayout.Layout}\n- **サイズ:** {page.ThemeLayout.Sizing}\n\n## 制約・ライフサイクル・対応プラットフォーム\n\n- **制約:** {page.Constraints.Constraints}\n- **ライフサイクル:** {page.Constraints.Lifecycle}\n- **対応プラットフォーム:** {page.Constraints.Platforms}\n\n## パラメーター・イベント・API\n\n- **API identity:** `{page.Api.Identity}`\n- **主なパラメーター:** {page.Api.Highlights}\n- **イベント契約:** {page.Api.EventContracts}\n\n");
        result.AppendFormatted(new DocEmbed(
            Widget: null,
            Kind: DocEmbedKind.ControlApiTable,
            Reference: page.Api.Identity,
            IncludeInherited: true,
            WidgetFactory: () => Kit.ApiTable(page.Api.Identity, inherited: true, width: 760)));

        result.AppendLiteral("\n\n## 関連する例・コンポーネント\n\n");
        foreach (ControlDocsStory story in page.RelatedStories)
            result.AppendLiteral($"- [{story.Label}](story:{story.Path}) — {story.Description}\n");
        return result;
    }

    private static void Validate(ControlDocsPage page, GeneratedComponentStoryDescriptor descriptor)
    {
        Require(page.ComponentType, nameof(page.ComponentType));
        Require(page.Title, nameof(page.Title));
        Require(page.Summary, nameof(page.Summary));
        RequireList(page.UseWhen, nameof(page.UseWhen), requireItem: true);
        RequireList(page.AvoidWhen, nameof(page.AvoidWhen), requireItem: true);
        RequireList(page.Alternatives, nameof(page.Alternatives));
        Require(page.UsageSnippet, nameof(page.UsageSnippet));
        Require(page.Anatomy, nameof(page.Anatomy));
        Require(page.Variants, nameof(page.Variants));
        Require(page.StateOwnership, nameof(page.StateOwnership));
        Require(page.PointerInteraction, nameof(page.PointerInteraction));
        RequireList(page.KeyboardBindings, nameof(page.KeyboardBindings));
        Require(page.FocusActivationDismissal, nameof(page.FocusActivationDismissal));
        ArgumentNullException.ThrowIfNull(page.Accessibility);
        Require(page.Accessibility.Name, $"{nameof(page.Accessibility)}.{nameof(page.Accessibility.Name)}");
        Require(page.Accessibility.Semantics, $"{nameof(page.Accessibility)}.{nameof(page.Accessibility.Semantics)}");
        Require(page.Accessibility.State, $"{nameof(page.Accessibility)}.{nameof(page.Accessibility.State)}");
        Require(page.Accessibility.Contrast, $"{nameof(page.Accessibility)}.{nameof(page.Accessibility.Contrast)}");
        Require(page.Accessibility.Motion, $"{nameof(page.Accessibility)}.{nameof(page.Accessibility.Motion)}");
        Require(page.Accessibility.Limitations, $"{nameof(page.Accessibility)}.{nameof(page.Accessibility.Limitations)}");
        ArgumentNullException.ThrowIfNull(page.ThemeLayout);
        Require(page.ThemeLayout.Theme, $"{nameof(page.ThemeLayout)}.{nameof(page.ThemeLayout.Theme)}");
        Require(page.ThemeLayout.Layout, $"{nameof(page.ThemeLayout)}.{nameof(page.ThemeLayout.Layout)}");
        Require(page.ThemeLayout.Sizing, $"{nameof(page.ThemeLayout)}.{nameof(page.ThemeLayout.Sizing)}");
        ArgumentNullException.ThrowIfNull(page.Constraints);
        Require(page.Constraints.Constraints, $"{nameof(page.Constraints)}.{nameof(page.Constraints.Constraints)}");
        Require(page.Constraints.Lifecycle, $"{nameof(page.Constraints)}.{nameof(page.Constraints.Lifecycle)}");
        Require(page.Constraints.Platforms, $"{nameof(page.Constraints)}.{nameof(page.Constraints.Platforms)}");
        ArgumentNullException.ThrowIfNull(page.Api);
        Require(page.Api.Identity, $"{nameof(page.Api)}.{nameof(page.Api.Identity)}");
        Require(page.Api.Highlights, $"{nameof(page.Api)}.{nameof(page.Api.Highlights)}");
        Require(page.Api.EventContracts, $"{nameof(page.Api)}.{nameof(page.Api.EventContracts)}");
        ArgumentNullException.ThrowIfNull(page.PrimaryStory);
        ValidateStory(page.PrimaryStory, nameof(page.PrimaryStory));
        RequireList(page.RelatedStories, nameof(page.RelatedStories), requireItem: true);

        if (!string.Equals(page.ComponentType, descriptor.ComponentType, StringComparison.Ordinal))
            throw new InvalidOperationException($"Docs component identity does not match descriptor: {page.ComponentType} != {descriptor.ComponentType}");
        if (!string.Equals(page.Api.Identity, descriptor.ControlName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Docs API identity does not match descriptor: {page.Api.Identity} != {descriptor.ControlName}");
        if (!string.Equals(page.PrimaryStory.Path, descriptor.BasicPath, StringComparison.Ordinal)
            || page.PrimaryStory.ExpectedKind != StoryKind.Basic)
            throw new InvalidOperationException($"Primary story must be the canonical Basic story: {descriptor.BasicPath}");

        var paths = new HashSet<string>(StringComparer.Ordinal) { page.PrimaryStory.Path };
        int playgroundCount = 0;
        foreach (ControlDocsStory story in page.RelatedStories)
        {
            ValidateStory(story, nameof(page.RelatedStories));
            if (!paths.Add(story.Path))
                throw new InvalidOperationException($"Docs story path is duplicated: {story.Path}");
            if (string.Equals(story.Path, descriptor.PlaygroundPath, StringComparison.Ordinal))
            {
                playgroundCount++;
                if (story.ExpectedKind != StoryKind.Playground)
                    throw new InvalidOperationException($"Playground story has the wrong expected kind: {story.Path}");
            }
        }
        if (playgroundCount != 1)
            throw new InvalidOperationException($"Docs must link the canonical Playground exactly once: {descriptor.PlaygroundPath}");
    }

    private static void ValidateStory(ControlDocsStory story, string field)
    {
        Require(story.Path, $"{field}.Path");
        Require(story.Label, $"{field}.Label");
        Require(story.Description, $"{field}.Description");
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Structured control docs field is required: {field}");
    }

    private static void RequireList<T>(IReadOnlyList<T>? values, string field, bool requireItem = false)
    {
        if (values is null || (requireItem && values.Count == 0))
            throw new InvalidOperationException($"Structured control docs list is required: {field}");
        for (int index = 0; index < values.Count; index++)
        {
            switch (values[index])
            {
                case string value:
                    Require(value, $"{field}[{index}]");
                    break;
                case ControlDocsAlternative alternative:
                    Require(alternative.Component, $"{field}[{index}].Component");
                    Require(alternative.Description, $"{field}[{index}].Description");
                    break;
                case ControlDocsKeyboardBinding binding:
                    Require(binding.Keys, $"{field}[{index}].Keys");
                    Require(binding.Action, $"{field}[{index}].Action");
                    break;
            }
        }
    }

    private static void AppendBullets(StoryResult result, IReadOnlyList<string> items)
    {
        foreach (string item in items) result.AppendLiteral($"- {item}\n");
    }

    private static string EscapeTable(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
}
