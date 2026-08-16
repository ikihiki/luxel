using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Start")]
public static class StartStories
{
    [Story]
    public static StoryResult Welcome(StoryContext ctx) => $$"""
        # Luxel Galleryへようこそ

        {{Toc()}}

        Luxel Galleryは、説明、実行できるサンプル、APIリファレンスを一つにまとめた開発者向けカタログです。まずプレビューを操作し、下部のSourceで`[Story]`メソッドを確認してください。

        初めてLuxelを触る場合は、[Tutorials](story:Tutorials/Overview)から作りたいアプリを選びます。GalleryへStoryを追加する場合は[最初のStory](story:Tutorials/Gallery/FirstStory)から始めます。

        ## 学習ルート

        - **アプリを一つ作る** — [Tutorials](story:Tutorials/Overview)から3D、2D、UI、Galleryのコースを選ぶ
        - **GPU描画を始める** — [Graphics](story:Learn/Graphics/Overview)でウィンドウ、デバイス、サーフェス、三角形まで進む
        - **2Dを描画する** — [2D](story:Learn/Graphics/First2DScene)でパス、色、画像、カメラ変換を使う
        - **入力や音声を加える** — [Input](story:Learn/Input/Overview)と[Audio](story:Learn/Audio/Overview)で実行時システムを組み立てる
        - **アセットを管理する** — [Resources](story:Learn/Resources/Overview)で読み込み、依存関係、再読み込み、寿命を学ぶ
        - **値やエフェクトを動かす** — [Animation](story:Learn/Animation/Overview)から始め、短時間の視覚効果は[Particles](story:Learn/Animation/Particles/Overview)へ進む
        - **実装を理解する** — [Internals](story:Internals/Architecture)と[2D rasterizer](story:Learn/Graphics/2D/Internal/Overview)で内部構造を追う

        ## ページの種類

        - **Tutorials** — 一つの成果物を順番に作る
        - **Learn** — 機能の概念と使い方を体系的に学ぶ
        - **Controls / Examples** — 実際に動く部品や完成例を操作する
        - **Reference** — APIのシグネチャ、既定値、型を調べる
        - **Internals** — アーキテクチャ、実装詳細、ADRを読む

        次に[自分のルートを選ぶ](story:Start/ChooseYourPath)か、[Tutorials](story:Tutorials/Overview)を開いてください。
        """;

    [Story]
    public static StoryResult ChooseYourPath(StoryContext ctx) => $$"""
        # 自分のルートを選ぶ

        {{Toc()}}

        | 目的 | 開始ページ | 到達物 |
        |---|---|---|
        | Galleryへサンプルを追加する | [最初のStory](story:Tutorials/Gallery/FirstStory) | Sourceで読めるStory |
        | Galleryライブラリを追加する | [Galleryライブラリ](story:Tutorials/Gallery/GalleryLibrary) | ホストへ登録された独立カテゴリ |
        | 最初のGPUアプリ | [Graphics](story:Learn/Graphics/Overview) | 三角形を描画するアプリ |
        | 2Dキャンバス | [2D](story:Learn/Graphics/First2DScene) | 2Dコンテンツを構築して描画できる |
        | 複数パスGPU描画 | [RenderGraph](story:Learn/Graphics/RenderGraph/Overview) | 一時リソース、自動バリア、カリング、エイリアシング |
        | 実装読解 | [2D Internal](story:Learn/Graphics/2D/Internal/Overview) | C#からcompute passまで説明できる |

        Luxelが初めてならTutorialsを先に進めてください。機能を探している場合はLearn、実装を調査する場合はInternalsへ直接進めます。
        """;

    [Story]
    public static StoryResult GalleryGuide(StoryContext ctx) => $$"""
        # Gallery の使い方

        {{Toc()}}

        1. **Tutorials**で一つの成果物を順番に作る
        2. **Learn**で必要な概念を学ぶ
        3. **Controls / Examples**で動作と使い方を確認する
        4. **Reference**でAPIを確認する
        5. **Internals**で設計判断と実装を読む

        Native版とBlazor版では同じStoryを操作できます。プレビューの下にArgs、Output、Sourceが表示され、Storyが公開する引数、実行ログ、C#ソースを確認できます。

        Sourceには`[Story]`属性、メソッド宣言、本体が表示されます。Story固有のコードを読む入口として使い、依存するヘルパーや型はリポジトリ内の同じGalleryライブラリから辿ってください。

        > [!TIP]
        > Gallery自体の追加方法は[Galleryの作り方](story:Tutorials/Gallery/Overview)、Markdownの詳細な執筆規約は[Authoring reference](story:Internals/Authoring)にあります。
        """;
}
