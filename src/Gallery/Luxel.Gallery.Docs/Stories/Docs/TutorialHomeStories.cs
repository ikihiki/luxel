using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>成果物から選ぶTutorialsの入口。</summary>
[StoryMeta("Tutorials")]
public static class TutorialHomeStories
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Tutorials

        {{Toc()}}

        Tutorialsは、機能を個別に調べるリファレンスではなく、完成する成果物を順番に作るコースです。作りたいものに最も近いコースを一つ選び、ページ末尾の「次」を辿ってください。

        ## コース

        | コース | 作るもの | 主に使う機能 |
        |---|---|---|
        | [3Dアプリ](story:Tutorials/3DApp/Overview) | GPUで描画する3Dシーン | `GpuView`、pipeline、depth、scene pass |
        | [2Dアプリ](story:Tutorials/2DApp/Overview) | カメラと画像を持つ2Dシーン | `Canvas2D`、`Scene2D`、path、sprite、更新 |
        | [UIアプリ](story:Tutorials/UIApp/Overview) | 状態を操作できる画面 | layout、Signal、event、overlay |
        | [Gallery](story:Tutorials/Gallery/Overview) | 説明と実行例を持つGalleryライブラリ | `StoryResult`、Args、Output、Markdown |

        ## TutorialsとLearnの違い

        Tutorialsは最短の一本の道を示します。選択肢や内部構造まで調べたい場合は、完成後に各ページから`Learn`へ進んでください。途中でAPIの型や既定値を確認するときは`Reference`を使います。

        ## 共通の進め方

        1. ページの完成像を確認する
        2. 埋め込まれたプレビューを操作する
        3. Sourceで最小構成を読む
        4. 自分のプロジェクトへ同じ境界で移す
        5. 各コースの完成チェックを通す
        """;
}
