using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>
/// スクリプト (csx / REPL / ノートブック) から**裸で見える API 面** (Roslyn の globals)。
/// <see cref="Luxel.Scripting.ScriptHost"/> はこの型でコンパイルし、実行インスタンスは
/// ストーリー毎に <c>new ScriptGlobals { Ctx = ctx }</c> で差し替える (Log の宛先が各ストーリー)。
/// </summary>
public sealed class ScriptGlobals
{
    /// <summary>実行中ストーリーの文脈 (Log の宛先)。</summary>
    public StoryContext? Ctx { get; init; }

    /// <summary>Log パネルへ (スクリプトの第一のデバッグ手段)。</summary>
    public void Log(string message) => Ctx?.Log(message);
}
