using Luxel.Ecs;

namespace Luxel.Scripting.Framework;

/// <summary>
/// .csx ゲームロジックを <see cref="World"/> のフェーズループに接続し hot reload する。
/// <para><b>安全性が最重要</b>: <see cref="Reload"/> でコンパイル失敗/実行時例外が出ても**旧 system を動かし続け**、
/// 診断を <see cref="LastResult"/>/<see cref="RuntimeException"/> に公開する (ゲームを止めない)。</para>
/// <para><b>仕組み</b>: <see cref="World.AddSystem(string, Action)"/> は削除できないので、<see cref="Attach"/> で
/// **安定ラッパ system を 1 回だけ登録**し、reload は現在のデリゲート束 (<see cref="ScriptSystems"/>) を差し替えるだけ。
/// 変更は <see cref="IScriptSource.Changed"/> でフラグに立て、<see cref="PollReload"/> (フレーム先頭) で 1 回反映する。</para>
/// </summary>
public sealed class ScriptSystem : IDisposable
{
    private readonly ScriptHost _host;
    private readonly IScriptSource _source;
    private readonly object _globals;
    private readonly Action<string>? _log;

    private ScriptSystems _current = new();
    private volatile bool _dirty;
    private bool _faulted;

    public ScriptSystem(ScriptHost host, IScriptSource source, object globals, Action<string>? log = null)
    {
        _host = host;
        _source = source;
        _globals = globals;
        _log = log;
        _source.Changed += OnSourceChanged;
        Reload();   // 初回コンパイル
    }

    /// <summary>直近の Reload 結果 (診断/コンパイル状態)。</summary>
    public ScriptResult? LastResult { get; private set; }

    /// <summary>スクリプトデリゲート実行中に投げられた例外 (捕捉済み — 以後 reload まで停止)。</summary>
    public Exception? RuntimeException { get; private set; }

    /// <summary>コンパイル失敗 or 実行時例外で停止中か。</summary>
    public bool HasError => LastResult is { Success: false } || _faulted;

    private void OnSourceChanged() => _dirty = true;

    /// <summary>ソースを読んで再コンパイル。成功時のみデリゲートを差し替え、失敗時は旧を維持する。</summary>
    public bool Reload()
    {
        _dirty = false;
        ScriptResult r = _host.Run(_source.Read(), _globals);
        LastResult = r;

        ScriptSystems? next = r.Success ? Extract(r.ReturnValue) : null;
        if (next is not null)
        {
            _current = next;
            _faulted = false;
            RuntimeException = null;
            return true;
        }

        // コンパイル/実行失敗 or 記述子でない → 旧 _current を維持 (診断は LastResult に)
        _log?.Invoke($"script reload failed: {FirstError(r)}");
        return false;
    }

    /// <summary>ソースが変わっていれば Reload する (フレーム先頭で呼ぶ — 連続変更を 1 回に畳む)。</summary>
    public void PollReload()
    {
        if (_dirty) Reload();
    }

    // Luxel.Framework.Game.Phase の Name と一致 (Framework を参照しないため文字列で持つ)。
    private const string FixedUpdatePhase = "FixedUpdate";
    private const string UpdatePhase = "Update";
    private const string LateUpdatePhase = "LateUpdate";

    /// <summary>安定ラッパ system を <paramref name="world"/> の各フェーズへ登録する。
    /// <paramref name="dt"/> は現フレームのデルタ秒を返すプロバイダ (シーンが毎フレーム更新するフィールド等)。
    /// 以後 reload しても再登録は不要 (ラッパが最新デリゲートを呼ぶ)。</summary>
    public void Attach(World world, Func<float> dt)
    {
        world.AddSystem(FixedUpdatePhase, () => RunFixedUpdate(world, dt()));
        world.AddSystem(UpdatePhase, () => RunUpdate(world, dt()));
        world.AddSystem(LateUpdatePhase, () => RunLateUpdate(world, dt()));
    }

    public void RunUpdate(World w, float dt) => Guard(() => _current.Update?.Invoke(w, dt));
    public void RunLateUpdate(World w, float dt) => Guard(() => _current.LateUpdate?.Invoke(w, dt));
    public void RunFixedUpdate(World w, float dt) => Guard(() => _current.FixedUpdate?.Invoke(w, dt));

    private void Guard(Action run)
    {
        if (_faulted) return;
        try
        {
            run();
        }
        catch (Exception e)
        {
            // 実行時例外はゲームを止めない — 記録して以後 reload まで停止 (毎フレーム例外を吐かない)
            _faulted = true;
            RuntimeException = e;
            _log?.Invoke($"script runtime error: {e.Message}");
        }
    }

    private static ScriptSystems? Extract(object? value) => value switch
    {
        ScriptSystems s => s,
        Action<World, float> u => new ScriptSystems(Update: u),
        _ => null,
    };

    private static string FirstError(ScriptResult r)
        => r.Exception?.Message
           ?? System.Linq.Enumerable.FirstOrDefault(r.Diagnostics, d => d.IsError)?.Message
           ?? "not a system descriptor";

    public void Dispose() => _source.Changed -= OnSourceChanged;
}
