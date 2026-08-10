using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnDomainSystems
{
    [Story("Learn/Scripting/Overview", Order = 0, SampleBundle = "scripting.gallery", Toc = true)]
    public static StoryResult ScriptingOverview(StoryContext ctx) => $$"""
        # Scripting overview

        Browser Galleryは`Luxel.Scripting.Roslyn.Web`の`WebScriptCompiler`と`WebScriptExecutor`を使い、publish時に固定したmetadata imageだけでC#をcompileします。Live CSX、notebook、multi-file Playgroundは同じWeb contractとdiagnostic modelを共有します。継続submissionが必要なREPLはNative Galleryで扱います。

        {{StoryRef(ctx, "Examples/Scripting/LiveCsx")}}
        {{SampleBundle("scripting.gallery")}}
        """;

    [Story("Learn/Scripting/ReloadAndIsolation", Order = 1, Toc = true)]
    public static StoryResult ScriptingReload(StoryContext ctx) => $$"""
        # Reload, diagnostics, and isolation

        browser hot reloadは新しいWidgetを別revisionとしてcompileし、実行成功時だけpreviewを差し替えます。compile error時は直前の成功previewを保持し、diagnosticを表示します。継続REPLやECS delegate差し替えなどRoslyn Scripting固有の状態fulな例はNative Galleryへ分離します。

        {{StoryRef(ctx, "Examples/Scripting/HotReload")}}
        {{StoryRef(ctx, "Examples/Scripting/Notebook")}}
        """;
}
