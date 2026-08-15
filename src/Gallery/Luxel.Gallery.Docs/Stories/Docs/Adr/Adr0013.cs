using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story]
    public static StoryResult Adr0013(StoryContext ctx) => $$"""
        # ADR-0013 — メニューは CommandRegistry を単一の真実とし、全サーフェスをその純粋ビューとして生成する

        {{Toc()}}

        - **Status**: Accepted
        - **Date**: 2026-07-09
        - **Deciders**: ikihiki

        ## Context

        > [!NOTE]
        > `ToDo/22`〜`ToDo/27` はADR作成当時の計画番号で、現在のファイル参照ではありません。現行の実装と利用手順は本文からリンクするLearnページと実装を正とします。


        Workbench ([ADR-0010](story:Internals/ADR/Adr0010)) にメニュー/コマンド起動の面が要ります。現状 `MenuBar`・コマンドパレット・汎用ツールバーは無く、あるのは `ContextMenu` と浮遊 UI 配置エンジン ([ADR-0007](story:Internals/ADR/Adr0007)) だけです。

        ゲームエンジンのエディタを調査すると、メニューの表現には共通の作法があります: **メニュー/ツールバー/キーマップ/検索を単一のコマンド定義から生成し、寄与 (contribution) で拡張する**。Unity は `[MenuItem("パス")]`、Unreal は `UToolMenus` + `FUICommandInfo` (メニュー/ツールバー/キーが同じコマンドを参照)、Blender はオペレータ (`bpy.ops`、メニュー/キー/F3 検索が共有)。いずれもメニューバーは薄く、発見性はコマンドパレット/検索が担います。

        リボン (Office Fluent UI) は候補になりますが、**特許 (意匠 + 実用、Corel 訴訟で行使実績あり) とライセンス条項 (旧 Office UI License は Office 競合アプリに使用不可・現在はプログラム撤回) の懸念**があり、加えて IDE/開発ツールの慣習にも合いません (発見性のために重い)。

        ## Decision

        **`CommandRegistry` を「コマンドの単一の真実」**に据え、メニュー系サーフェスをその純粋ビューとして生成します。リボンは採りません。

        - **コマンド** { id, タイトル, キーバインド, enablement, run(ctx) } を登録し、**`MenuBar` / `ContextMenu` / `CommandPalette` / `Toolbar` / Keymap** はすべてこの Registry のビュー
        - **寄与モデル** — 項目はパス文字列 (例 `"Edit/Find"`) + コマンド id で登録し、パスがそのまま階層になる (Unity 流)。**共通メニュー (Workbench) + アクティブ `IEditorDocument` の文脈メニュー節を合成**する (Unreal の per-editor 流)
        - **発見性の主役はコマンドパレット** (Blender の F3 相当)。メニューバーは薄く保つ
        - `IEditorDocument` ([ADR-0010](story:Internals/ADR/Adr0010)) に「メニュー/ツールバー寄与」の面を 1 本追加する

        実装計画は ToDo/26。

        ## Alternatives

        - **リボン (Fluent UI 風)** — 特許/ライセンスの懸念 + IDE 慣習外 + 発見性のために過剰 → 却下 (独自意匠のグルーピング表示が必要になっても CommandRegistry の上に後付けできる)
        - **メニューを手書きで固定** — 拡張のたびに 1 か所が肥大化し、アクティブ doc 依存の文脈メニューにできない → 却下
        - **コマンドを持たず各サーフェスが独自にアクションを実装** — 重複と、キーバインド/メニュー/パレット間の不整合が生じる → 却下

        ## Consequences

        - ✅ メニュー/パレット/ツールバー/キーマップが 1 モデルから生え、整合が自動で取れる
        - ✅ アクティブ doc が文脈メニューを寄与でき、エディタ種別ごとに chrome が切り替わる
        - ✅ リボンを避けつつ発見性はパレットで担保し、特許面もクリーン
        - ⚠️ 全コントロールをコマンド経由に寄せる**規律**が要る (直接アクション実装を避ける)
        - ⚠️ enablement / keymap の評価タイミング (アクティブ doc の変化に追従) を正しく配線する必要がある
        """;
}
