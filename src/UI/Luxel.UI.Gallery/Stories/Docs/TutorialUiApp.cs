using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>レイアウト、状態、イベント、overlayを持つUIアプリを組み立てる。</summary>
[StoryMeta("Tutorials/UIApp")]
public static partial class TutorialUiApp
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # UIアプリを作る

        {{Toc()}}

        このコースでは、固定座標を使わずに画面を構成し、`Signal`へ状態を集め、イベントで更新し、最後にdialogを加えます。完成するのは、テーマとサイズ変更に追従し、状態の流れを追える小さなタスク画面です。

        ## 学習順

        1. [レイアウトを組む](story:Tutorials/UIApp/ComposeLayout)
        2. [状態とイベントを接続する](story:Tutorials/UIApp/StateAndEvents)
        3. [オーバーレイを加えて完成](story:Tutorials/UIApp/Finish)

        ## 基本方針

        UIの構造はWidget tree、変化する値は`Signal<T>`、操作はevent handlerに分けます。Widgetを状態の保存場所にせず、再構築しても同じSignalへ接続できる形にします。
        """;

    [Story]
    public static StoryResult ComposeLayout(StoryContext ctx) => $$"""
        # レイアウトを組む

        {{Toc()}}

        画面をheader、content、actionsに分け、`VStack`と`HStack`で配置します。余白は親containerに、要素間隔はstackに持たせると、個々のcontrolへmarginが散らばりません。

        {{StoryRef("Tutorials/UIApp/LayoutSample")}}

        ## サイズ変更へ備える

        - 固定幅はボタンやiconなど意味のある部分だけに使う
        - 主領域はstretchさせ、長い本文はscrollへ入れる
        - 色は直接固定せず`UiTheme.T`から取得する
        - card、section、rowの境界をWidget treeでも一致させる

        次は[状態とイベントを接続する](story:Tutorials/UIApp/StateAndEvents)へ進みます。
        """;

    [Story]
    public static StoryResult StateAndEvents(StoryContext ctx) => $$"""
        # 状態とイベントを接続する

        {{Toc()}}

        画面で変わる値を`Signal`として宣言し、表示と入力の両方を同じSignalへ接続します。イベントはSignalを更新し、必要ならOutputへ意味のある操作を記録します。

        {{StoryRef("Tutorials/UIApp/TaskCounterSample", knobs: true)}}

        ## 状態を置く場所

        Storyでは`ctx.Signal`を使うとArgsから同じ値を変更できます。実アプリでは画面より長く生きる状態をview modelやdomainへ置き、control固有の開閉状態だけを画面に残します。

        次は[オーバーレイを加えて完成](story:Tutorials/UIApp/Finish)へ進みます。
        """;

    [Story]
    public static StoryResult Finish(StoryContext ctx) => $$"""
        # オーバーレイを加えて完成する

        {{Toc()}}

        dialog、menu、tooltipは通常レイアウトの子に見えても、描画とhit testはoverlay layerで行います。開閉は`Signal<bool>`で管理し、Esc、外側クリック、操作完了のすべてを同じclose処理へ集めます。

        {{StoryRef("Tutorials/UIApp/DialogSample", knobs: true)}}

        ## 完成チェック

        - 主要な状態がWidgetの外から確認できる
        - Tab移動とEnter/Escで操作できる
        - light/dark themeの両方で文字と背景に十分な差がある
        - resizeしても主操作が画面外へ消えない
        - dialogを閉じる経路が一つの状態へ集約されている

        controlの選択肢とstyleを詳しく調べる場合は[UI Learn](story:Learn/UI/Overview)、個別controlは`Controls`へ進んでください。
        """;
}
