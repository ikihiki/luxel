using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — UI/コントロール章。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsUi
{
    [Story("Docs/UI", Width = 800, Height = 480, Order = 20)]
    public static Widget Ui(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # 宣言的 UI (Luxel.UI)

        保持型 2D 層 (RetainedCanvas) の上に、**宣言的 C# DSL + signals (細粒度リアクティブ) +
        単一パスレイアウト + 入力**を提供します。フレームワークは仮想 DOM を持たず、
        変わった値だけが保持型キャンバスの部分更新に落ちます。

        ## DSL — ベアファクトリ + indexer

        構築は `Button(...)` / `VStack(...)` などの**ベアファクトリ** (ソースジェネレーターが
        `[UiComponent]`/`[UiParam]` から生成)、子は get-only インデクサ `[...]`、見た目は
        すべて省略可能引数、添付プロパティだけ `P.Grid.Column(1)` を `parts:` に渡します:

        ```csharp
        var count = new Signal<int>(0);
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))
        [
            Grid(columns: [1, 2])[
                Text($"Count: {count}", 30, parts: [P.Grid.Row(0), P.Grid.ColumnSpan(2)]),
                Button(_ => count.Value--, "- 1", parts: P.Grid.Column(0)),
                Button(_ => count.Value++, "+ 1", parts: P.Grid.Column(1))]
        ]
        ```

        > [!NOTE]
        > `Foo(...)` 呼び出しと `Foo.Bar` メンバアクセスを同名で両立できない C# の制約のため、
        > 構築はベア関数、添付は `P.Grid.*` に分離しています。コールバックの第一引数は
        > **発火元コントロール** (sender-first 規約) です。

        ## Signals — 細粒度リアクティブ

        `Signal<T>` / `Computed<T>` / `Reactive.Effect` が反応系の全部です。プロパティを
        `Bind.From(() => signal.Value)` で束縛すると、signal 変化で**その束縛ノードだけ**
        再評価されます。色/位置の変化は保持型キャンバスのスタイル/変換書き込みだけで済み、
        再レイアウトも再実体化も走りません。

        ## レイアウト — Flutter 風の単一パス

        `Layout(Constraints, parentUsesSize) → Size` を 1 回呼び、同じ呼び出しの中で子の
        Offset を書きます (Measure/Arrange の 2 パスなし)。Grid は Fixed/Star/Auto、
        寸法は `Length` (px / % / em / vw) が使えます
        ([Layout/Units](story:Layout/Units) / [Grid/Columns](story:Grid/Columns))。

        ## 入力とエラー境界

        `UiHost` がポインタ/キー/IME を前面優先でディスパッチします (Esc → Tab → フォーカス
        → フォーカス中コントロール → アプリ全域ショートカットの順)。ユーザーコード
        (Build / Effect / 入力ハンドラ) の例外は**エラー境界**が捕捉し、該当サブツリーを
        赤枠の ErrorWidget に縮退させます — アプリ全体は落ちません。

        次: [Docs/Controls](story:Docs/Controls) (組み込みコントロールと独自コントロール) /
        [Docs/Styling](story:Docs/Styling) (状態別スタイルと Tailwind)。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Controls", Width = 800, Height = 480, Order = 21)]
    public static Widget Controls(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # コントロール (Luxel.Controls)

        Button から RichTextEditor まで 40 超のコントロール群です。**実物はサイドバーの
        各章にあります** — このページは地図と、独自コントロールの書き方です。

        ## カタログ

        - **入力/選択**: [Button](story:Docs/Button) / [CheckBox](story:CheckBox/Basic) /
          [Switch](story:Switch/Basic) / [Slider](story:Slider/Basic) /
          [Segmented](story:Segmented/Basic) / [Radios](story:Radios/Basic) /
          [Select](story:Select/Basic) / [LengthField](story:LengthField/Basic)
        - **テキスト**: [TextField](story:TextField/Basic) / [TextArea](story:TextArea/Basic) /
          [SearchField](story:SearchField/Basic) / [RichTextEditor](story:RichTextEditor/Basic) /
          [MarkdownEditor (hybrid)](story:MarkdownEditor/Hybrid) / [RichText 表示](story:RichText/Basic)
        - **コンテナ**: [Border](story:Border/Card) / [Grid](story:Grid/Columns) /
          [ScrollViewer](story:ScrollViewer/Basic) / [WrapPanel](story:WrapPanel/Basic) /
          [ListView (仮想化)](story:ListView/Huge)
        - **オーバーレイ**: [Dialog](story:Dialog/Basic) / [Toast](story:Toast/Basic) /
          [Drawer](story:Drawer/Basic) / [Tooltip](story:Tooltip/Basic) /
          [Dropdown](story:Dropdown/Basic) / [Tabs](story:Tabs/Basic) / [Accordion](story:Accordion/Basic)
        - **表示**: [Badge/Chip](story:Kit/Badges) / [Alert](story:Kit/Alert) /
          [Spinner](story:Spinner/Basic) / [Icon](story:Icon/Kinds) / [Sparkline](story:Sparkline/Basic)

        ## 横断基盤

        - **テーマ**: `UiTheme.T` (Light/Dark) — Variant × Intent × 状態から配色を解決。
          `Ctrl+D` で切替
        - **フォーカス**: Tab 巡回 + FocusRing、キー入力はフォーカス優先で配送
        - **オーバーレイ**: Dialog/Toast/Drawer は overlay レイヤに実体化 (Esc / 外側クリック)
        - **仮想化**: ListView は可視行プールだけを実体化 — 10 万行でもスクロール/選択が
          破綻しません ([ListView/Huge](story:ListView/Huge))

        ## CompositeControl — 独自コントロールを書く

        既存コントロールを**宣言的に組み合わせる**基底です。`Build()` がサブツリーを返し、
        レイアウト/実体化は委譲 — 手書きの PerformLayout/RealizeCore は書きません:

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

        **状態を保つ子はフィールドに保持して Build() で参照を組み込む**のが鍵です —
        Rebuild はコンテナだけ作り直し、TextArea 等のインスタンスは生き残ります。
        実例が SearchField (タイプ → 候補絞り込み = 構造状態 → Rebuild):

        {{StoryRef(ctx, "SearchField/Basic")}}

        > [!TIP]
        > 完全自前描画が要るときだけ従来どおり `Widget` を直接派生します。
        > `[UiComponent] partial` を付ければ生成ファクトリ / DebugProps / knobs が自動で付きます。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Styling", Width = 800, Height = 480, Order = 23)]
    public static Widget Styling(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # スタイリングと Tailwind

        コントロールの見た目は 3 つの層で決まります: **① テーマ既定** (Variant × Intent) →
        **② ファクトリ引数** (background 等の個別指定) → **③ Tailwind utility**
        (`parts:` に渡す override)。後の層ほど強く、CSS の specificity と同じ感覚です。

        ## 状態レイヤ (hover / pressed / checked …)

        状態別の見た目は `S.On(WidgetState.Hover, ...)` で utility を状態に紐づけるか、
        生成された `.When(state, ...)` 拡張で引数と同名のプロパティを上書きします。
        該当状態のスタイルが既定へ**後勝ちマージ**されます:

        ```csharp
        Button(_ => { }, "Hover me",
            background: Tw.Blue500, foreground: Tw.White, rounded: 10, width: 180, height: 64,
            parts: [S.On(WidgetState.Hover, S.Bg(Tw.Red500), S.Scale(1.08f)),
                    S.On(WidgetState.Pressed, S.Scale(0.94f))]);
        ```

        {{StoryRef(ctx, "Button/Tailwind")}}

        ## Tailwind utility (Luxel.UI.Tailwind)

        `Tw.Blue500` などのパレット定数と `S.Bg / S.Fg / S.Rounded / S.Scale / S.On(state, ...)`
        の utility を別アセンブリで提供します。HTML の class 属性との対応で読めます —
        `class="bg-blue-500 hover:bg-red-500"` ≒ `parts: [S.Bg(Tw.Blue500),
        S.On(WidgetState.Hover, S.Bg(Tw.Red500))]`。CheckBox の Checked など
        コントロール固有状態にも同じ形で効きます ([CheckBox/CheckedStyle](story:CheckBox/CheckedStyle))。

        ## ユーザー定義テーマ

        `Luxel.UI` 本体はテーマ型を強制しません — アプリが record で自由に定義し、
        ファクトリ引数へ流し込みます。組み込みコントロールの既定配色だけは
        `Luxel.Controls` の `UiTheme` (Light/Dark) が持ちます:

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

        状態切替は瞬時が既定で、`.Transition(duration, curve, プロパティ群)` を宣言した
        プロパティだけが補間されます。方向別 (`TransitionTo` / `TransitionBetween`) の
        指定もできます — 実物で確かめてください:

        {{StoryRef(ctx, "Transitions/States")}}

        設計ノート: 状態別スタイルを「引数で全部渡せる」形にしたのは Tailwind / MUI sx /
        Flutter WidgetState と同じ発想です。テーマを経由しない一発指定と、テーマ経由の
        既定解決が同居し、どちらもトランジションに乗ります。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Button", Width = 800, Height = 480, Order = 22)]
    public static Widget ButtonDocs(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # Button

        ボタンは **Variant × Intent × 状態** から配色を解決します。未指定のプロパティは
        テーマ値へフォールバックし、hover/press はトランジション (状態機械) で補間されます。

        ## Variant (形)

        > [!TIP]
        > 下の実例のすぐ下に `StorySource` で**実際のストーリーソース**を出しています
        > (ジェネレーターが焼き込むため、手書きコピーの乖離が起きません)。

        {{StoryRef(ctx, "Button/Variants")}}

        {{StorySource("Button/Variants")}}

        ## Intent (意味色)

        {{StoryRef(ctx, "Button/Intents")}}

        ## 使い方

        ```csharp
        Button(_ => ctx.Log("clicked"), "OK", variant: Variant.Tonal, intent: Intent.Success)
        ```

        コールバックの第一引数は**発火元の Button 自身** (sender-first 規約) です。
        入門は [GettingStarted](story:Docs/GettingStarted)、状態別スタイルと Tailwind は
        [Docs/Styling](story:Docs/Styling) へ。

        ## API

        {{ApiTable("Button")}}
        """, toc: true, fences: DocsFences));
}
