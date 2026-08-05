using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0003-Declarative-Signal-Ui", Order = 74, Toc = true)]
    public static StoryResult Adr0003(StoryContext ctx) => $$"""
        # ADR-0003 — UI は「宣言的 C# DSL + signals」を自作する

        - **Status**: Accepted
        - **Date**: 2026-07-08 (記録日 — 決定自体は UI 層の着手時)
        - **Deciders**: ikihiki

        ## Context

        Luxel は自前の GPU 抽象 ([ADR-0002](story:Internals/ADR/0002-Thin-Bindless-Gpu-Abstraction)) と保持型 2D 層 (RetainedCanvas) の上に UI 層を必要としていました。要件と力学:

        - **描画先が自前** — 描画は RetainedCanvas の部分更新に落としたい。既存 UI フレームワークは自分のレンダラ (Skia / Direct2D / 合成ツリー) を前提にしており、そのまま載らない
        - **エンジンとの一体性** — docs・Gallery・ゲーム内 UI・ツール (DevTools) まで全部この UI で書く (ドッグフーディング)。エンジンの Signal/アニメーション/リソース系と反応系を共有したい
        - **更新コストの規律** — ライブ波形の再生でフル再構築 0、タイプ連打で再構築 ~3% という bench 回帰ゲートを敷けること。つまり「何が変わったら何が起きるか」がフレームワークの構造から予測可能であること
        - **C# 一枚岩** — マークアップ言語 (XAML) や別言語 (TS/HTML) の層を挟まず、型・リファクタリング・スクリプティング (Roslyn) が UI 構築コードにそのまま効くこと

        ## Decision

        UI フレームワークを自作します (`Luxel.UI` + `Luxel.Controls`)。核心は次の 4 点:

        - **宣言的 C# DSL** — `Button(...)` / `VStack(...)` のベアファクトリ + 子は get-only インデクサ `[...]` + 見た目は省略可能引数 + 追加宣言は fluent 拡張 (`.When` / `.Transition` / `.GridColumn`)。ファクトリ/拡張はソースジェネレーターが `[UiComponent]`/`[UiParam]` から生成する (reflection なし)
        - **signals による細粒度リアクティブ、仮想 DOM なし** — `Signal<T>` / `Computed<T>` / `Effect` が反応系の全部。`Bind.From(() => sig.Value)` で束縛したノードだけが再評価され、色/位置の変化は保持型キャンバスへのスタイル/変換書き込みだけで済む。ツリー全体の再構築→diff は行わない
        - **単一パスレイアウト** — Flutter 風の `Layout(Constraints, parentUsesSize) → Size` 1 回で、同じ呼び出し内に子 Offset を書く (Measure/Arrange の 2 パスなし)
        - **状態 3 層の規約** — 外部 props は `[UiParam] Bindable<T>`、内部の値状態は `Signal<T>` (細粒度反映)、内部の構造状態は plain フィールド + 明示 `Rebuild()`。値の変化と構造の変化を型で区別する

        現在の姿は [Reference/Guides/UI](story:Reference/Guides/UI) / [Reference/Guides/Controls](story:Reference/Guides/Controls) / [Reference/Guides/Styling](story:Reference/Guides/Styling) へ。

        ## Alternatives

        - **既存 .NET UI フレームワーク (WPF / Avalonia / MAUI)** — レンダラ (DirectX 合成 / Skia) が自前で、RetainedCanvas + bindless GPU 経路に載らない。XAML + reflection ベースのバインディングは「何が変わったら何が起きるか」が構造から読めず、bench 回帰ゲートの規律と合わない → 却下
        - **immediate-mode GUI (Dear ImGui 系)** — 毎フレーム全再構築が前提で「フル再構築 0」の規律と正反対。テキスト編集・IME・リッチドキュメントのような保持状態の重いコントロールに不向き → 却下 (ツール用オーバーレイとしても、ドッグフーディング方針から採らない)
        - **WebView (HTML/CSS/JS)** — 別言語・別プロセスの巨大ランタイムを抱え、エンジンの Signal/アニメーションと反応系を共有できない。ゲーム内 UI のフレーム予算にも合わない → 却下
        - **仮想 DOM / diff 方式の自作 (React 風)** — 宣言の書き味は近いが、更新のたびにツリー構築 + diff のアロケーションと CPU を払う。細粒度 signal なら「束縛ノードだけ再評価」で同じ書き味が得られる (SolidJS と同じ判断) → 却下
        - **2 パス Measure/Arrange レイアウト (WPF 風)** — 汎用性は高いが、パスが増えるほどレイアウトコストと複雑さが増す。Flutter が実証した単一パス + Constraints で必要十分 → 却下

        ## Consequences

        - ✅ 値の変化は束縛ノードの再評価だけ — bench の回帰ゲート (波形再生 = フル再構築 0、タイプ連打 = 再構築 ~3%、仮想化リストのスクロール = 再構築 0) が構造的に成立する
        - ✅ UI 構築が C# のみ — 型チェック・リファクタリング・Roslyn スクリプティング・ソース焼き込み (StorySource) がそのまま効く。docs も Gallery も DevTools もこの UI で書けている (ドッグフーディング)
        - ✅ ソースジェネレーター経由なので reflection ゼロ — ファクトリ/DebugProps/knobs が自動生成され、AOT にも素直
        - ⚠️ **コントロールは全部自作** — Button から RichTextEditor・仮想化 ListView・IME 対応テキスト編集まで 40 超を自前で作り、保守する (サードパーティのコントロール生態系はない)
        - ⚠️ 値状態と構造状態の区別は**規約** — 構造が変わるのに `Rebuild()` を忘れる・状態を保つ子をフィールド保持しないと壊れる。CompositeControl の 3 層規約を守る教育コストがある
        - ⚠️ Effect の文脈規律が要る — Effect 内から他 widget の状態 signal を書くと依存追跡と干渉するため、「フラグを立てて Update (effect 外) で書く」パターンを守る必要がある (GalleryApp が実例)
        - ⚠️ 単一パスレイアウトは「親サイズが子に依存し、子が親サイズに依存する」ような循環要求を表現できない — Constraints で表せる範囲に設計を寄せる
        """;
}
