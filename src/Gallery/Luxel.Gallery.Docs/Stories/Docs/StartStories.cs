using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Start")]
public static class StartStories
{
    [Story]
    public static StoryResult Welcome(StoryContext ctx) => $$"""
        # Luxel Gallery — 見て、コピーして、アプリを作る

        {{Toc()}}

        Gallery は動く教科書です。説明を読んだら preview を操作し、**検証済み Sample Bundle** の実ファイルをコピーしてください。

        ## 学習ルート

        - **GPU が初めて** — [Graphics](story:Learn/Graphics/Overview) で window、device、surface、三角形まで進む
        - **3D アプリを作る** — Indexed Cube と 3D Camera を組み合わせる
        - **2D を描画する** — [2D](story:Learn/Graphics/First2DScene) で path、色、画像、camera transform を使う
        - **複数passを構成する** — [RenderGraph](story:Learn/Graphics/RenderGraph/Overview) でresource、依存、culling、aliasingを順に学ぶ
        - **Input / Audio / Resources** — [Input](story:Learn/Input/Overview)、[Audio](story:Learn/Audio/Overview)、[Resources](story:Learn/Resources/Overview)でapp runtimeを組む
        - **値・clip・effectを動かす** — [Animation](story:Learn/Animation/Overview)から始め、短命なvisual effectは[Particles](story:Learn/Animation/Particles/Overview)へ進む
        - **2D rasterizer の中を読む** — [Internal](story:Learn/Graphics/2D/Internal/Overview) で encode、bounds、bin、fine pass を追う

        ## コードの保証レベル

        - **Snippet**: 既存アプリへ貼る短い断片
        - **Block**: App Host の接続点へ追加できる機能単位
        - **Recipe**: 複数 block を組み合わせて build 検証した構成
        - **StandaloneProject**: `.csproj`、C#、shader、asset を含む実行可能プロジェクト
        - **GalleryOnly**: `StoryContext` など Gallery harness が必要。Source タブだけでは standalone にならない

        次に [自分のルートを選ぶ](story:Start/ChooseYourPath) か、[Gallery の使い方](story:Start/GalleryGuide) を開いてください。
        """;

    [Story]
    public static StoryResult ChooseYourPath(StoryContext ctx) => $$"""
        # 自分のルートを選ぶ

        {{Toc()}}

        | 目的 | 開始ページ | 到達物 |
        |---|---|---|
        | 最初の GPU アプリ | [Graphics](story:Learn/Graphics/Overview) | standalone triangle |
        | 実用 3D | Indexed Cube | indexed mesh + perspective camera |
        | 2D canvas | [2D](story:Learn/Graphics/First2DScene) | 2D content を構築して描画できる |
        | 複数pass GPU描画 | [RenderGraph](story:Learn/Graphics/RenderGraph/Overview) | transient resource、自動barrier、culling、aliasing |
        | 実装読解 | [Internal](story:Learn/Graphics/2D/Internal/Overview) | C# から compute pass まで説明できる |

        初心者は Graphics 直下のページを順番に進めてください。各ページ末尾の「次」を辿れば前提を飛ばしません。
        """;

    [Story]
    public static StoryResult GalleryGuide(StoryContext ctx) => $$"""
        # Gallery の使い方

        {{Toc()}}

        1. **Learn** で概念を順番に学ぶ
        2. **Build** でコピー可能な block と recipe を探す
        3. **Examples** で完成形を操作する
        4. **Reference** で API を確認する
        5. **Internals** で設計判断と実装を読む

        Native Gallery の preview、Knobs、Interactions は対話可能です。Static Gallery は画像ですが、同じ source bundle、依存、実行コマンドを表示します。

        Source タブは `[Story]` メソッドだけの場合があります。**Run this sample** または Sample Bundle があるページをコピー元にしてください。
        """;
}
