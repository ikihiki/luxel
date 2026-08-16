using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>保持型2Dシーンを段階的に組み立てる。</summary>
[StoryMeta("Tutorials/2DApp")]
public static partial class Tutorial2DApp
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # 2Dアプリを作る

        {{Toc()}}

        このコースでは、`Canvas2D`へ図形を置き、camera transformとspriteを加え、最後に保持したsceneを差分更新します。毎フレームすべての描画命令を組み直さず、変わった要素だけを更新する2Dアプリが完成します。

        ## 学習順

        1. [図形でシーンを作る](story:Tutorials/2DApp/DrawScene)
        2. [カメラと画像を加える](story:Tutorials/2DApp/CameraAndSprites)
        3. [差分更新して完成](story:Tutorials/2DApp/Finish)

        ## 使うモデル

        `Canvas2D`はUI内に描画面を置き、`Scene2D`はpath、stroke、image、transformと描画順を保持します。入力やゲーム状態はsceneの外に置き、その結果だけをscene nodeへ反映します。
        """;

    [Story]
    public static StoryResult DrawScene(StoryContext ctx) => $$"""
        # 図形でシーンを作る

        {{Toc()}}

        最初に背景、矩形、円、pathを追加します。座標系は左上が原点で、同じlayerでは後から追加した要素が手前になります。色と寸法を先に定数へまとめると、後からcameraを加えてもsceneの意味を保てます。

        {{StoryRef("Tutorials/2DApp/ShapesSample")}}

        ## 最小の責務分離

        - app stateは位置、速度、選択中IDなど意味のある値を持つ
        - sceneはapp stateを画面上の形へ投影する
        - pointer入力はhit結果をapp commandへ変換する

        次は[カメラと画像を加える](story:Tutorials/2DApp/CameraAndSprites)へ進みます。
        """;

    [Story]
    public static StoryResult CameraAndSprites(StoryContext ctx) => $$"""
        # カメラと画像を加える

        {{Toc()}}

        world座標をcamera transformでscreen座標へ変換します。pan、zoom、回転をscene全体へ適用し、HUDだけはcameraの外側へ置きます。

        {{StoryRef("Tutorials/2DApp/CameraSample", knobs: true)}}

        spriteはtextureとsource rectangleを共有し、位置や大きさなどinstanceごとの差だけを保持します。大量の画像を個別textureにせず、atlasとしてまとめるのが基本です。

        {{StoryRef("Tutorials/2DApp/SpritesSample")}}

        次は[差分更新して完成](story:Tutorials/2DApp/Finish)へ進みます。
        """;

    [Story]
    public static StoryResult Finish(StoryContext ctx) => $$"""
        # 差分更新して完成する

        {{Toc()}}

        scene nodeの安定したhandleを保持し、移動したobjectのtransformだけを更新します。毎フレームscene全体を破棄すると、encode、allocation、GPU uploadのすべてが増えるため避けます。

        {{StoryRef("Tutorials/2DApp/RetainedUpdatesSample", knobs: true)}}

        ## 完成チェック

        - worldとscreenの座標変換が一か所にまとまっている
        - cameraの影響を受けないHUDを別layerに置いている
        - sprite atlasとimage resourceを再利用している
        - 更新対象を安定したhandleで特定している
        - resize後もviewportと入力座標が一致する

        path、合成、rasterizerを詳しく学ぶ場合は[2D Learn](story:Learn/Graphics/First2DScene)へ進んでください。
        """;
}
