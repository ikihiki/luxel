using Luxel.Controls;
using Luxel.Gallery.Stories;
using Luxel.Scripting;
using Luxel.TwoD;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Gallery;

/// <summary>
/// ストーリーが DI で借りる共有サービスの登録簿。**重いシングルトン** (Roslyn の ScriptHost /
/// 言語サービス ScriptWorkspace) をここに一元化し、GalleryHost と E2E 検出パスの両方が
/// 同じ <see cref="IServiceProvider"/> を <c>ctx.SetServices</c> で結線する。ストーリー関数は
/// ASP.NET minimal API と同じく引数で受け取る:
/// <code>
/// [Story("...")]
/// public static Widget Foo(StoryContext ctx, ScriptHost host) => ...;   // host は DI 注入
/// </code>
/// これで各ストーリーが <c>static Lazy&lt;ScriptHost&gt;</c> を個別に抱える重複が消える。
/// </summary>
public static class GalleryServices
{
    // スクリプトから見える API 面 (参照 + using) — Host も Workspace も同じものを使う
    private static readonly System.Reflection.Assembly[] Refs =
    [
        typeof(object).Assembly, typeof(Enumerable).Assembly,
        typeof(GpuDevice).Assembly, typeof(Scene2D).Assembly,
        typeof(Widget).Assembly, typeof(Kit).Assembly,
        typeof(Luxel.UI.Tailwind.Tw).Assembly, typeof(ScriptGlobals).Assembly,
    ];
    private static readonly string[] Usings =
    [
        "System", "System.Linq", "System.Collections.Generic",
        "Luxel.UI", "Luxel.TwoD", "Luxel.Controls",
        "Luxel.Controls.Kit",       // 静的 import — Button/VStack/… が裸で書ける
        "Luxel.UI.Tailwind",
    ];

    /// <summary>UI (Widget を返す) スクリプトの役割名。</summary>
    public const string UiProfile = "ui";

    /// <summary>UI スクリプトのプロファイル (docs/Gallery のライブブロック用)。</summary>
    private static ScriptProfile UiScriptProfile => new(UiProfile, Refs, Usings, typeof(ScriptGlobals));

    private static readonly Lazy<IServiceProvider> Lazy = new(Build);

    /// <summary>プロセス共有のサービスプロバイダ (初回参照で構築 — ScriptHost 初回コンパイルは 1-2 秒)。</summary>
    public static IServiceProvider Provider => Lazy.Value;

    private static IServiceProvider Build()
    {
        // 役割ごとの ScriptHost を束ねる登録簿 (UI 用をここで、ゲーム/ECS 用は各機能側が GetOrAdd で足す)。
        var registry = new ScriptHostRegistry();
        registry.Register(UiScriptProfile);

        var services = new ServiceCollection();
        services.AddSingleton(registry);
        // 既存ストーリー互換: ScriptHost/ScriptWorkspace/ICodeLanguage は UI プロファイルへ解決
        services.AddSingleton(sp => sp.GetRequiredService<ScriptHostRegistry>().Host(UiProfile));
        services.AddSingleton(sp => sp.GetRequiredService<ScriptHostRegistry>().Workspace(UiProfile));
        services.AddSingleton<ICodeLanguage>(sp => new Luxel.Gallery.Stories.CsharpCodeLanguage(sp.GetRequiredService<ScriptWorkspace>()));
        return services.BuildServiceProvider();
    }
}
