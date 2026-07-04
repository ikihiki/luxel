using System.Collections.Concurrent;

namespace Luxel.Diagnostics;

/// <summary>
/// 操作レジストリ + 受信キュー。エンジン/アプリが <see cref="Register(string, Func{object, object})"/> で操作を「登録だけ」し、
/// リスナー(別プロジェクト)が任意スレッドから <see cref="Enqueue"/> で要求、app スレッドが
/// 毎フレーム <see cref="Drain"/> で実行する (UI 状態の単一書込者を保つ)。
/// 一方向の DiagnosticListener を補完する入力(browser→engine)経路。
/// 引数 <c>arg</c> は呼出側が決める任意 (DevTools は <see cref="System.Text.Json.JsonElement"/> を渡す)。
/// </summary>
public sealed class EngineCommands
{
    private readonly Dictionary<string, Func<object?, object?>> _handlers = new();
    private readonly ConcurrentQueue<(string name, object? arg)> _inbound = new();

    /// <summary>操作を登録する (戻り値あり)。同名は上書き。</summary>
    public void Register(string name, Func<object?, object?> handler) => _handlers[name] = handler;

    /// <summary>操作を登録する (副作用のみ)。</summary>
    public void Register(string name, Action<object?> handler)
        => _handlers[name] = a => { handler(a); return null; };

    /// <summary>登録済み操作名。</summary>
    public IReadOnlyCollection<string> Names => _handlers.Keys;
    public bool Has(string name) => _handlers.ContainsKey(name);

    /// <summary>リスナーが呼ぶ (任意スレッド)。実行は app スレッドの <see cref="Drain"/> まで遅延。</summary>
    public void Enqueue(string name, object? arg) => _inbound.Enqueue((name, arg));

    /// <summary>app スレッドが毎フレーム呼ぶ。溜まった操作を順に実行する。実行件数を返す。
    /// throw した操作は報告して読み飛ばす (エラー境界 — 1 コマンドの失敗でループを落とさない)。</summary>
    public int Drain()
    {
        int n = 0;
        while (_inbound.TryDequeue(out (string name, object? arg) c))
        {
            try { Invoke(c.name, c.arg); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[cmd-error] {c.name}: {ex.GetType().Name}: {ex.Message}");
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Input))
                    EngineDiagnostics.Emit(EngineDiagnostics.Input, new DiagInput("error", $"cmd {c.name}: {ex.Message}"));
            }
            n++;
        }
        return n;
    }

    /// <summary>同期実行 (app スレッドから)。未知操作は無視し null を返す。</summary>
    public object? Invoke(string name, object? arg)
        => _handlers.TryGetValue(name, out Func<object?, object?>? h) ? h(arg) : null;
}
