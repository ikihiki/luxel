using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — Workbench (Luxel.Workbench、ADR-0010〜0014)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsWorkbench
{
    [Story("Reference/Guides/Workbench", Order = 43, Toc = true)]
    public static StoryResult Workbench(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());   // mermaid 図 + コードの描画 golden (ライブ埋め込み無し)
        return $$"""
        # Workbench (Luxel.Workbench)

        複数のエディタを「**開いて・並べて・保存する**」シェルのフレームワークです。エディタは特定型をハードコードせず**構成**として載せます — テキスト ([Reference/Guides/Editor](story:Reference/Guides/Editor))・ノード ([Reference/Guides/NodeEditor](story:Reference/Guides/NodeEditor))・Inspector が同じ契約で並び、内部モデルは統一しません。決定は [ADR-0010](story:Internals/ADR/0010-Workbench-Framework) (コア) / [ADR-0013](story:Internals/ADR/0013-Menu-Command-System) (コマンド) / [ADR-0014](story:Internals/ADR/0014-Workbench-Ui-Controls) (基盤 UI)。

        ```mermaid
        flowchart TB
        doc[IEditorDocument - 不透明ハンドル] --> ws[Workspace - 開閉/アクティブ/ダーティ集約]
        tree[DockTree - レイアウトの真実] --> host[DockHost - 描画+D&D]
        ws --> host
        store[IDocumentStore - open/save/watch] --> ws
        reg[CommandRegistry - コマンドの真実] --> menu[MenuBar / CommandPalette / Toolbar / Keymap]
        ```

        ## IEditorDocument — 1 ドキュメントの不透明ハンドル

        { Kind, Title, `Signal<bool>` Dirty, `CreateView()`, CanUndo/Redo + Undo/Redo, Serialize/LoadFrom, Contributions }。各エディタスタックがこれを実装するアダプタを持ち、シェルは契約だけを見ます。標準アダプタ (Luxel.Controls):

        - **`TextDocument`** — Code / Markdown / Strudel は **viewFactory の構成差だけ** (プロバイダ/言語サービス/フォント)。真実は `Signal<string>`、undo はビューへ委譲
        - **`NodeGraphDocument`** — 直列化は `NodeGraphJson` の JSON 往復。ドメインは `INodeCatalog` を configure で渡す
        - **`ObjectDocument<T>`** — 設定/コンポーネントを [PropertyGrid](story:Controls/PropertyGrid/Basic) で Inspector 編集。直列化は JSON、undo は**プロパティ変更単位**

        ## Workspace と DockTree

        - **`Workspace`** — 開いている doc 群・`Signal` のアクティブ・**ダーティ集約** (`Computed<bool>` AnyDirty、開閉にも各 doc の Dirty にも追従)・undo/redo のアクティブ doc への委譲。「保存しますか」はシェルの責務
        - **`DockTree`** — 領域 + タブグループの**不変**再帰木 (レイアウトの真実)。分割 `Dock(tab, group, side)`・移動 `MoveTab`・**窓内フローティング** (`Float`/`MoveFloat`)・JSON 直列化。空グループは畳み、同方向の入れ子は平坦化
        - **`DockHost`** — ツリーを描く container。タブ帯 = [DocumentTabs](story:Controls/DocumentTabs/Basic) (ダーティ ●・×・D&D)、分割 = Splitter、**端 25% へのドロップで分割**、フロートはつかみバーで移動。すべて tree signal を書き換えて自動再構築

        実物: [Controls/DockHost/Basic](story:Controls/DockHost/Basic) (ドラッグ分割/リサイズ/グループ間移動) / [Floating](story:Controls/DockHost/Floating) (窓内フローティング)。

        ## IDocumentStore — 実ファイルの open/save

        doc ↔ path の結線と永続化。低レベル FS は `IFileStorage` (Exists/Read/Write/Watch/List — メモリ実装と実ディスク実装)。**外部変更検知**は自書込エコーを保存点比較で無視し、`DocumentBinding.ExternalChange` signal で「再読込しますか」をシェルに委ねます。[AssetBrowser](story:Controls/AssetBrowser/Basic) がファイルツリー → open の入口。

        ## コマンド (ADR-0013)

        **`CommandRegistry` がコマンドの単一の真実**: { id, タイトル, キーバインド, enablement, run }。メニュー項目は**パス文字列** ("File/保存") の寄与で登録し、[MenuBar](story:Controls/MenuBar/Basic) / [CommandPalette](story:Controls/CommandPalette/Basic) (発見性の主役、↑↓/Enter) / [Toolbar](story:Controls/Toolbar/Basic) / Keymap (`BindShortcuts` — フォーカスが消費しないキーだけ届く) はその純粋ビューです。アクティブ doc は `IEditorDocument.Contributions` で文脈メニュー/ツールバー/キーを寄与できます。

        ## 組み立て例

        ```csharp
        var ws = new Workspace();
        ws.RegisterProvider(new TextProvider("code", CodeView));   // 拡張子 → kind はシェルが解決
        var store = new DocumentStore(ws, new PhysicalFileStorage(root));
        var tree = new Signal<DockTree>(DockTree.Single());
        var reg = new CommandRegistry();
        reg.Register("file.save", "保存", () => store.Save(ws.Active.Peek()!),
            enabled: () => ws.Active.Peek()?.Dirty.Peek() == true, key: "Ctrl+S", toolbar: true);
        // AssetBrowser.OnOpen → store.Open → tree.AddTab、DockHost が描く
        ```

        実物 (フル構成のシェル): [Examples/Workbench/Shell](story:Examples/Workbench/Shell) (4 エディタ + メニュー/キーマップ) / [Files](story:Examples/Workbench/Files) (実ファイル open/save/外部変更) / [Material](story:Examples/Workbench/Material) (**新ドメインを構成だけで追加** — マテリアルグラフ + Slang) / [Inspector](story:Examples/Workbench/Inspector) (PropertyGrid エディタ)。

        > [!NOTE]
        > **Gallery 自身がドッグフード**です — この画面のサイドバー/プレビュー/下ペイン (Log/Knobs/Interactions/Console のタブ)/Props は DockTree + DockHost で組まれており、下ペインのタブは D&D で動かせます。単一タブのペインはタブ帯を隠して従来の chrome と同じ見た目にしています (`hideSingleTabStrip`)。
        """;
    }
}
