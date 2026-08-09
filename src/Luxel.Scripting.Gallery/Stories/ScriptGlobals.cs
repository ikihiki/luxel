using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>
/// スクリプト (csx / REPL / ノートブック) から**裸で見える API 面** (Roslyn の globals)。
/// <see cref="Luxel.Scripting.ScriptHost"/> はこの型でコンパイルし、実行インスタンスは
/// ストーリー毎に <c>new ScriptGlobals { Ctx = ctx }</c> で差し替える (Log の宛先が各ストーリー)。
/// </summary>
public sealed class ScriptGlobals
{
    private readonly Func<StoryContext?>? _live;
    private StoryContext? _ctx;

    /// <summary>固定文脈で作る (ストーリー毎の <c>new ScriptGlobals { Ctx = ctx }</c> 用)。</summary>
    public ScriptGlobals() { }

    /// <summary>文脈を**遅延解決**する (Gallery の常設 Console タブ用 — セッションは 1 度だけ開き、
    /// Log の宛先は毎回「今選択中のストーリー」に追従する)。</summary>
    public ScriptGlobals(Func<StoryContext?> live) => _live = live;

    /// <summary>実行中ストーリーの文脈 (Log の宛先)。遅延解決版なら毎参照で現在値。</summary>
    public StoryContext? Ctx { get => _live is not null ? _live() : _ctx; init => _ctx = value; }

    /// <summary>Log パネルへ (スクリプトの第一のデバッグ手段)。</summary>
    public void Log(string message) => Ctx?.Log(message);
}
