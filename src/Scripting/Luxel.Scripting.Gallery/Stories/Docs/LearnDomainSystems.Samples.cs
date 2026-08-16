using Luxel.Controls;
using Luxel.Scripting.Gallery;
using Luxel.Scripting.Roslyn.Web;
using Luxel.UI;

namespace Luxel.Gallery.Stories;

public static partial class LearnDomainSystems
{
    [Story]
    public static StoryResult LiveCsxSample(StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage language)
        => ScriptingStory.LiveCsx(ctx, runtime, language);

    [Story]
    public static StoryResult HotReloadSample(StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage language)
        => BrowserScriptHotReloadStory.HotReload(ctx, runtime, language);

    [Story]
    public static StoryResult NotebookSample(StoryContext ctx, BrowserRoslynGalleryRuntime runtime, ICodeLanguage language)
        => ScriptingStory.Notebook(ctx, runtime, language);
}
