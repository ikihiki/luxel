using Luxel.UI;
using static Luxel.Controls.Kit;

namespace LuxelCavern.Core;

/// <summary>
/// タイトル画面の UI 組み立て (純ロジック — GPU/実窓に非依存で、Gallery ストーリーからも
/// スタンドアロン exe からも同じツリーを構築できる)。ボタン押下は <see cref="Actions"/> の
/// コールバック経由で外側 (ホスト) に委譲する。
/// </summary>
public static class TitleScreen
{
    /// <summary>タイトル画面のボタンが呼ぶコールバック束。<paramref name="Continue"/> が null なら
    /// 「つづきから」ボタンは出さない (セーブが無い状態。永続化は ToDo 15 で追加)。</summary>
    public sealed record Actions(Action Start, Action Settings, Action Quit, Action? Continue = null);

    public const string GameTitle = "Luxel Cavern";
    public const string Subtitle = "洞窟探索アクション — Luxel エンジン capstone ①";

    /// <summary>タイトル画面ツリーを組み立てる。背景で画面全体を塗り、中央にタイトルとメニューを積む。</summary>
    public static Widget Build(Actions actions)
    {
        var menu = new List<Widget>
        {
            Heading(GameTitle, 1),
            Muted(Subtitle),
            Spacer(height: 20f),
            MenuButton("はじめる", actions.Start),
        };
        if (actions.Continue is { } cont)
            menu.Add(MenuButton("つづきから", cont));
        menu.Add(MenuButton("せってい", actions.Settings));
        menu.Add(MenuButton("おわる", actions.Quit));

        return Border(
            background: Bind.From(() => UiTheme.T.Background),
            hAlign: Align.Stretch, vAlign: Align.Stretch)
        [
            Center()[VStack(12, width: 260f)[menu.ToArray()]]
        ];
    }

    private static Widget MenuButton(string text, Action onClick)
        => Button(_ => onClick(), text, hAlign: Align.Stretch);
}
