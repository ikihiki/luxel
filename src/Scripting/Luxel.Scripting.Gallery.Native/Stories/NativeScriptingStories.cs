using Luxel.Controls;
using Luxel.Scripting;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

[StoryMeta("Examples/Scripting")]
public static class NativeScriptingStories
{
    [Story]
    public static StoryResult Repl(StoryContext ctx, ScriptHost host)
    {
        var repl = new ReplConsole(460, host, new ScriptGlobals { Ctx = ctx });
        ctx.Play(async d =>
        {
            await d.Snap();
            repl.SetInput("var a = 21;");
            await d.Click(repl.SubmitButton);
            repl.SetInput("a * 2");                  // 前の行の変数が見える
            await d.Click(repl.SubmitButton);
            await d.Step(2);
            await d.Snap("session");
            await d.Expect(() => repl.LastOutput == "42", "継続セッションで状態が残る");
        });
        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("REPL コンソール (継続セッション)"),
                Muted("行を投入すると前の行で宣言した変数が次に見える — DevTools のスクリプトコンソール相当。"),
                repl]];
    }

}
