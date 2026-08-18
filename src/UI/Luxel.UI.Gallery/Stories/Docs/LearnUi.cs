using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/UI")]
public static class LearnUi
{
    private static readonly string[] Routes =
    [
        "Learn/UI/Overview", "Learn/UI/Trees", "Learn/UI/LayoutAndMeasurement",
        "Learn/UI/Signals", "Learn/UI/InputAndFocus", "Learn/UI/StylingAndThemes",
        "Learn/UI/Reconciliation", "Learn/UI/CustomControls", "Learn/UI/Diagnostics",
    ];

    private static DocMarkdown Meta(string path, string prerequisites)
    {
        int index = Array.IndexOf(Routes, path);
        string previous = index > 0 ? $"**前へ:** [{Routes[index - 1].Split('/')[^1]}](story:{Routes[index - 1]})" : "";
        string next = index >= 0 && index + 1 < Routes.Length ? $"**次:** [{Routes[index + 1].Split('/')[^1]}](story:{Routes[index + 1]})" : "";
        return new DocMarkdown($"**難易度:** 初級　 **環境:** Native / Browser　 **前提:** {prerequisites}\n\n{previous}{(previous.Length > 0 && next.Length > 0 ? "　 " : "")}{next}");
    }

    [Story]
    public static StoryResult Overview(StoryContext ctx) => $"""
        # UI学習ガイド

        {Toc()}

        {Meta("Learn/UI/Overview", "C#の基本")}

        Luxel UIは、C#で宣言したWidgetツリーをレイアウトし、保持型の2Dノードとして描画します。このコースでは、最初のWidgetからレイアウト、状態更新、入力、テーマ、独自Control、診断までを順番に扱います。

        ## 処理の流れ

        ```text
        Widgetを宣言 → Build → Layout → Realize → 入力と状態更新 → 必要な範囲を再描画
        ```

        `CompositeControl.Build()`は既存Controlを組み合わせる標準的な入口です。完全な独自描画が必要な場合を除き、`Widget`を直接継承せず、生成された`Kit`ファクトリからツリーを返します。

        ## 学習順

        1. [Widgetツリー](story:Learn/UI/Trees)
        2. [レイアウトと計測](story:Learn/UI/LayoutAndMeasurement)
        3. [Signalと状態](story:Learn/UI/Signals)
        4. [入力とフォーカス](story:Learn/UI/InputAndFocus)
        5. [スタイルとテーマ](story:Learn/UI/StylingAndThemes)
        6. [再構築](story:Learn/UI/Reconciliation)
        7. [独自Control](story:Learn/UI/CustomControls)
        8. [診断](story:Learn/UI/Diagnostics)
        """;

    [Story]
    public static StoryResult Trees(StoryContext ctx) => $"""
        # Widgetツリーを作る

        {Toc()}

        {Meta("Learn/UI/Trees", "UI学習ガイド")}

        Widgetは画面の構造を表します。`VStack`や`HStack`で子を並べ、`Text`や`Button`などのControlを配置します。

        ```csharp
        using static Luxel.Controls.Kit;

        Widget content = Card(VStack(12)[
            Heading("Settings", 2),
            Text("Choose how the application behaves."),
            Button(_ => Save(), "Save")
        ]);
        ```

        ## 選び方

        - 縦または横に並べるだけなら`VStack` / `HStack`
        - 行と列で整列させるなら`Grid`
        - 重ね合わせるなら`Overlay`
        - 長い内容を表示するなら`ScrollView`

        Controlの具体例は[Button docs](story:Controls/Input/Button/Docs)で確認できます。

        > [!TIP]
        > まず既存Controlを組み合わせます。入力、フォーカス、テーマ対応をすべて自作する必要がある場合だけ、低水準Widgetを検討してください。
        """;

    [Story]
    public static StoryResult LayoutAndMeasurement(StoryContext ctx) => $"""
        # レイアウトと計測

        {Toc()}

        {Meta("Learn/UI/LayoutAndMeasurement", "Widgetツリー")}

        レイアウトは親から渡された制約の中で子を計測し、最終的なサイズと位置を決定します。固定ピクセルだけでなく、内容に合わせる長さ、割合、余白、最小・最大サイズを組み合わせられます。

        ## まず親の制約を確認する

        子が要求したサイズをそのまま使えるとは限りません。横幅いっぱいに伸ばしたい場合は、子の幅だけでなく親が利用可能な幅を渡しているか確認します。スクロール方向では実質的に制約がなくなるため、割合指定より内容サイズを優先します。

        ## よくある失敗

        | 症状 | 確認すること |
        |---|---|
        | Widgetが見えない | 幅または高さが0になっていないか |
        | 右端が切れる | paddingと子の固定幅を二重に足していないか |
        | ScrollViewが動かない | スクロール方向と直交方向の制約を渡しているか |
        | テキストが不自然に折り返す | 利用可能幅とフォントサイズが確定しているか |

        レイアウト問題は見た目だけで直さず、親から子へ渡る制約と、子が返した計測値を順番に確認します。
        """;

    [Story]
    public static StoryResult Signals(StoryContext ctx) => $$"""
        # Signalと状態更新

        {{Toc()}}

        {{Meta("Learn/UI/Signals", "レイアウトと計測")}}

        `Signal<T>.Value`を読むと現在のreactive scopeへ依存が登録され、値を変更すると購読側が無効化されます。依存として登録せずに値だけを読む場合は`Peek()`を使います。

        ```csharp
        Signal<int> count = ctx.Signal("count", 0, "表示するカウント");

        return VStack(8)[
            Text($"Count: {count}"),
            Button(_ => count.Value++, "+1")
        ];
        ```

        ## 状態の置き場所

        Storyでは`StoryContext`からSignalを作り、Storyの寿命へ閉じ込めます。アプリでは、所有者となるControlやアプリケーションモデルがSignalを保持します。表示だけの一時状態をグローバルサービスへ置くと、画面を閉じても状態が残りやすくなります。

        ## `Peek()`を使う場面

        ログ出力や一度だけ行う初期化など、値の変更で再評価してほしくない読み取りに限定します。表示やレイアウトを決める値へ`Peek()`を使うと、画面が更新されません。
        """;

    [Story]
    public static StoryResult InputAndFocus(StoryContext ctx) => $"""
        # 入力とフォーカス

        {Toc()}

        {Meta("Learn/UI/InputAndFocus", "Signalと状態更新")}

        ポインター入力はヒットテストで対象Widgetを決め、キーボード入力は現在のフォーカス所有者へ送られます。テキスト入力では物理キーと入力文字を分け、IME compositionをUIのテキスト入力経路で扱います。

        ## Controlが担当すること

        - Buttonはクリック、無効状態、キーボード操作をまとめる
        - TextFieldはキャレット、選択、IME、編集状態をまとめる
        - MenuやDialogは開いている間のフォーカス範囲を管理する
        - ScrollViewはホイール入力を自身で消費できる場合だけ処理する

        ## 確認項目

        マウスだけでなく、Tab移動、EnterまたはSpace、Escape、文字選択、IME入力を確認します。読み取り専用表示はテキスト選択を許可しても、編集用ツールバーやスラッシュメニューを出しません。

        複雑な編集UIは[TextEditorView](story:Controls/Editor/TextEditorView/Basic)を実例として参照できます。
        """;

    [Story]
    public static StoryResult StylingAndThemes(StoryContext ctx) => $"""
        # スタイルとテーマ

        {Toc()}

        {Meta("Learn/UI/StylingAndThemes", "入力とフォーカス")}

        色、余白、角丸、フォントサイズなどは、Controlの引数、テーマトークン、スタイル設定から決定します。アプリ全体で意味が共通する値はテーマへ置き、一つの画面だけの調整はControl側へ置きます。

        ## テーマへ置く値

        - 背景、前景、境界線、強調色などの意味色
        - 本文、見出し、補助テキストの基準フォント
        - 標準余白、角丸、Control高さ

        ## テーマ切り替えに追従させる

        構築時の色をフィールドへ固定せず、テーマから得た値を描画更新時に反映します。特にMarkdown見出し、コードブロック、選択色、無効状態は、明暗テーマの両方でコントラストを確認します。

        > [!WARNING]
        > 小さい文字を単に薄い色へすると、アンチエイリアスの影響でかすれて見えます。本文は16pxを基準にし、補助テキストでも十分なコントラストを保ってください。
        """;

    [Story]
    public static StoryResult Reconciliation(StoryContext ctx) => $"""
        # Build、Layout、再構築

        {Toc()}

        {Meta("Learn/UI/Reconciliation", "スタイルとテーマ")}

        Buildは遅延実行され、最初のLayoutで必要になったときに呼ばれます。`CompositeControl`がBuild中に読んだSignalが変わると、次のLayoutで最新値を使って一度だけ再Buildします。

        ## 再構築と増分更新を使い分ける

        子の種類や個数が変わる場合は再Buildが必要です。文字列、色、変換など、既存ノードのプロパティだけが変わる場合は増分更新を優先します。毎フレーム変わる値でツリー全体を再Buildすると、割り当てとレイアウトの負荷が増えます。

        ## 状態を失わないために

        編集内容やスクロール位置などの状態をBuild内のローカル変数として毎回作らないでください。所有Controlまたは外部モデルに保持し、開閉やテーマ変更による再構築後も同じ状態を参照します。
        """;

    [Story]
    public static StoryResult CustomControls(StoryContext ctx) => $$"""
        # 独自Controlを作る

        {{Toc()}}

        {{Meta("Learn/UI/CustomControls", "Buildと再構築")}}

        既存Controlの組み合わせで表現できる場合は`CompositeControl`を使います。公開パラメーターには`[UiParam]`を付け、利用側が生成済み`Kit`ファクトリから作れるようにします。

        ```csharp
        [UiComponent]
        public sealed partial class StatusCard : CompositeControl
        {
            [UiParam] public required string Title { get; init; }
            [UiParam] public required Signal<string> Status { get; init; }

            protected override Widget Build() => Card(VStack(8)[
                Heading(Title, 3),
                Text($"Status: {Status}")
            ]);
        }
        ```

        ControlのStoryでは通常状態、境界値、無効状態、長い文字列、明暗テーマを確認します。製品ControlにGallery専用コードを入れず、Storyとテストは対応するGalleryプロジェクトへ置きます。
        """;

    [Story]
    public static StoryResult Diagnostics(StoryContext ctx) => $"""
        # UIを診断する

        {Toc()}

        {Meta("Learn/UI/Diagnostics", "独自Control")}

        UIの不具合は、状態、Build、Layout、入力、Realizeのどこで期待と違ったかを分けて調べます。

        | 症状 | 最初に確認する層 |
        |---|---|
        | 値が変わらない | Signalの所有者と依存追跡 |
        | 一文字入力するとフォーカスが外れる | 入力のたびにWidgetを置換していないか |
        | 文字が消える | 再BuildでDocumentを作り直していないか |
        | クリックできない | ヒットテスト範囲、重なり、Enabled |
        | 描画だけ古い | Realize対象のdirty範囲とテーマ依存 |

        GalleryではArgsで状態を変え、Outputでイベントを確認し、Sourceで構築コードを読みます。再現条件をStoryへ閉じ込めると、Native版とBlazor版の差も比較しやすくなります。

        次は実際のControlを[Controls](story:Controls/Input/Button/Docs)で確認するか、[Framework](story:Learn/Framework/Overview)でアプリケーションの実行ループへ進んでください。
        """;
}
