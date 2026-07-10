using Luxel.Diagnostics;
using Luxel.TwoD;

namespace Luxel.UI;

/// <summary>
/// UI 内で発生したユーザーコード例外の集約先 (エラー境界)。ライブコーディングのように
/// 「壊れたコードを頻繁に評価する」アプリでも、Build/Effect/入力ハンドラ/アニメーションの
/// 例外でアプリが落ちない — 報告して該当箇所だけ縮退する。
/// </summary>
public static class UiError
{
    /// <summary>例外を報告する (Console.Error + 診断イベント "error" — DevTools/Gallery の Log に出る)。</summary>
    public static void Report(Exception ex, string context)
    {
        Console.Error.WriteLine($"[ui-error] {context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Input))
            EngineDiagnostics.Emit(EngineDiagnostics.Input, new DiagInput("error", $"{context}: {ex.Message}"));
    }
}

/// <summary>Build が throw したときに <see cref="CompositeControl"/> が差し替える縮退表示
/// (赤枠 + 例外メッセージ)。次の Rebuild で本来の Build が再試行される。</summary>
internal sealed class ErrorWidget(Exception ex) : Widget
{
    private const float H = 44;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
        => Size = c.Constrain(new Size(float.IsInfinity(c.MaxW) ? 320 : MathF.Max(120, c.MaxW), H));

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        var s = new Scene2D();
        s.StrokeRoundedRect(Color2D.White, 2f, 1, 1, Size.Width - 2, H - 2, 6);
        string msg = $"⚠ {ex.GetType().Name}: {ex.Message}";
        if (msg.Length > 120) msg = msg[..120] + "…";
        ctx.Font.AppendText(s, msg, 10, H / 2 + ctx.Font.Ascent(12) / 2 - 2, 12, Color2D.White);
        node.Content = s;
        ctx.Effect(() => node.Color = ctx.Theme.Value.Danger);
    }

    public override string? DebugDetail => ex.Message;
}
