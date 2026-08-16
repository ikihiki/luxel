using Luxel.Controls;
using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/Scripting")]
public static partial class LearnDomainSystems
{
    private static readonly string[] Routes =
    [
        "Learn/Scripting/ScriptingOverview", "Learn/Scripting/BrowserExecution",
        "Learn/Scripting/ScriptingReload", "Learn/Scripting/NotebookAndRepl",
        "Learn/Scripting/IsolationAndDiagnostics",
    ];

    private static DocMarkdown Meta(string path, string prerequisites)
    {
        int index = Array.IndexOf(Routes, path);
        string previous = index > 0 ? $"**前へ:** [{Routes[index - 1].Split('/')[^1]}](story:{Routes[index - 1]})" : "";
        string next = index >= 0 && index + 1 < Routes.Length ? $"**次:** [{Routes[index + 1].Split('/')[^1]}](story:{Routes[index + 1]})" : "";
        return new DocMarkdown($"**難易度:** 中級　 **環境:** Browser / Native　 **前提:** {prerequisites}\n\n{previous}{(previous.Length > 0 && next.Length > 0 ? "　 " : "")}{next}");
    }

    [Story]
    public static StoryResult ScriptingOverview(StoryContext ctx) => $$"""
        # Scripting学習ガイド

        {{Toc()}}

        {{Meta("Learn/Scripting/ScriptingOverview", "C#、非同期処理")}}

        Luxel Scriptingは、コードの編集、compile、診断、実行、成功結果の公開を共通の契約で扱います。Browser版は公開時に固定したmetadata imageを使い、Native版は継続REPLなど追加の実行方式を提供します。

        {{StoryRef("Learn/Scripting/LiveCsxSample")}}

        ## 処理の流れ

        ```text
        source revision → compile → diagnostics → isolated execution → successful result publication
        ```

        compile成功と実行成功を分けて扱います。新しいrevisionが失敗しても直前の成功結果を維持し、エラーを診断として表示できます。

        ## 学習順

        1. [Browserでの実行](story:Learn/Scripting/BrowserExecution)
        2. [再読み込み](story:Learn/Scripting/ScriptingReload)
        3. [NotebookとREPL](story:Learn/Scripting/NotebookAndRepl)
        4. [分離と診断](story:Learn/Scripting/IsolationAndDiagnostics)
        """;

    [Story]
    public static StoryResult BrowserExecution(StoryContext ctx) => $$"""
        # BrowserでC#を実行する

        {{Toc()}}

        {{Meta("Learn/Scripting/BrowserExecution", "Scripting学習ガイド")}}

        Browser Galleryは`Luxel.Scripting.Roslyn.Web`の`WebScriptCompiler`と`WebScriptExecutor`を使います。実行時に任意のDLLを探索せず、ホストが公開したmanifestとmetadata imageだけをcompile参照へ使います。

        ## ホストが用意するもの

        1. compileを許可するアセンブリの静的Web asset
        2. アセンブリ名を列挙するmanifest
        3. `BrowserRoslynGalleryRuntime`
        4. 言語サービスと実行サービスのDI登録

        ## 制約

        BrowserではUI threadを同期的にblockしません。compileと実行は非同期で待ち、キャンセル可能にします。ファイルシステム、ネイティブAPI、動的なアセンブリ探索を前提にしたスクリプトはNative側へ分離してください。

        > [!WARNING]
        > metadata参照へ追加したDLLは配布サイズと公開API面を増やします。Galleryで編集可能にしたい型だけを明示的に登録してください。
        """;

    [Story]
    public static StoryResult ScriptingReload(StoryContext ctx) => $$"""
        # 再読み込みとlast-good表示

        {{Toc()}}

        {{Meta("Learn/Scripting/ScriptingReload", "Browserでの実行")}}

        hot reloadは新しいWidgetを別revisionとしてcompileし、実行成功時だけプレビューを差し替えます。compileまたは実行に失敗した場合は、直前の成功プレビューを維持して診断を表示します。

        {{StoryRef("Learn/Scripting/HotReloadSample")}}

        ## revisionの扱い

        - 編集するたびに単調増加するrevisionを割り当てる
        - 古いcompileが後から完了しても公開しない
        - 新しい結果の実行成功を確認してから表示を交換する
        - 置換されたWidgetとResource leaseを確実に解放する

        この規則により、入力の速さとcompile時間が逆転しても古いコードへ戻りません。
        """;

    [Story]
    public static StoryResult NotebookAndRepl(StoryContext ctx) => $$"""
        # NotebookとREPL

        {{Toc()}}

        {{Meta("Learn/Scripting/NotebookAndRepl", "再読み込み")}}

        Notebookはセルごとのsourceと結果を保持し、同じcompile contractと診断モデルを使います。Browser版では各セルを明示的な入力からcompileし、隠れたプロセス状態を最小限にします。

        {{StoryRef("Learn/Scripting/NotebookSample")}}

        ## Notebookに向く処理

        - 値やAPIを小さく試す
        - 診断と出力をセル単位で残す
        - 同じ入力から結果を再現する

        継続submission、長寿命の変数、Nativeサービスへ接続するREPLはNative Galleryで扱います。BrowserとNativeで名前が同じ機能でも、利用可能な参照と寿命は同一とは限りません。
        """;

    [Story]
    public static StoryResult IsolationAndDiagnostics(StoryContext ctx) => $$"""
        # 分離、キャンセル、診断

        {{Toc()}}

        {{Meta("Learn/Scripting/IsolationAndDiagnostics", "NotebookとREPL")}}

        ユーザーコードの失敗をGalleryホストの失敗として扱わないよう、compile診断、実行例外、キャンセル、ホスト設備の障害を分けて記録します。

        | 種類 | 表示する情報 | ホストの動作 |
        |---|---|---|
        | Compile error | ID、行、列、メッセージ | 実行せずlast-goodを維持 |
        | Runtime error | 例外型、メッセージ、対象revision | 新しい結果を公開しない |
        | Cancel | 対象revisionと理由 | 古い完了を無視する |
        | Host failure | metadata、worker、DIなどの設備情報 | Scripting機能を失敗状態にする |

        ## 終了時の確認

        実行へ渡したCancellationTokenを停止し、worker、scope、生成Widget、Resource leaseを順番に解放します。画面を閉じた後に古いrevisionがOutputへ書き込まないことも確認してください。

        完成例は[Examples/Scripting](story:Examples/Scripting/HotReload)で確認できます。存在する機能名は環境ごとに異なるため、Native専用Storyには明示的な対応範囲を記載します。
        """;
}
