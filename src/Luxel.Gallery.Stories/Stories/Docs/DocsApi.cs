using System.Runtime.CompilerServices;
using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>API reference pages generated from the control/type registries.</summary>
public static class DocsApi
{
    private static readonly object RegistrationGate = new();
    private static readonly HashSet<string> RegisteredNamespaces = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RegisteredControlCategories = new(StringComparer.Ordinal);

    internal static void RegisterReferenceProvider()
        => StoryRegistry.RegisterProvider(RegisterReferenceStories);

    /// <summary>明示 StoryCatalog 用の API/reference story 登録。</summary>
    internal static void RegisterReferenceStories(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        RuntimeHelpers.RunModuleConstructor(typeof(Luxel.Controls.Kit).Module.ModuleHandle);
        var categories = new HashSet<string>(StringComparer.Ordinal);

        foreach (string ns in TypeApiRegistry.Namespaces)
            builder.Add(new StoryInfo($"Reference/{ns}", 0, 0, null,
                ctx => NamespacePage(ctx, ns), Order: 60), replaceGenerated: true);

        foreach (ControlApi api in ControlApiRegistry.All)
        {
            if (api.Namespace != "Luxel.Controls") continue;
            string? category = ExistingControlCategory(api.Name);
            if (category is null || !categories.Add(category)) continue;
            builder.Add(api.Name == "Button"
                ? new StoryInfo("Controls/Button/Overview", 0, 0, null,
                    ctx => ControlPage(ctx, api), Order: 0,
                    ResultBuild: static _ => ButtonOverview())
                : new StoryInfo($"Controls/{category}/Overview", 0, 0, null,
                    ctx => ControlPage(ctx, api), Order: 0), replaceGenerated: true);
        }

        RegisterSpecialControlPage(builder, categories, "Layout", LayoutPage);
        RegisterSpecialControlPage(builder, categories, "Kit", KitPage);
        RegisterSpecialControlPage(builder, categories, "CommandPalette", CommandPalettePage);
    }

    private static void RegisterSpecialControlPage(StoryCatalogBuilder builder, HashSet<string> categories,
        string category, Func<StoryContext, Widget> build)
    {
        if (!categories.Add(category)) return;
        builder.Add(new StoryInfo($"Controls/{category}/Overview", 0, 0, null, build, Order: 0), replaceGenerated: true);
    }

    private static void RegisterReferenceStories()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Luxel.Controls.Kit).Module.ModuleHandle);
        lock (RegistrationGate)
        {
            foreach (string value in TypeApiRegistry.Namespaces)
            {
                string ns = value;
                if (!RegisteredNamespaces.Add(ns)) continue;
                StoryRegistry.Register(new StoryInfo($"Reference/{ns}", 0, 0, null,
                    ctx => NamespacePage(ctx, ns), Order: 60));
            }

            foreach (ControlApi value in ControlApiRegistry.All)
            {
                ControlApi api = value;
                if (api.Namespace != "Luxel.Controls") continue;
                string? category = ExistingControlCategory(api.Name);
                if (category is null || !RegisteredControlCategories.Add(category)) continue;
                StoryRegistry.Register(new StoryInfo($"Controls/{category}/Overview", 0, 0, null,
                    ctx => ControlPage(ctx, api), Order: 0));
            }

            RegisterSpecialControlPage("Layout", LayoutPage);
            RegisterSpecialControlPage("Kit", KitPage);
            RegisterSpecialControlPage("CommandPalette", CommandPalettePage);
        }
    }

    private static void RegisterSpecialControlPage(string category, Func<StoryContext, Widget> build)
    {
        if (!RegisteredControlCategories.Add(category)) return;
        StoryRegistry.Register(new StoryInfo($"Controls/{category}/Overview", 0, 0, null, build, Order: 0));
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

    private static Widget NamespacePage(StoryContext ctx, string ns)
    {
        IReadOnlyList<TypeApi> types = TypeApiRegistry.InNamespace(ns);
        var s = new DocString(512, types.Count);
        s.AppendLiteral($"# {ns}\n\n");
        s.AppendLiteral("この名前空間の公開型 API です。ソースジェネレーターが参照アセンブリの XML doc コメントから焼き込む (`[assembly: GenerateAssemblyApi]` → `TypeApiRegistry`) ため、コードと乖離しません。\n");
        foreach (TypeApi type in types)
        {
            s.AppendLiteral($"\n## {type.Name}\n\n");
            s.AppendFormatted(TypeApiReference($"{ns}.{type.Name}"));
            s.AppendLiteral("\n");
        }
        return DocNew(ctx, s, toc: true);
    }

    private static StoryResult ButtonOverview() => $$"""
        # Button

        `Button` executes an action in response to pointer activation. The browser bundle currently hosts
        the canonical interactive counter story; component-playground hosting remains native until the
        generated component catalog can be linked into the browser-safe dependency closure.

        {{StoryReference.To("Controls/Button/Counter", new { count = 0 })}}

        The interactive reference above is isolated in its own browser runtime iframe; the surrounding
        overview remains semantic HTML.
        """;

    private static Widget ControlPage(StoryContext ctx, ControlApi api)
    {
        var s = new DocString(256, 1);
        s.AppendLiteral($"# {api.Name}\n\n");
        s.AppendLiteral($"`{api.Namespace}` のコントロール API です。コンストラクタ引数・イベント・パラメータは `[UiComponent]` のソースと XML doc コメントから生成されます。\n\n");
        s.AppendFormatted(ControlApiReference(api.Name));
        return DocNew(ctx, s);
    }

    private static Widget LayoutPage(StoryContext ctx)
    {
        var s = new DocString(384, 4);
        s.AppendLiteral("# Layout\n\n`Controls/Layout` のデモで使う基本レイアウトコントロールです。個別カテゴリを持たないプリミティブをまとめています。\n");
        foreach (string name in new[] { "Box", "Stack", "Center", "Spacer" })
        {
            s.AppendLiteral($"\n## {name}\n\n");
            s.AppendFormatted(ControlApiReference(name));
        }
        return DocNew(ctx, s, toc: true);
    }

    private static Widget KitPage(StoryContext ctx) => DocNew(ctx, $$"""
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

        実例は [Badges](story:Controls/Kit/Badges) / [Alert](story:Controls/Kit/Alert) / [Typography](story:Controls/Kit/Typography) へ。
        """, toc: true);

    private static Widget CommandPalettePage(StoryContext ctx) => DocNew(ctx, $$"""
        # CommandPalette

        `CommandPalette` は `[UiComponent]` ではなく、前面overlayへパレットを開くstatic APIです。

        ```csharp
        CommandPalette.PaletteView CommandPalette.Open(
            UiBuildContext ctx,
            CommandRegistry registry,
            IReadOnlyList<CommandContribution>? contributions = null)
        ```

        戻り値の `PaletteView` はplay/testで現在の絞り込み結果 (`Filtered`) と入力欄 (`Field`) を参照できます。実例は [Basic](story:Controls/CommandPalette/Basic) へ。
        """, toc: true);

    internal static DocEmbed ControlApiReference(string name, bool inherited = false, float width = 720f)
        => new(ApiTable(name, inherited: inherited, width: width), DocEmbedKind.ControlApiTable, name,
            IncludeInherited: inherited);

    internal static DocEmbed TypeApiReference(string name, float width = 760f)
        => new(TypeApiTable(name, width: width), DocEmbedKind.TypeApiTable, name);
}
