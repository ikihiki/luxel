using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Framework;

/// <summary>
/// Scene 切替を管理する DI-registered service。<see cref="IScene"/> がゲームループを所有する構造なので、
/// SceneManager は「現在の Scene を実行 + 切替リクエストがあれば次に載せ替え」の外側ループを提供する。
/// </summary>
public sealed class SceneManager
{
    private readonly IServiceProvider _services;
    private IScene? _current;
    private IScene? _pending;
    private CancellationTokenSource? _cts;

    public SceneManager(IServiceProvider services) => _services = services;

    public IScene? Current => _current;

    /// <summary>Scene の内側から次 Scene への切替を要求 (fire-and-forget)。現在の Scene の RunAsync が cancel され、
    /// OnUnload → OnLoad → 新 Scene の RunAsync に載せ替わる。</summary>
    public Task SwitchAsync<TScene>() where TScene : class, IScene
    {
        var next = (TScene?)_services.GetService(typeof(TScene))
                   ?? ActivatorUtilities.CreateInstance<TScene>(_services);
        return SwitchAsync(next);
    }

    public Task SwitchAsync(IScene next)
    {
        _pending = next;
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>startup Scene から始めて cancel されるまで Scene を実行し続ける。切替が要求されれば載せ替え。</summary>
    public async Task RunLoopAsync(IScene startup, CancellationToken outerToken)
    {
        _current = startup;
        await _current.OnLoadAsync();

        while (!outerToken.IsCancellationRequested && _current is not null)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            _cts = linked;
            try { await _current.RunAsync(linked.Token); }
            catch (OperationCanceledException) { }
            _cts = null;

            await _current.OnUnloadAsync();
            _current = _pending;
            _pending = null;
            if (_current is not null && !outerToken.IsCancellationRequested)
                await _current.OnLoadAsync();
        }

        if (_current is not null) { await _current.OnUnloadAsync(); _current = null; }
    }
}
