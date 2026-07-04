using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — コントロール API リファレンス。静的テキストではなく
/// <see cref="ControlApiRegistry"/> から実行時に組み立てる — コントロールを追加すると
/// 自動でここに載る (手書きの一覧は作らない)。</summary>
public static class DocsApi
{
    [Story("Docs/Api", Order = 29)]
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
        return WithDocFonts(Docs(ctx, s, toc: true, fences: DocsFences));
    }

    [Story("Docs/Api2D", Order = 14)]
    public static Widget Api2D(StoryContext ctx)
    {
        var types = TypeApiRegistry.InNamespace("Luxel.TwoD");
        var s = new DocString(512, types.Count * 2);
        s.AppendLiteral("# 2D API リファレンス (Luxel.TwoD)\n\n");
        s.AppendLiteral(
            "`Luxel.TwoD` の公開型の **コンストラクタ / メソッド / プロパティ / フィールド** の一覧です。" +
            "ソースジェネレーターが参照アセンブリの XML doc コメントから焼き込む (`[assembly: GenerateAssemblyApi]` → `TypeApiRegistry`) ため、コードと乖離しません。" +
            "概念と使い方は [Docs/TwoD](story:Docs/TwoD)、動くデモはサイドバーの 2D 章へ。\n");

        foreach (TypeApi t in types)
        {
            s.AppendLiteral($"\n## {t.Name}\n\n");
            s.AppendFormatted(TypeApiTable(t.Name));
            s.AppendLiteral("\n");
        }
        return WithDocFonts(Docs(ctx, s, toc: true, fences: DocsFences));
    }
}
