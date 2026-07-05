using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — API リファレンス章 (Order 60〜)。静的テキストではなくレジストリから
/// 実行時に組み立てる — 型/コントロールを追加すると自動でここに載る (手書きの一覧は作らない)。
/// コントロールは <see cref="ControlApiRegistry"/> ([UiComponent] 由来)、それ以外の型は
/// <see cref="TypeApiRegistry"/> (AssemblyApi.cs の [assembly: GenerateAssemblyApi] 由来)。</summary>
public static class DocsApi
{
    [Story("Reference/Overview", Order = 60)]
    public static Widget Api(StoryContext ctx)
    {
        var s = new DocString(512, ControlApiRegistry.All.Count * 2);
        s.AppendLiteral("# コントロール API リファレンス\n\n");
        s.AppendLiteral(
            "全コントロールの **コンストラクタ引数 / イベント / パラメータ** の一覧です。" +
            "ソースジェネレーターが `[UiComponent]` の `///` コメントごと焼き込む (`ControlApiRegistry`) ため、コードと乖離しません。" +
            "見出しはファクトリ名 (`Kit` の関数名)。実物のデモはサイドバーの各章、書き方は [Docs/Controls](story:Docs/Controls) へ。\n\n");
        s.AppendLiteral(
            "「(状態対応)」の付いたパラメータは `.When(state, ...)` で状態別に上書きできます。" +
            "`Margin` / `Width` / `Height` / `HAlign` / `VAlign` / `TranslateX/Y` / `ScaleX/Y` / `Rotate` などの " +
            "**Widget 共通パラメータは各表では省略**しています (個別ページでは `ApiTable(名前, inherited: true)` で含められます)。\n");

        foreach (ControlApi api in ControlApiRegistry.All)
        {
            s.AppendLiteral($"\n## {api.Name}\n\n");
            s.AppendFormatted(ApiTable(api.Name, width: 720f));
            s.AppendLiteral("\n");
        }
        ctx.Play(static d => d.Snap());
        return WithDocFonts(Docs(ctx, s, toc: true, fences: DocsFences));
    }

    [Story("Reference/Ui", Order = 61)]
    public static Widget ApiUi(StoryContext ctx) => NamespacesPage(ctx,
        "UI API リファレンス (Luxel.UI)",
        "宣言的 UI の土台 — Widget / Signal / UiHost / テーマ / スタイリングと Tailwind パレットの公開型です。概念は [Docs/UI](story:Docs/UI) / [Docs/Styling](story:Docs/Styling) へ。",
        "Luxel.UI", "Luxel.UI.Styling", "Luxel.UI.Tailwind");

    [Story("Reference/TwoD", Order = 62)]
    public static Widget Api2D(StoryContext ctx) => NamespacesPage(ctx,
        "2D API リファレンス (Luxel.TwoD)",
        "compute ラスタライザと保持型キャンバスの公開型です。概念と使い方は [Docs/TwoD](story:Docs/TwoD)、動くデモはサイドバーの 2D 章へ。",
        "Luxel.TwoD");

    [Story("Reference/Core", Order = 63)]
    public static Widget ApiCore(StoryContext ctx) => NamespacesPage(ctx,
        "コア API リファレンス (Luxel)",
        "GPU 抽象 (GpuDevice / バッファ / パイプライン / コマンド) と診断・ウィンドウ抽象、両バックエンドの公開型です。概念は [Docs/GpuDevice](story:Docs/GpuDevice) へ。",
        "Luxel", "Luxel.Abstraction", "Luxel.Diagnostics", "Luxel.Vulkan", "Luxel.D3D12");

    [Story("Reference/Text", Order = 64)]
    public static Widget ApiText(StoryContext ctx) => NamespacesPage(ctx,
        "テキスト API リファレンス (Luxel.Typography / Document)",
        "テキストレイアウト・文書モデル・ハイライト・図・数式の公開型です。概念は [Docs/Typography](story:Docs/Typography) / [Docs/Editor](story:Docs/Editor) へ。",
        "Luxel.Typography", "Luxel.Document", "Luxel.Highlight", "Luxel.Diagram", "Luxel.MathText");

    [Story("Reference/Animation", Order = 65)]
    public static Widget ApiAnimation(StoryContext ctx) => NamespacesPage(ctx,
        "アニメーション API リファレンス (Luxel.Animation)",
        "3 層 IR (Clip / Track / Player) と UI・2D・3D ターゲットアダプタの公開型です。概念は [Docs/Animation](story:Docs/Animation) / [Docs/Transitions](story:Docs/Transitions) へ。",
        "Luxel.Animation", "Luxel.Animation.UI", "Luxel.Animation.TwoD", "Luxel.Animation.ThreeD");

    [Story("Reference/ThreeD", Order = 66)]
    public static Widget Api3D(StoryContext ctx) => NamespacesPage(ctx,
        "3D / レンダーグラフ API リファレンス",
        "レンダーグラフ・ECS・アセット・glTF の公開型です。概念は [Docs/RenderGraph](story:Docs/RenderGraph) / [Docs/ThreeD](story:Docs/ThreeD) へ。",
        "Luxel.RenderGraph", "Luxel.Ecs", "Luxel.Ecs.Signal", "Luxel.Physics", "Luxel.Assets", "Luxel.AssetsGpu", "Luxel.AssetRuntime", "Luxel.Gltf");

    [Story("Reference/Runtime", Order = 67)]
    public static Widget ApiRuntime(StoryContext ctx) => NamespacesPage(ctx,
        "ランタイム API リファレンス",
        "リソース・画像・入力・音声・プラットフォーム・アプリ骨格・DevTools の公開型です。概念は [Docs/Resources](story:Docs/Resources) / [Docs/Framework](story:Docs/Framework) ほかランタイム章へ。",
        "Luxel.Resources", "Luxel.Imaging", "Luxel.Input", "Luxel.Audio", "Luxel.Audio.Sequencing",
        "Luxel.Strudel", "Luxel.Platform", "Luxel.Framework", "Luxel.Scene.UI", "Luxel.DevTools");

    /// <summary>名前空間グループの型 API ページ (H2 = 名前空間、H3 = 型)。
    /// TypeApiRegistry から実行時に組み立てる — 型の追加はページに自動反映される。</summary>
    private static Widget NamespacesPage(StoryContext ctx, string title, string lead, params string[] namespaces)
    {
        var s = new DocString(512, 64);
        s.AppendLiteral($"# {title}\n\n");
        s.AppendLiteral(lead + " ");
        s.AppendLiteral("ソースジェネレーターが参照アセンブリの XML doc コメントから焼き込む (`[assembly: GenerateAssemblyApi]` → `TypeApiRegistry`) ため、コードと乖離しません。\n");
        foreach (string ns in namespaces)
        {
            var types = TypeApiRegistry.InNamespace(ns);
            if (types.Count == 0) continue;
            s.AppendLiteral($"\n## {ns}\n");
            foreach (TypeApi t in types)
            {
                s.AppendLiteral($"\n### {t.Name}\n\n");
                s.AppendFormatted(TypeApiTable($"{ns}.{t.Name}"));
                s.AppendLiteral("\n");
            }
        }
        return WithDocFonts(Docs(ctx, s, toc: true, fences: DocsFences));
    }
}
