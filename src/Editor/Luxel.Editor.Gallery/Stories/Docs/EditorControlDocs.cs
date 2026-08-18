using Luxel.Controls;

namespace Luxel.Editor.Gallery;

/// <summary>利用者向け Editor コンポーネントの日本語 Docs。</summary>
internal static class EditorControlDocs
{
    private sealed record Page(string Type, string Overview, string Usage, string State, string MainApi,
        string Operations, string Theme, string Limits, string Api, string[] Examples);

    private static readonly Page[] Pages =
    [
        new("AssetBrowser",
            "プロジェクト内の資産を階層・一覧で探し、選択や起動へつなぐブラウザーです。ファイル管理画面や素材選択に使います。",
            "AssetBrowser(model)",
            "資産ツリー、選択、現在位置、読み込み結果はブラウザーモデル側が所有します。表示側だけに永続状態を置かないでください。",
            "資産モデル、選択状態、開く・選択・コンテキスト操作の通知、表示寸法が中心です。",
            "ポインターによる選択、フォルダー移動、項目起動を扱います。キーボード一覧操作と支援技術向け階層意味は保証されないため、検索やパス入力などの代替経路を用意してください。",
            "エディターの表面色、選択色、アイコン尺度に合わせ、一覧へ十分な幅と高さを与えます。",
            "資産の監視、インポート、権限、競合、巨大一覧の性能はモデルと ResourceSystem の構成に依存します。購読とハンドルを破棄時に解放します。",
            "AssetBrowser", ["Controls/AssetBrowser/Basic"]),
        new("DockHost",
            "複数のツール・文書パネルを分割、移動、フロート、再ドックできる作業領域です。エディターの主シェルに使います。",
            "DockHost(layout, items)",
            "DockTree、各 DockItem、選択、分割比率、フロート位置は Workbench 側が所有し、必要な部分だけ永続化します。",
            "レイアウト木、項目辞書、選択・移動・分割・フロート変更通知、ホスト寸法が中心です。",
            "タブ選択、ドラッグ移動、スプリッター調整、フロート操作を扱います。完全なキーボードドッキングや支援技術表現は提供されないため、配置リセットやメニュー操作を代替として用意してください。",
            "アプリ全体の領域を明示し、ドロップ候補、選択タブ、境界をテーマで区別します。",
            "レイアウトの妥当性、最小寸法、保存形式の移行、画面外フロートの回収は呼び出し側の責任です。子ビューの生成・破棄回数にも注意します。",
            "DockHost", ["Controls/DockHost/Basic"]),
        new("MenuBar",
            "CommandRegistry から主要コマンドを階層メニューとして表示します。アプリ全体で安定した操作の入口に使います。",
            "MenuBar(registry)",
            "コマンド定義、有効状態、実行処理は CommandRegistry が単一の真実として所有し、MenuBar はそのビューになります。",
            "CommandRegistry、必要に応じた contribution、メニュー配置が中心です。コマンド実行は登録済み処理へ委譲します。",
            "ポインターでメニューを開き項目を実行します。表示ショートカットと Keymap を一致させ、キーボードメニュー操作と読み上げは対象ホストで検証してください。",
            "画面上端へ配置し、メニュー面、選択行、無効状態をエディターテーマで示します。",
            "コマンドそのもの、Undo、権限、エラー表示は管理しません。レジストリと contribution の寿命をホストに合わせます。",
            "MenuBar", ["Controls/MenuBar/Basic"]),
        new("SceneEditorView",
            "シーンの表示、選択、編集ツールをまとめるエディタービューです。ゲームや可視化シーンの作業領域に使います。",
            "SceneEditorView(document)",
            "シーン文書、選択、変更履歴、保存状態は文書モデル側が所有し、カメラや一時ツール状態はビュー側が保持します。",
            "シーン文書、レンダリング設備、選択・編集通知、表示寸法、ツール構成が中心です。",
            "ビューポート上の選択、カメラ操作、編集ツールを扱います。操作体系はツール構成に依存し、完全なキーボード操作や支援技術表現は提供されません。階層・プロパティ編集を代替経路にします。",
            "大きな表示領域を与え、選択輪郭、グリッド、ツール表示をテーマとシーンの両方に対して見分けやすくします。",
            "GPU とシーン資源が必要です。保存、Undo、競合、実行時シーンとの同期、資源破棄は文書・ホスト側の責任です。",
            "SceneEditorView", ["Controls/SceneEditorView/Basic"]),
        new("SceneInspector",
            "選択したシーン要素の階層やプロパティを表示・編集するインスペクターです。",
            "SceneInspector(selection)",
            "選択対象、シーンデータ、確定値、Undo 履歴はエディター文書側が所有します。インスペクターは現在選択の投影と編集入口です。",
            "選択、プロパティ定義、変更通知、読み取り専用状態が中心です。",
            "各プロパティエディターの操作に従います。ラベル、単位、検証結果を明示し、キーボード移動と支援技術対応は組み込む各入力ごとに検証してください。",
            "ラベル列、値列、セクション、エラー色を他のプロパティ画面と統一します。狭すぎる幅を避けます。",
            "複数選択の混在値、型変換、循環参照、Undo、保存は自動ではありません。選択解除時に一時編集をどう確定するか決めてください。",
            "SceneInspector", ["Controls/SceneInspector/Basic"]),
        new("Toolbar",
            "CommandRegistry の頻繁に使う操作をアイコンや短いラベルで並べるコマンドビューです。",
            "Toolbar(registry, commandIds)",
            "コマンド定義、有効状態、実行処理は CommandRegistry が所有し、Toolbar は選んだコマンドを投影します。",
            "CommandRegistry、commandIds、配置、区切りが中心です。押下時は登録コマンドを実行します。",
            "ポインターで起動します。アイコンだけにせず Tooltip やラベルを用意し、Keymap がある場合は表示と一致させます。専用の読み上げ情報は保証されません。",
            "エディターのアイコン尺度、Intent、無効色に合わせ、関連操作を区切って並べます。",
            "コマンド検索、履歴、権限、非同期進捗は管理しません。コマンド ID が未登録の場合の扱いをホストで確認してください。",
            "Toolbar", ["Controls/Toolbar/Basic"]),
    ];

    internal static void Register(StoryCatalogBuilder builder, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors)
    {
        Dictionary<string, GeneratedComponentStoryDescriptor> byType = descriptors.ToDictionary(static item => item.ComponentType, StringComparer.Ordinal);
        foreach (Page page in Pages)
        {
            string identity = $"global::Luxel.Controls.{page.Type}";
            if (!byType.TryGetValue(identity, out GeneratedComponentStoryDescriptor? descriptor))
                throw new InvalidOperationException($"Docs 対象のコンポーネントが見つかりません: {identity}");
            builder.Add(new StoryInfo(descriptor.DocsPath, _ => Build(page, descriptor), Source: "日本語で執筆した Editor コンポーネント Docs。"), replaceGenerated: true);
        }
    }

    private static StoryResult Build(Page page, GeneratedComponentStoryDescriptor descriptor)
    {
        var result = new StoryResult(1600, 1);
        result.AppendLiteral($"# {page.Type}\n\n## 概要と用途\n\n{page.Overview}\n\n## 最小使用例\n\n```csharp\n{page.Usage}\n```\n\n## 状態の所有\n\n{page.State}\n\n## 主なパラメーターとイベント\n\n{page.MainApi}\n\n## 操作・キーボード・アクセシビリティ\n\n{page.Operations}\n\n## テーマとレイアウト\n\n{page.Theme}\n\n## 制約・能力・ライフサイクル\n\n{page.Limits}\n\n## API リファレンス\n\n");
        result.AppendFormatted(new DocEmbed(global::Luxel.Gallery.UI.Kit.ApiTable(page.Api, inherited: true, width: 760), DocEmbedKind.ControlApiTable, page.Api, IncludeInherited: true));
        result.AppendLiteral($"\n\n## 関連する Basic と Examples\n\n- [Basic](story:{descriptor.BasicPath})\n");
        foreach (string example in page.Examples.Where(path => path != descriptor.BasicPath)) result.AppendLiteral($"- [{example[(example.LastIndexOf('/') + 1)..]}](story:{example})\n");
        return result;
    }
}
