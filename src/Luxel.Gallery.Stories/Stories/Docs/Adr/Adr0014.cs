using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0014-Workbench-Ui-Controls", Order = 85, Toc = true)]
    public static StoryResult Adr0014(StoryContext ctx) => $$"""
        # ADR-0014 — Workbench 基盤 UI コントロール群を新設する

        - **Status**: Accepted
        - **Date**: 2026-07-09
        - **Deciders**: ikihiki

        ## Context

        > [!NOTE]
        > `ToDo/22`〜`ToDo/27` はADR作成当時の計画番号で、現在のファイル参照ではありません。現行の実装と利用手順は本文からリンクする `Reference/Guides/*` とLearnページを正とします。


        Workbench ([ADR-0010](story:Internals/ADR/0010-Workbench-Framework)) のシェルに必要な**内容/レイアウト系コントロール**が未実装です。調査の結果、次が「無い」と判明しました: 複数ドキュメントの `DocumentTabs`、ドッキングを描く `DockHost`、型/ECS を反映する `PropertyGrid`/Inspector、`StatusBar`、VFS 連動の `AssetBrowser`。

        既存に**種**はあります — `Tabs` (ビュー切替専用)・`Splitter`・`KnobsTable`/`TypeApiTable`・`TreeView` — が、複数ドキュメント/ドッキング/インスペクションの用途にはそのまま使えません。これらを WS-D (具体エディタ作成) に埋め込まず、**再利用可能なコントロールとして独立に切り出す**判断です (Inspector は将来のシーン/レベルエディタの前提でもあるため)。

        ## Decision

        `Luxel.Controls` に基盤コントロールを新設します:

        - **`DocumentTabs`** — 複数ドキュメントのタブ帯 { ダーティ点, × 閉じ, オーバーフロー, D&D 並べ替え }。既存 `Tabs` を汎用化。D&D は [ADR-0011](story:Internals/ADR/0011-Pointer-Button-Modifiers) が前提
        - **`DockHost`** — `DockTree` ([ADR-0010](story:Internals/ADR/0010-Workbench-Framework)) を描く container。ペイン分割 (`Splitter`)・ドラッグの**ドロップゾーン**・窓内フローティング (Overlay 層、[ADR-0007](story:Internals/ADR/0007-Floating-Ui-Placement))
        - **`PropertyGrid` / Inspector** — target (型 / ECS コンポーネント / config オブジェクト) を反映し、型別エディタ (Slider/TextField/ColorPicker/Switch/Select/ベクトル) を行に並べる。グループ化 + 束縛。`KnobsTable`/`TypeApiTable` を土台に
        - **`StatusBar`** — 下部ステータス帯 (セグメント + ライブ `Signal`)
        - **`AssetBrowser` / FileTree** — `TreeView` を VFS ([ADR-0012](story:Internals/ADR/0012-Rich-Document-Stack) の Write を含む `IDocumentStore`) に結線 (+サムネイルグリッド)

        **コマンド駆動サーフェス** (MenuBar / コマンドパレット / ツールバー) は [ADR-0013](story:Internals/ADR/0013-Menu-Command-System) 側に置き、この ADR は内容/レイアウト系に集中します。ヒエラルキー/アウトライナは `TreeView` 流用で新規不要です。

        実装計画は ToDo/26。

        ## Alternatives

        - **既存 `Tabs`/`Splitter` をそのまま流用** — ダーティ表示・D&D・ドッキング・ドロップゾーンが無く、複数ドキュメントに耐えない → 却下 (汎用化/新設)
        - **Inspector を各エディタが個別実装** — 重複し、シーンエディタで再発明になる → 却下 (共有コントロール)
        - **これらを WS-D に内包** — 再利用されず、将来のシーン/レベルエディタへの布石にならない → 却下 (独立 ADR)

        ## Consequences

        - ✅ 複数ドキュメント・ドッキング・インスペクションが**再利用可能なコントロール**として揃う
        - ✅ `PropertyGrid` が将来の 2D/3D シーンエディタ (ビューポート系) の土台になる
        - ✅ 既存の種 (`Tabs`/`Splitter`/`KnobsTable`/`TreeView`) を活かせて新規コードを絞れる
        - ⚠️ `DockHost` の D&D/ドロップゾーンは実装が重く、[ADR-0011](story:Internals/ADR/0011-Pointer-Button-Modifiers) と**端レイアウトの golden 検証**が要る
        - ⚠️ `PropertyGrid` の型別エディタは、対応型を増やすたびに保守が要る
        """;
}
