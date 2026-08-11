using Luxel.Ecs;

namespace Luxel.Scripting.Framework;

/// <summary>
/// .csx スクリプトが最後の式で返す「system 登録記述子」。フェーズ別の <c>(World, dt)</c> デリゲートを持つ。
/// スクリプトは <see cref="ScriptGameGlobals.Systems"/> ヘルパで作る (<c>Systems(update: (w, dt) =&gt; {...})</c>)、
/// または裸の <c>Action&lt;World, float&gt;</c> を返すと Update 扱い。
/// </summary>
public sealed record ScriptSystems(
    Action<World, float>? Update = null,
    Action<World, float>? LateUpdate = null,
    Action<World, float>? FixedUpdate = null);

/// <summary>
/// ゲームスクリプトの globals (メンバはスクリプト内で裸で書ける)。<see cref="Log"/> でデバッグ出力、
/// <see cref="Systems"/> で記述子を作る。<c>ScriptHost</c> 構築時にこの型を globalsType に渡す。
/// </summary>
public class ScriptGameGlobals
{
    private readonly Action<string>? _log;

    public ScriptGameGlobals(Action<string>? log = null) => _log = log;

    /// <summary>ログ出力 (スクリプトの第一のデバッグ手段)。</summary>
    public void Log(string message) => _log?.Invoke(message);

    /// <summary>フェーズ別デリゲートから <see cref="ScriptSystems"/> を作る。</summary>
    public ScriptSystems Systems(
        Action<World, float>? update = null,
        Action<World, float>? lateUpdate = null,
        Action<World, float>? fixedUpdate = null)
        => new(update, lateUpdate, fixedUpdate);
}
