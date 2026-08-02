using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class StartStories
{
    [Story("Start/Welcome", Order = 0)]
    public static Widget Welcome(StoryContext ctx) => DocNew(ctx, $$"""
        # Luxel Gallery — 見て、コピーして、アプリを作る

        Gallery は動く教科書です。説明を読んだら preview を操作し、**検証済み Sample Bundle** の実ファイルをコピーしてください。

        ## 学習ルート

        - **GPU が初めて** — [Grapics](story:Learn/Grapics/Overview) で window、device、surface、三角形まで進む
        - **3D アプリを作る** — [ThreeD](story:Learn/Grapics/ThreeD/Textures) で texture、camera、depth、lighting、RenderGraph を組み合わせる
        - **2D アプリを作る** — [TwoD](story:Learn/Grapics/TwoD/Overview) で path、camera、retained canvas を使う
        - **Input / Audio / Resources** — [Input](story:Learn/Input/Overview)、[Audio](story:Learn/Audio/Overview)、[Resources](story:Learn/Resources/Overview)でapp runtimeを組む
        - **2D rasterizer の中を読む** — [Rasterizer Internals](story:Learn/Grapics/RasterizerInternals/Overview) で encode、bounds、bin、fine pass を追う

        ## コードの保証レベル

        - **Snippet**: 既存アプリへ貼る短い断片
        - **Block**: App Host の接続点へ追加できる機能単位
        - **Recipe**: 複数 block を組み合わせて build 検証した構成
        - **StandaloneProject**: `.csproj`、C#、shader、asset を含む実行可能プロジェクト
        - **GalleryOnly**: `StoryContext` など Gallery harness が必要。Source タブだけでは standalone にならない

        次に [自分のルートを選ぶ](story:Start/ChooseYourPath) か、[Gallery の使い方](story:Start/GalleryGuide) を開いてください。
        """, toc: true);

    [Story("Start/ChooseYourPath", Order = 1)]
    public static Widget ChooseYourPath(StoryContext ctx) => DocNew(ctx, $$"""
        # 自分のルートを選ぶ

        | 目的 | 開始ページ | 到達物 |
        |---|---|---|
        | 最初の GPU アプリ | [Grapics](story:Learn/Grapics/Overview) | standalone triangle |
        | 実用 3D | [ThreeD](story:Learn/Grapics/ThreeD/Textures) | textured 3D viewer |
        | 2D canvas | [TwoD](story:Learn/Grapics/TwoD/Overview) | input と camera 付き 2D app |
        | 実装読解 | [Rasterizer Internals](story:Learn/Grapics/RasterizerInternals/Overview) | C# から compute pass まで説明できる |

        初心者は Grapics 直下のページを順番に進めてください。各ページ末尾の「次」を辿れば前提を飛ばしません。
        """, toc: true);

    [Story("Start/GalleryGuide", Order = 2)]
    public static Widget GalleryGuide(StoryContext ctx) => DocNew(ctx, $$"""
        # Gallery の使い方

        1. **Learn** で概念を順番に学ぶ
        2. **Build** でコピー可能な block と recipe を探す
        3. **Examples** で完成形を操作する
        4. **Reference** で API を確認する
        5. **Internals** で設計判断と実装を読む

        Native Gallery の preview、Knobs、Interactions は対話可能です。Static Gallery は画像ですが、同じ source bundle、依存、実行コマンドを表示します。

        Source タブは `[Story]` メソッドだけの場合があります。**Run this sample** または Sample Bundle があるページをコピー元にしてください。
        """, toc: true);
}
