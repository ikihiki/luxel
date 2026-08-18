using System.Runtime.CompilerServices;
using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.DocKit.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>API reference pages generated from the control/type registries.</summary>
[StoryMeta("Controls/Button")]
internal static class ControlDocsApi
{
    private static readonly object RegistrationGate = new();
    private static readonly HashSet<string> RegisteredNamespaces = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RegisteredControlCategories = new(StringComparer.Ordinal);

    internal static void RegisterControlProvider()
        => StoryRegistry.RegisterProvider(RegisterControlStories);

    /// <summary>明示 StoryCatalog 用の API/reference story 登録。</summary>
    internal static void RegisterControlStories(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        RuntimeHelpers.RunModuleConstructor(typeof(Luxel.Controls.Kit).Module.ModuleHandle);
        var categories = new HashSet<string>(StringComparer.Ordinal);

        foreach (ControlApi api in ControlApiRegistry.All)
        {
            if (api.Namespace != "Luxel.Controls") continue;
            string? category = ExistingControlCategory(api.Name);
            if (category is null || !categories.Add(category)) continue;
            string path = $"Controls/{category}/Docs";
            if (builder.ContainsPath(path)) continue;
            builder.Add(api.Name == "Button"
                ? new StoryInfo(path,
                    static _ => ButtonDocs(), Source: "Generated component docs.")
                : new StoryInfo(path,
                    ctx => ControlPage(ctx, api), Source: "Generated component docs."), replaceGenerated: true);
        }

        RegisterSpecialControlPage(builder, categories, "Layout", LayoutPage);
        RegisterSpecialControlPage(builder, categories, "Kit", KitPage);
        RegisterSpecialControlPage(builder, categories, "CommandPalette", CommandPalettePage);
    }

    private static void RegisterSpecialControlPage(StoryCatalogBuilder builder, HashSet<string> categories,
        string category, Func<StoryContext, StoryResult> build)
    {
        if (!categories.Add(category)) return;
        builder.Add(new StoryInfo($"Controls/{category}/Docs", build, Source: "Generated component docs."),
            replaceGenerated: true);
    }

    private static void RegisterControlStories()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Luxel.Controls.Kit).Module.ModuleHandle);
        lock (RegistrationGate)
        {

            foreach (ControlApi value in ControlApiRegistry.All)
            {
                ControlApi api = value;
                if (api.Namespace != "Luxel.Controls") continue;
                string? category = ExistingControlCategory(api.Name);
                if (category is null || !RegisteredControlCategories.Add(category)) continue;
                StoryRegistry.Register(new StoryInfo($"Controls/{category}/Docs",
                    ctx => ControlPage(ctx, api), Source: "Generated component docs."));
            }

            RegisterSpecialControlPage("Layout", LayoutPage);
            RegisterSpecialControlPage("Kit", KitPage);
            RegisterSpecialControlPage("CommandPalette", CommandPalettePage);
        }
    }

    private static void RegisterSpecialControlPage(string category, Func<StoryContext, StoryResult> build)
    {
        if (!RegisteredControlCategories.Add(category)) return;
        StoryRegistry.Register(new StoryInfo($"Controls/{category}/Docs", build,
            Source: "Generated component docs."));
    }

    private static string? ExistingControlCategory(string apiName) => apiName switch
    {
        "Check" => "CheckBox",
        "KnobsTable" => "Knobs",
        "RichTextView" => "RichText",
        "Scroll" => "ScrollViewer",
        "Wrap" => "WrapPanel",
        "ApiTable" or "Box" or "Canvas2D" or "Center" or "GpuView" or "SceneInspector" or "Spacer" or "Stack" or "TypeApiTable" => null,
        _ => apiName,
    };

    private static StoryResult NamespacePage(StoryContext ctx, string ns)
    {
        IReadOnlyList<TypeApi> types = TypeApiRegistry.InNamespace(ns);
        var s = new StoryResult(512, types.Count);
        s.AppendLiteral($"# {ns}\n\n");
        s.AppendLiteral("この名前空間の公開型 API です。ソースジェネレーターが参照アセンブリの XML doc コメントから焼き込む (`[assembly: GenerateAssemblyApi]` → `TypeApiRegistry`) ため、コードと乖離しません。\n");
        foreach (TypeApi type in types)
        {
            s.AppendLiteral($"\n## {type.Name}\n\n");
            s.AppendFormatted(TypeApiReference($"{ns}.{type.Name}"));
            s.AppendLiteral("\n");
        }
        return s;
    }

    private static StoryResult ButtonDocs() => $$"""
        # Button

        `Button` executes an action in response to pointer activation. The browser bundle currently hosts
        the canonical interactive counter story; component-playground hosting remains native until the
        generated component catalog can be linked into the browser-safe dependency closure.

        {{StoryReference.To("Controls/Button/Examples/Counter", new { count = 0 })}}

        The interactive reference above is isolated in its own browser runtime iframe; the surrounding
        overview remains semantic HTML.
        """;

    private static StoryResult ControlPage(StoryContext ctx, ControlApi api)
    {
        var s = new StoryResult(256, 1);
        s.AppendLiteral($"# {api.Name}\n\n");
        s.AppendLiteral($"`{api.Namespace}` のコントロール API です。コンストラクタ引数・イベント・パラメータは `[UiComponent]` のソースと XML doc コメントから生成されます。\n\n");
        s.AppendFormatted(ControlApiReference(api.Name));
        return s;
    }

    public static IReadOnlyList<StoryArgDefinition> CounterSampleArgs() => InputControlStories.CounterArgs();

    [Story(Path = "Controls/Button/Examples/Counter", Args = nameof(CounterSampleArgs))]
    public static StoryResult CounterSample(StoryContext ctx) => InputControlStories.ButtonCounter(ctx);

    private static StoryResult LayoutPage(StoryContext ctx)
    {
        var s = new StoryResult(384, 4);
        s.AppendLiteral("# Layout\n\n`Controls/Layout` のデモで使う基本レイアウトコントロールです。個別カテゴリを持たないプリミティブをまとめています。\n");
        foreach (string name in new[] { "Box", "Stack", "Center", "Spacer" })
        {
            s.AppendLiteral($"\n## {name}\n\n");
            s.AppendFormatted(ControlApiReference(name));
        }
        return s;
    }

    private static StoryResult KitPage(StoryContext ctx) => $$"""
        # Kit

        `Luxel.Controls.Kit` の複合helperです。`[UiComponent]` ではないため生成 `ApiTable` は持たず、公開signatureをここにまとめます。

        ```csharp
        Text Heading(string text, int level = 1)
        Text Label(string text)
        Text Muted(string text)
        Border Card(Widget child)
        Box Divider()
        Border Badge(string text, Intent intent = Intent.Primary)
        Border Chip(string text)
        Border Alert(string message, Intent intent = Intent.Info)
        Box Skeleton(float width, float height)
        Border Breadcrumb(params string[] crumbs)
        ```

        実例は [Badges](story:Controls/Kit/Examples/Badges) / [Alert](story:Controls/Kit/Examples/Alert) / [Typography](story:Controls/Kit/Examples/Typography) へ。
        """;

    private static StoryResult CommandPalettePage(StoryContext ctx) => $$"""
        # CommandPalette

        `CommandPalette` は `[UiComponent]` ではなく、前面overlayへパレットを開くstatic APIです。

        ```csharp
        CommandPalette.PaletteView CommandPalette.Open(
            UiBuildContext ctx,
            CommandRegistry registry,
            IReadOnlyList<CommandContribution>? contributions = null)
        ```

        戻り値の `PaletteView` はplay/testで現在の絞り込み結果 (`Filtered`) と入力欄 (`Field`) を参照できます。実例は [Basic](story:Controls/CommandPalette/Basic) へ。
        """;

    internal static DocEmbed ControlApiReference(string name, bool inherited = false, float width = 720f)
        => new(global::Luxel.Gallery.UI.Kit.ApiTable(name, inherited: inherited, width: width), DocEmbedKind.ControlApiTable, name,
            IncludeInherited: inherited);

    internal static DocEmbed TypeApiReference(string name, float width = 760f)
        => new(global::Luxel.Gallery.UI.Kit.TypeApiTable(name, width: width), DocEmbedKind.TypeApiTable, name);
}
