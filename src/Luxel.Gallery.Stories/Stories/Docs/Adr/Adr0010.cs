using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0010-Workbench-Framework", Order = 81, Toc = true)]
    public static StoryResult Adr0010(StoryContext ctx) => $$"""
        # ADR-0010 — 複数エディタを束ねる Workbench フレームワークを新規に作る

        - **Status**: Accepted
        - **Date**: 2026-07-09
        - **Deciders**: ikihiki

        ## Context

        > [!NOTE]
        > `ToDo/22`〜`ToDo/27` はADR作成当時の計画番号で、現在のファイル参照ではありません。現行の実装と利用手順は本文からリンクする `Reference/Guides/*` とLearnページを正とします。


        Luxel には成熟した**編集コンポーネント**が育ちました — テキスト新スタック ([ADR-0006](story:Internals/ADR/0006-Editor-New-Stack)、`Luxel.Document` + `TextEditorView`) とノード新スタック ([ADR-0009](story:Internals/ADR/0009-Node-Editor-Stack)、`Luxel.NodeGraph` + `NodeGraphView`)。さらにリッチ文書スタック ([ADR-0012](story:Internals/ADR/0012-Rich-Document-Stack)) を新設予定です。

        しかしこれらを「**開いて・並べて・保存する**」アプリ/シェル層がありません。唯一のシェルは Gallery (Storybook 風・単一ドキュメントのプレビュー) で、複数ドキュメントのタブ・ドッキング・ファイル連動・コマンド起動・保存経路が全面的に未実装です。一方で各エディタスタックは**内部モデルを意図的に分離**しています ([ADR-0006](story:Internals/ADR/0006-Editor-New-Stack) / [ADR-0009](story:Internals/ADR/0009-Node-Editor-Stack))。シェルはそこに手を出さず、各エディタを不透明に束ねたい、という力学です。

        ## Decision

        canvas 非依存の新プロジェクト **`Luxel.Workbench`** を作り、複数エディタを 1 シェルに束ねます。エディタは特定型をハードコードせず「**構成**」として載せます。核心:

        - **`IEditorDocument`** — 1 ドキュメントの**不透明ハンドル** { Kind, Title, `Signal<bool>` Dirty, `Widget CreateView()`, CanUndo/Redo + Undo/Redo, Serialize/LoadFrom }。各スタックはこれを実装するアダプタを持ち、**内部モデル (EditorState/NodeGraphState/…) は統一しない**
        - **`IDocumentProvider`** — Kind ↔ (new / open / serialize) の工場。ドメインが登録する (Unity の `[MenuItem]`・Unreal の asset editor 流)
        - **`Workspace`** — 開いている doc 群・アクティブ doc・ダーティ集約・undo を各 doc へ委譲
        - **`DockTree`** — 領域 + タブグループの再帰木 (直列化/復元可、**本格ドッキング**)。この木を描く UI は [ADR-0014](story:Internals/ADR/0014-Workbench-Ui-Controls) の `DockHost`
        - **`IDocumentStore`** — open / save / saveAs / watch (読み取り専用の `Luxel.Resources` VFS に Write を足すか新規に切る) + ダーティ・外部変更検知

        ドッグフードは **`GalleryApp` を Workbench で再構築**します。既定レイアウトを現 chrome (サイドバー｜プレビュー｜下ペイン｜右パネル) の再現にして **golden 中立**で移行します ([ADR-0007](story:Internals/ADR/0007-Floating-Ui-Placement) の「既存挙動を同等に写して diff 0」と同じ手口)。本格ドッキングの D&D は [ADR-0011](story:Internals/ADR/0011-Pointer-Button-Modifiers) を、UI コントロールは [ADR-0014](story:Internals/ADR/0014-Workbench-Ui-Controls) を、メニュー/コマンドは [ADR-0013](story:Internals/ADR/0013-Menu-Command-System) を、文書レンダラは [ADR-0012](story:Internals/ADR/0012-Rich-Document-Stack) を前提とします。

        実装計画は ToDo/26。

        ## Alternatives

        - **各エディタスタックを generic で統一** — [ADR-0006](story:Internals/ADR/0006-Editor-New-Stack) / [ADR-0009](story:Internals/ADR/0009-Node-Editor-Stack) の意図的なモデル分離に反し、共通化のためのインスタンス churn を生む → 却下 (不透明 host に留める)
        - **Gallery のシェルを流用** — 単一ドキュメント前提で、複数 doc・ドッキング・保存に再設計が要る → 参考に留め新規
        - **既存 IDE を組込 (VS Code webview 等)** — 自前レンダラに載らず別ランタイムを抱える ([ADR-0003](story:Internals/ADR/0003-Declarative-Signal-Ui) と同じ力学) → 却下

        ## Consequences

        - ✅ Text / Node / RichDoc / Code が 1 シェルに載り、エディタが構成として安く量産できる (1 スタックの横展開)
        - ✅ 内部モデルを触らないので既存スタックは無回帰
        - ✅ Gallery 自身が最大のドッグフード — docs をタブで開き、ドックに並べる
        - ⚠️ 本格ドッキング + 保存経路 + 複数 doc は**新規コード量が大きい** (だからこそ ADR-0011〜0014 に分割した)
        - ⚠️ Gallery 移行は golden 面積が広い — 既定レイアウト再現で中立化する規律が要る
        - ⚠️ `IEditorDocument` の serialize 契約を各スタックで満たす必要がある (ノード = JSON 往復、テキスト = 文字列、リッチ文書 = ブロック)
        """;
}
