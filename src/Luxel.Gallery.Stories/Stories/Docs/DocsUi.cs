using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — UI/コントロール章。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsUi
{
    [Story("Reference/Guides/UI", Order = 20)]
    public static Widget Ui(StoryContext ctx) => DocNew(ctx, $$"""
        # 宣言的 UI (Luxel.UI)

        保持型 2D 層 (RetainedCanvas) の上に、**宣言的 C# DSL + signals (細粒度リアクティブ) + 単一パスレイアウト + 入力**を提供します。フレームワークは仮想 DOM を持たず、変わった値だけが保持型キャンバスの部分更新に落ちます。

        ## DSL — ベアファクトリ + indexer

        構築は `Button(...)` / `VStack(...)` などの**ベアファクトリ** (ソースジェネレーターが `[UiComponent]`/`[UiParam]` から生成)、子は get-only インデクサ `[...]`、見た目はすべて省略可能引数。状態レイヤやグリッド配置などの追加宣言は **fluent 拡張** (`.When(...)` / `.Transition(...)` / `.GridColumn(1)`) をチェーンします:

        ```csharp
        var count = new Signal<int>(0);
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))
        [
            Grid(columns: [1, 2])[
                Text($"Count: {count}", 30).GridRow(0).GridColumnSpan(2),
                Button(_ => count.Value--, "- 1").GridColumn(0),
                Button(_ => count.Value++, "+ 1").GridColumn(1)]
        ]
        ```

        > [!NOTE]
        > fluent 拡張はジェネリック this で**具象型を返す**ため、`When` / `Transition` /
        > `GridColumn` を任意の順でチェーンできます。コールバックの第一引数は
        > **発火元コントロール** (sender-first 規約) です。

        ## Signals — 細粒度リアクティブ

        `Signal<T>` / `Computed<T>` / `Reactive.Effect` が反応系の全部です。プロパティを `Bind.From(() => signal.Value)` で束縛すると、signal 変化で**その束縛ノードだけ** 再評価されます。色/位置の変化は保持型キャンバスのスタイル/変換書き込みだけで済み、再レイアウトも再実体化も走りません。

        ## レイアウト — Flutter 風の単一パス

        `Layout(Constraints, parentUsesSize) → Size` を 1 回呼び、同じ呼び出しの中で子の Offset を書きます (Measure/Arrange の 2 パスなし)。Grid は Fixed/Star/Auto、寸法は `Length` (px / % / em / vw) が使えます ([Controls/Layout/Units](story:Controls/Layout/Units) / [Controls/Grid/Columns](story:Controls/Grid/Columns))。

        ## 入力とエラー境界

        `UiHost` がポインタ/キー/IME を前面優先でディスパッチします (Esc → Tab → フォーカス → フォーカス中コントロール → アプリ全域ショートカットの順)。ユーザーコード (Build / Effect / 入力ハンドラ) の例外は**エラー境界**が捕捉し、該当サブツリーを赤枠の ErrorWidget に縮退させます — アプリ全体は落ちません。

        次: [Reference/Guides/Controls](story:Reference/Guides/Controls) (組み込みコントロールと独自コントロール) / [Reference/Guides/Styling](story:Reference/Guides/Styling) (状態別スタイルと Tailwind)。
        """, toc: true);

    [Story("Reference/Guides/Controls", Order = 21)]
    public static Widget Controls(StoryContext ctx) => DocNew(ctx, $$"""
        # コントロール (Luxel.Controls)

        Button から RichTextEditor まで 40 超のコントロール群です。**実物はサイドバーの各章にあります** — このページは地図と、独自コントロールの書き方です。各コントロールの引数/イベント/パラメータは、そのカテゴリ直下の `Overview` (例: [Controls/Button/Overview](story:Controls/Button/Overview)) にあります。

        ## カタログ

        - **入力/選択**: [Button](story:Reference/Guides/Button) / [CheckBox](story:Controls/CheckBox/Basic) / [Switch](story:Controls/Switch/Basic) / [Slider](story:Controls/Slider/Basic) / [Segmented](story:Controls/Segmented/Basic) / [Radios](story:Controls/Radios/Basic) / [Select](story:Controls/Select/Basic) / [LengthField](story:Controls/LengthField/Basic)
        - **テキスト**: [TextField](story:Controls/TextField/Basic) / [TextEditorView](story:Controls/TextEditorView/Basic) / [SearchField](story:Controls/SearchField/Basic) / [Markdown 編集 (Live Preview)](story:Controls/TextEditorView/LivePreview) / [RichText 表示](story:Controls/RichText/Basic)
        - **コンテナ/ナビゲーション**: [Border](story:Controls/Border/Card) / [Grid](story:Controls/Grid/Columns) / [ScrollViewer](story:Controls/ScrollViewer/Basic) / [WrapPanel](story:Controls/WrapPanel/Basic) / [ListView (仮想化)](story:Controls/ListView/Huge) / [NavigationView](story:Controls/NavigationView/Basic) / [Navigation履歴](story:Examples/UI/Navigation)
        - **オーバーレイ**: [Dialog](story:Controls/Dialog/Basic) / [Toast](story:Controls/Toast/Basic) / [Drawer](story:Controls/Drawer/Basic) / [Tooltip](story:Controls/Tooltip/Basic) / [Dropdown](story:Controls/Dropdown/Basic) / [Tabs](story:Controls/Tabs/Basic) / [Accordion](story:Controls/Accordion/Basic)
        - **表示**: [Badge/Chip](story:Controls/Kit/Badges) / [Alert](story:Controls/Kit/Alert) / [Spinner](story:Controls/Spinner/Basic) / [Icon](story:Controls/Icon/Kinds) / [Sparkline](story:Controls/Sparkline/Basic)

        ## 横断基盤

        - **テーマ**: `UiTheme.T` (Light/Dark) — Variant × Intent × 状態から配色を解決。`Ctrl+D` で切替
        - **フォーカス**: Tab 巡回 + FocusRing、キー入力はフォーカス優先で配送
        - **オーバーレイ**: Dialog/Toast/Drawer は overlay レイヤに実体化 (Esc / 外側クリック)
        - **仮想化**: ListView は可視行プールだけを実体化 — 10 万行でもスクロール/選択が破綻しません ([Controls/ListView/Huge](story:Controls/ListView/Huge))

        ## CompositeControl — 独自コントロールを書く

        既存コントロールを**宣言的に組み合わせる**基底です。`Build()` がサブツリーを返し、レイアウト/実体化は委譲 — 手書きの PerformLayout/RealizeCore は書きません:

        ```csharp
        [UiComponent]
        public sealed partial class MyPanel : CompositeControl
        {
            private readonly Signal<string> _query = new("");   // 値状態 (細粒度反映)
            private readonly TextArea _editor;                  // 状態を保つ子はフィールド保持

            protected override Widget Build()                   // 構造だけを宣言
                => VStack(spacing: 4)[
                       HStack(spacing: 6)[Button(_ => Run(), "Run")],
                       _editor];

            protected override void OnRealize(UiBuildContext ctx)
                => ctx.AddAnimation(dt => { Tick(dt); return false; });
        }
        ```

        状態は 3 層の規約で持ちます:

        | 層 | 置き場所 | 更新経路 |
        | --- | --- | --- |
        | 外部 props | `[UiParam] Bindable<T>` フィールド | 呼び出し側/knobs が束縛 — Effect で細粒度反映 |
        | 内部の値状態 | `private Signal<T>` | getter 束縛で子へ配線 (Rebuild 不要) |
        | 内部の構造状態 | private フィールド | 変更したら `Rebuild()` を明示 |

        **状態を保つ子はフィールドに保持して Build() で参照を組み込む**のが鍵です — Rebuild はコンテナだけ作り直し、TextArea 等のインスタンスは生き残ります。実例が SearchField (タイプ → 候補絞り込み = 構造状態 → Rebuild):

        {{StoryRef(ctx, "Controls/SearchField/Basic")}}

        > [!TIP]
        > 完全自前描画が要るときだけ従来どおり `Widget` を直接派生します。`[UiComponent] partial` を付ければ生成ファクトリ / DebugProps / knobs が自動で付きます。

        ## スクロールの共通機構 (ScrollModel / ScrollBars)

        スクロールを自前実装するコントロールは `ScrollModel` (クランプ・サム幾何・ドラッグ写像の計算) と `ScrollBars.AttachVertical` (サム描画 + ドラッグ配線) を使います — ScrollViewer / ListView / RichTextEditor は全部この機構の上にあります。

        - **オフセットだけでなく内容長/ビューポート長も Signal** — `SetLengths(contentH, viewH)` を Refresh/レイアウトから毎回呼ぶ (同値なら発火しない)。幅変更で内容高が変わっても、`model.Clamped` を読む effect (content transform) が再実行され、**スクロール位置は保たれたまま**新しい範囲へクランプされる
        - model を **フィールドで持てば再実体化をまたいで位置が生き残る**
        - 平滑スクロール (UiStates) と併用するときは `displayOffset` に動的値、`onDirectChange` でドラッグの即時チャネルへ切り替える (ScrollViewer/ListView の形)
        """, toc: true);

    [Story("Reference/Guides/Styling", Order = 23)]
    public static Widget Styling(StoryContext ctx) => DocNew(ctx, $$"""
        # スタイリングと Tailwind

        コントロールの見た目は 3 つの層で決まります: **① テーマ既定** (Variant × Intent) → **② ファクトリ引数** (background 等の個別指定) → **③ 状態レイヤ** (fluent `.When(state, ...)` の override)。後の層ほど強く、CSS の specificity と同じ感覚です。

        ## 状態レイヤ (hover / pressed / checked …)

        状態別の見た目は生成された `.When(state, ...)` 拡張で宣言します — 引数はファクトリと同名で、該当状態のスタイルが既定へ**後勝ちマージ**されます:

        ```csharp
        Button(_ => { }, "Hover me",
                background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 180, height: 64)
            .When(WidgetState.Hover, background: Tw.Red500, scale: 1.08f)
            .When(WidgetState.Pressed, scale: 0.94f);
        ```

        {{StoryRef(ctx, "Controls/Button/Tailwind")}}

        ## カラーパレット (Luxel.UI.Tailwind)

        `Tw.Blue500` など Tailwind のカラーパレット定数を別アセンブリで提供します。HTML の class 属性との対応で読めます — `class="bg-blue-500 hover:bg-red-500"` ≒ `background: Tw.Blue500` + `.When(WidgetState.Hover, background: Tw.Red500)`。CheckBox の Checked などコントロール固有状態にも同じ形で効きます ([Controls/CheckBox/CheckedStyle](story:Controls/CheckBox/CheckedStyle))。

        ## ユーザー定義テーマ

        `Luxel.UI` 本体はテーマ型を強制しません — アプリが record で自由に定義し、ファクトリ引数へ流し込みます。組み込みコントロールの既定配色だけは `Luxel.Controls` の `UiTheme` (Light/Dark) が持ちます:

        ```csharp
        // 自分のアプリで自由命名 — Luxel.UI 本体は Theme 型を強制しない
        public sealed record AppTheme
        {
            public required uint Primary { get; init; }
            public required uint Surface { get; init; }
            public required float RoundedMd { get; init; }
        }
        var theme = new AppTheme { Primary = Tw.Blue500, Surface = Tw.Slate50, RoundedMd = 6f };
        Button(_ => { }, "OK", background: theme.Primary, rounded: theme.RoundedMd);
        ```

        ## 状態遷移の補間 (Transition)

        状態切替は瞬時が既定で、`.Transition(duration, curve, プロパティ群)` を宣言したプロパティだけが補間されます。方向別 (`TransitionTo` / `TransitionBetween`) の指定もできます — 実物で確かめてください:

        {{StoryRef(ctx, "Examples/Animation/Transitions")}}

        設計ノート: 状態別スタイルを「引数と同名の型付き宣言」にしたのは Tailwind の hover: / MUI sx / Flutter WidgetState と同じ発想です。テーマを経由しない一発指定と、テーマ経由の既定解決が同居し、どちらもトランジションに乗ります。
        """, toc: true);

    [Story("Reference/Guides/Button", Order = 22)]
    public static Widget ButtonDocs(StoryContext ctx) => DocNew(ctx, $$"""
        # Button

        ボタンは **Variant × Intent × 状態** から配色を解決します。未指定のプロパティはテーマ値へフォールバックし、hover/press はトランジション (状態機械) で補間されます。

        ## Variant (形)

        > [!TIP]
        > 下の実例のすぐ下に `StorySource` で**実際のストーリーソース**を出しています (ジェネレーターが焼き込むため、手書きコピーの乖離が起きません)。

        {{StoryRef(ctx, "Controls/Button/Variants")}}

        {{StorySource("Controls/Button/Variants")}}

        ## Intent (意味色)

        {{StoryRef(ctx, "Controls/Button/Intents")}}

        ## 使い方

        ```csharp
        Button(_ => ctx.Log("clicked"), "OK", variant: Variant.Tonal, intent: Intent.Success)
        ```

        コールバックの第一引数は**発火元の Button 自身** (sender-first 規約) です。入門は [GettingStarted](story:Reference/Guides/GettingStarted)、状態別スタイルと Tailwind は [Reference/Guides/Styling](story:Reference/Guides/Styling) へ。

        ## API

        {{DocsApi.ControlApiReference("Button")}}
        """, toc: true);
}
