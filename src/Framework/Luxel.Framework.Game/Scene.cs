using System.Diagnostics;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Luxel.Audio;
using Luxel.Diagnostics;
using Luxel.Ecs;
using Luxel.Input;
using Luxel.Resources;
using LuxelRG = Luxel.Graphics.RenderGraph;

namespace Luxel.Framework.Game;

// ==================== IScene (最小契約) ====================

/// <summary>
/// Scene の最小契約。engine は起動時に <see cref="RunAsync"/> を呼ぶだけで、ゲームループ本体は Scene が所有する。
/// カスタム描画・タイミング制御を全て自作したい場合はこの Interface を直接実装する。
/// 通常は <see cref="GameScene"/> を継承する。
/// </summary>
public interface IScene
{
    Task OnLoadAsync() => Task.CompletedTask;
    /// <summary>Scene のゲームループ。token がキャンセルされるまで走る。</summary>
    Task RunAsync(CancellationToken token);
    Task OnUnloadAsync() => Task.CompletedTask;
}

// ==================== Phase context (per-phase 型) ====================

public readonly record struct EarlyUpdateContext(FrameTime Time);
/// <summary>FixedUpdate 用の context。可変 dt (<c>Time.DeltaSeconds</c>) を渡さないのは意図的 —
/// 固定ステップ内で可変 dt を読むと決定性が壊れる。刻みは <paramref name="FixedDeltaSeconds"/> のみ。</summary>
public readonly record struct FixedUpdateContext(long Frame, double TotalSeconds, float FixedDeltaSeconds);
public readonly record struct UpdateContext(FrameTime Time);
public readonly record struct LateUpdateContext(FrameTime Time);
public readonly record struct PreRenderContext(FrameTime Time, LuxelRG.RenderGraph RenderGraph);
public readonly record struct RenderContext(FrameTime Time, LuxelRG.RenderGraph RenderGraph);
public readonly record struct PostRenderContext(FrameTime Time);

// ==================== SceneLoopServices (loop 依存注入用) ====================

/// <summary>
/// <see cref="GameScene"/> のゲームループが必要とする外部サービスの束。
/// Framework が DI で組み立て、Scene の ctor に渡す。
/// <para><paramref name="WaitFrame"/> は**フレームペーシングの差し込み点** (Platform 部分の抽象)。
/// null = 既定の固定ディレイ (<paramref name="FrameDelayMs"/>)。Storybook 等の埋め込みホストは
/// 自分の描画ティックに同期する waiter を渡す — TCS の同期継続で完了させると、
/// アプリの 1 フレームがホストのスレッド上で同期実行される (GPU キュー共有が安全になる)。</para>
/// </summary>
public sealed record SceneLoopServices(
    GpuDevice Device,
    ResourceSystem? Resources = null,
    AudioMixer? Mixer = null,
    InputBus? InputBus = null,
    InputStack? InputStack = null,
    IReadOnlyList<IInputSource>? InputSources = null,
    Luxel.Diagnostics.EngineCommands? Commands = null,
    AudioRegistry? AudioRegistry = null,
    Luxel.UI.UiRegistry? UiRegistry = null,
    int FrameDelayMs = 16,
    Func<CancellationToken, Task>? WaitFrame = null,
    Func<int>? PumpGraphicsLifecycle = null);

// ==================== GameScene (標準抽象) ====================

/// <summary>
/// 標準的な動作をする抽象 Scene。<see cref="RunAsync"/> にゲームループの while が入り、以下を一括管理する:
/// <list type="bullet">
/// <item>FrameTime の更新</item>
/// <item>Input source の Poll / InputStack.Update</item>
/// <item>7 Phase (EarlyUpdate / FixedUpdate / Update / LateUpdate / PreRender / Render / PostRender) 順の実行
///       ─ virtual フック + 登録 World の <see cref="World.RunPhase"/></item>
/// <item>RenderGraph の生成 / Execute / Submit</item>
/// <item>ResourceSystem.Pump / AudioMixer.Tick</item>
/// </list>
///
/// 継承 Scene は phase ごとの virtual メソッド (例 <see cref="OnUpdate"/> / <see cref="OnRender"/>) を override するだけでよい。
/// ECS-heavy な処理は <see cref="AddWorld"/> で登録した World の system として組み込む。
/// </summary>
public abstract class GameScene : IScene
{
    private readonly SceneLoopServices _loop;
    private readonly List<World> _worlds = new();
    private readonly List<UiSurface> _surfaces = new();

    protected GameScene(SceneLoopServices loop) => _loop = loop;

    private bool _perfMonitorInstalled;

    protected SceneLoopServices Loop => _loop;
    protected GpuDevice Device => _loop.Device;

    /// <summary>World を登録する。<see cref="RunAsync"/> が各 phase で <see cref="World.RunPhase"/> を呼ぶ。</summary>
    protected void AddWorld(World world)
    {
        if (!_worlds.Contains(world)) _worlds.Add(world);
    }

    private static readonly string[] AllPhaseNames = new[]
    {
        Phase.EarlyUpdate.Name, Phase.FixedUpdate.Name, Phase.Update.Name, Phase.LateUpdate.Name,
        Phase.PreRender.Name,   Phase.Render.Name,      Phase.PostRender.Name,
    };

    /// <summary><see cref="UiSurface"/> を登録する。RunAsync が Render phase で RateHz に従い各 surface の Draw pass を RG に追加する。</summary>
    protected void AddSurface(UiSurface surface)
    {
        if (!_surfaces.Contains(surface)) _surfaces.Add(surface);
    }

    /// <summary>登録済 surface (読み取り専用) ─ user の OnRender が composite などで参照する。</summary>
    protected IReadOnlyList<UiSurface> Surfaces => _surfaces;

    // ==================== 固定タイムステップ設定 (user override) ====================

    /// <summary>FixedUpdate の固定刻み (秒)。既定 1/60。物理と揃えるとゲームロジックと物理が同じ刻みで進む。</summary>
    protected virtual double FixedDeltaSeconds => 1.0 / 60;
    /// <summary>1 フレームで回す FixedUpdate の上限 (spiral of death 防止)。既定 8。</summary>
    protected virtual int MaxFixedStepsPerFrame => 8;

    private FixedTimestep? _fixed;

    /// <summary>描画補間係数 [0,1) — 直近の FixedUpdate から次の固定ステップまでの進捗。
    /// Render/PreRender で <c>lerp(prev, curr, Alpha)</c> に使う (<see cref="TransformInterpolationSystem"/>)。</summary>
    protected float Alpha { get; private set; }

    // ==================== Phase virtual フック (user override) ====================
    protected virtual void OnEarlyUpdate(EarlyUpdateContext ctx) { }
    /// <summary>固定タイムステップのシミュレーション。1 フレームで 0 回以上呼ばれ、dt は常に <c>FixedDeltaSeconds</c>。</summary>
    protected virtual void OnFixedUpdate(FixedUpdateContext ctx) { }
    protected virtual void OnUpdate(UpdateContext ctx) { }
    protected virtual void OnLateUpdate(LateUpdateContext ctx) { }
    protected virtual void OnPreRender(PreRenderContext ctx) { }
    protected virtual void OnRender(RenderContext ctx) { }
    protected virtual void OnPostRender(PostRenderContext ctx) { }

    public virtual Task OnLoadAsync() => Task.CompletedTask;
    public virtual Task OnUnloadAsync() => Task.CompletedTask;

    // ==================== ゲームループ ====================

    // pause/step: DevTools から /cmd で切り替えられる
    private volatile bool _paused;
    private volatile int _stepRequests;
    // timescale: dt に掛ける係数 (0.1 でスローモーション、既定 1)。app スレッドのみが読み書き。
    // play/e2e は使わない (決定性は固定 dt が担保、これは手動デバッグ専用)。
    private double _timeScale = 1.0;
    // ECS インスペクタ: 詳細を出す選択 entity (未選択は -1) と一覧フィルタ (null は全件)。
    private int _ecsSelWorld = -1;
    private int _ecsSelEntity = -1;
    private string? _ecsFilter;

    public async Task RunAsync(CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        var phaseSw = new Stopwatch();
        double prev = 0;
        long frame = 0;
        double fpsAvg = 0;
        int ecsEmitCounter = 0;   // Ecs snapshot は emit コスト高 → 30 フレームに 1 回
        _fixed = new FixedTimestep(FixedDeltaSeconds, MaxFixedStepsPerFrame);

        RegisterCommands();

        while (!token.IsCancellationRequested)
        {
            // フレームペーシング (Platform 部分の差し込み点) — ループ先頭で待つ:
            // 埋め込みホスト (Storybook 等) の waiter を同期継続で完了させると、フレーム本体が
            // ホストのスレッド上で実行される (初回フレームも待ってから = 起動直後のスレッド競合なし)
            try { await (_loop.WaitFrame?.Invoke(token) ?? Task.Delay(_loop.FrameDelayMs, token)); }
            catch (OperationCanceledException) { break; }

            // pause 中は step 要求が来るまでフレームをスキップしてコマンドを Drain し続ける
            _loop.Commands?.Drain();
            _loop.PumpGraphicsLifecycle?.Invoke();
            if (_paused && _stepRequests <= 0)
            {
                // pause でも state emit は続けて DevTools UI を最新に保つ
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.EngineState))
                    EngineDiagnostics.Emit(EngineDiagnostics.EngineState, new DiagEngineState(true));
                continue;
            }
            if (_stepRequests > 0) _stepRequests--;

            double now = sw.Elapsed.TotalSeconds;
            float dt = FixedTimestep.ScaleDt(now - prev, _timeScale);
            prev = now;
            var time = new FrameTime(frame++, dt, now);
            var tick = new UpdateTick(dt, (float)now);

            bool perfEnabled = EngineDiagnostics.IsEnabled(EngineDiagnostics.Perf);
            DiagPhaseTiming[]? phaseTimings = perfEnabled ? new DiagPhaseTiming[7] : null;
            // 初回に system perf monitor を on にする — 診断購読者が居る時のみコスト発生
            if (perfEnabled && !_perfMonitorInstalled)
            {
                foreach (var w in _worlds) w.EnableSystemPerfMonitor();
                _perfMonitorInstalled = true;
            }

            // === Input polling ===
            if (_loop.InputBus is not null && _loop.InputStack is not null)
            {
                if (_loop.InputSources is not null)
                    foreach (var src in _loop.InputSources) src.Poll(_loop.InputBus);
                _loop.InputStack.Update(_loop.InputBus);
            }

            // === EarlyUpdate ===
            phaseSw.Restart();
            OnEarlyUpdate(new EarlyUpdateContext(time));
            RunWorlds(Phase.EarlyUpdate.Name, tick);
            if (phaseTimings is not null) phaseTimings[0] = new DiagPhaseTiming("EarlyUpdate", phaseSw.Elapsed.TotalMilliseconds);

            // === FixedUpdate (蓄積器で溜まった分だけ固定刻みで回す) ===
            phaseSw.Restart();
            int fixedSteps = _fixed!.Advance(dt);
            if (fixedSteps > 0)
            {
                var fixedTick = new UpdateTick((float)_fixed.FixedDt, (float)now);
                for (int i = 0; i < fixedSteps; i++)
                {
                    OnFixedUpdate(new FixedUpdateContext(time.Frame, time.TotalSeconds, (float)_fixed.FixedDt));
                    RunWorlds(Phase.FixedUpdate.Name, fixedTick);
                }
            }
            Alpha = _fixed.Alpha;
            if (phaseTimings is not null) phaseTimings[1] = new DiagPhaseTiming("FixedUpdate", phaseSw.Elapsed.TotalMilliseconds);

            // === Update / LateUpdate ===
            phaseSw.Restart();
            OnUpdate(new UpdateContext(time));
            RunWorlds(Phase.Update.Name, tick);
            if (phaseTimings is not null) phaseTimings[2] = new DiagPhaseTiming("Update", phaseSw.Elapsed.TotalMilliseconds);

            phaseSw.Restart();
            OnLateUpdate(new LateUpdateContext(time));
            RunWorlds(Phase.LateUpdate.Name, tick);
            if (phaseTimings is not null) phaseTimings[3] = new DiagPhaseTiming("LateUpdate", phaseSw.Elapsed.TotalMilliseconds);

            // === PreRender / Render (RenderGraph 生成～Submit) ===
            using (var rg = new LuxelRG.RenderGraph(_loop.Device))
            {
                phaseSw.Restart();
                OnPreRender(new PreRenderContext(time, rg));
                RunWorlds(Phase.PreRender.Name, tick);
                AddSurfacePasses(rg, time);
                if (phaseTimings is not null) phaseTimings[4] = new DiagPhaseTiming("PreRender", phaseSw.Elapsed.TotalMilliseconds);

                phaseSw.Restart();
                OnRender(new RenderContext(time, rg));
                RunWorlds(Phase.Render.Name, tick);

                using var cmd = _loop.Device.MainQueue.StartCommandRecording();
                rg.Execute(cmd);
                cmd.Finish();
                _loop.Device.MainQueue.SubmitAndWait(cmd);
                if (phaseTimings is not null) phaseTimings[5] = new DiagPhaseTiming("Render", phaseSw.Elapsed.TotalMilliseconds);
            }

            // === PostRender ===
            phaseSw.Restart();
            OnPostRender(new PostRenderContext(time));
            RunWorlds(Phase.PostRender.Name, tick);
            if (phaseTimings is not null) phaseTimings[6] = new DiagPhaseTiming("PostRender", phaseSw.Elapsed.TotalMilliseconds);

            // === Pump ===
            _loop.Resources?.Pump();
            _loop.Mixer?.Tick();

            // === Diagnostics emit ===
            double frameMs = (sw.Elapsed.TotalSeconds - now) * 1000 + dt * 1000;
            fpsAvg = fpsAvg == 0 ? (1.0 / dt) : fpsAvg * 0.9 + (1.0 / dt) * 0.1;
            if (perfEnabled && phaseTimings is not null)
            {
                var systems = new List<DiagSystemTiming>();
                foreach (var phaseName in AllPhaseNames)
                    foreach (var w in _worlds)
                        foreach (var (name, ms) in w.CollectSystemTimings(phaseName))
                            systems.Add(new DiagSystemTiming(phaseName, name, ms));
                var fixedStat = new DiagFixedStep(
                    fixedSteps, _fixed.Alpha, _fixed.Accumulator * 1000.0, _fixed.FixedDt * 1000.0, _fixed.DroppedSteps);
                EngineDiagnostics.Emit(EngineDiagnostics.Perf,
                    new DiagPerf(time.Frame, dt * 1000.0, fpsAvg, phaseTimings, systems.ToArray(), fixedStat));
            }
            // ECS / Surfaces / Audio / Runtime / UI Trees スナップショットは 30 フレームに 1 回 (≒ 0.5s @ 60fps)
            if (++ecsEmitCounter >= 30)
            {
                ecsEmitCounter = 0;
                // ECS: 一覧は軽量サマリ (常時)、詳細は選択 entity のみ (未選択かつ小規模なら全量フォールバック)
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.EcsSummary))
                    EngineDiagnostics.Emit(EngineDiagnostics.EcsSummary, EcsDiagnostics.BuildSummary(_worlds, _ecsFilter));
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Ecs))
                    EngineDiagnostics.Emit(EngineDiagnostics.Ecs, EcsDiagnostics.BuildDetail(_worlds, _ecsSelWorld, _ecsSelEntity));
                DevStats.Flush();   // ゲーム側カスタム統計 (self-guard: 購読者ゼロなら no-op)
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Surfaces)) EmitSurfacesSnapshot();
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Audio)) EmitAudioSnapshot();
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Runtime)) EmitRuntimeSnapshot();
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Trees)) EmitUiTreesSnapshot();
            }
            // InputState は毎フレーム変わりうるので即時 emit
            if (EngineDiagnostics.IsEnabled(EngineDiagnostics.InputState))
                EmitInputStateSnapshot();
            if (EngineDiagnostics.IsEnabled(EngineDiagnostics.EngineState))
                EngineDiagnostics.Emit(EngineDiagnostics.EngineState, new DiagEngineState(_paused));
        }
    }

    private void RegisterCommands()
    {
        var cmds = _loop.Commands;
        if (cmds is null) return;
        cmds.Register("engine.pause", _ => _paused = true);
        cmds.Register("engine.resume", _ => _paused = false);
        cmds.Register("engine.step", _ => { _paused = true; _stepRequests++; });
        cmds.Register("engine.timescale", arg => HandleTimeScale(arg));
        cmds.Register("ecs.set", arg => HandleEcsSet(arg));
        cmds.Register("ecs.inspect", arg => HandleEcsInspect(arg));
        cmds.Register("ecs.filter", arg => HandleEcsFilter(arg));
        cmds.Register("gizmo.enable", arg => HandleGizmoEnable(arg));
        cmds.Register("ui.set", arg => HandleUiSet(arg));
    }

    /// <summary>timescale 変更。<c>{ op:"engine.timescale", value:0.5 }</c>。[0,8] にクランプ。</summary>
    private void HandleTimeScale(object? arg)
    {
        if (arg is System.Text.Json.JsonElement el
            && el.TryGetProperty("value", out var v) && v.TryGetDouble(out double s))
            _timeScale = Math.Clamp(s, 0.0, 8.0);
    }

    /// <summary>ECS 詳細の対象 entity を選択。<c>{ op:"ecs.inspect", world:0, entityId:1 }</c>。entityId&lt;0 で選択解除。</summary>
    private void HandleEcsInspect(object? arg)
    {
        if (arg is not System.Text.Json.JsonElement el) return;
        int world = el.TryGetProperty("world", out var w) && w.TryGetInt32(out int wi) ? wi : 0;
        int entity = el.TryGetProperty("entityId", out var e) && e.TryGetInt32(out int ei) ? ei : -1;
        _ecsSelWorld = world;
        _ecsSelEntity = entity;
    }

    /// <summary>ECS 一覧フィルタ。<c>{ op:"ecs.filter", text:"Enemy" }</c>。空/未指定で解除。</summary>
    private void HandleEcsFilter(object? arg)
    {
        if (arg is not System.Text.Json.JsonElement el) { _ecsFilter = null; return; }
        string? text = el.TryGetProperty("text", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String
            ? t.GetString() : null;
        _ecsFilter = string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>gizmo カテゴリの ON/OFF。<c>{ op:"gizmo.enable", kind:"physics", on:true }</c>。
    /// kind 省略時は <c>"all"</c>、on 省略時は true。実際に描くかはゲーム側の <see cref="Luxel.Graphics.TwoD.DebugDraw"/> flush 次第。</summary>
    private static void HandleGizmoEnable(object? arg)
    {
        if (arg is not System.Text.Json.JsonElement el) { Luxel.Graphics.TwoD.DebugDraw.Enable(Luxel.Graphics.TwoD.DebugDraw.All); return; }
        string kind = el.TryGetProperty("kind", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.String
            ? k.GetString() ?? Luxel.Graphics.TwoD.DebugDraw.All : Luxel.Graphics.TwoD.DebugDraw.All;
        bool on = !el.TryGetProperty("on", out var o) || o.ValueKind != System.Text.Json.JsonValueKind.False;
        Luxel.Graphics.TwoD.DebugDraw.SetEnabled(kind, on);
    }

    /// <summary>
    /// DevTools からの component 値書き換え。arg は JSON:
    /// <c>{ op:"ecs.set", world:0, entityId:1, componentType:"LocalTransform", path:"Matrix.M11", value:1.5 }</c>
    /// path は "." 区切りの nested field 名。value は number / bool / string。
    /// </summary>
    private void HandleEcsSet(object? arg)
    {
        if (arg is not System.Text.Json.JsonElement el) return;
        try
        {
            int worldIdx = el.GetProperty("world").GetInt32();
            int entityId = el.GetProperty("entityId").GetInt32();
            string typeNm = el.GetProperty("componentType").GetString() ?? "";
            string path = el.GetProperty("path").GetString() ?? "";
            System.Text.Json.JsonElement valEl = el.GetProperty("value");

            if (worldIdx < 0 || worldIdx >= _worlds.Count) return;
            var world = _worlds[worldIdx];
            var entity = world.Store.GetEntityById(entityId);
            if (entity.IsNull) return;

            // Component type を short name で検索 (LocalTransform / Color3D 等)
            System.Type? compType = null;
            foreach (var c in entity.Components)
                if (c.Type.Type.Name == typeNm) { compType = c.Type.Type; break; }
            if (compType is null) return;

            // 現在の component を boxed で取得
            var getMethod = typeof(Friflo.Engine.ECS.Entity).GetMethod(
                nameof(Friflo.Engine.ECS.Entity.GetComponent), System.Type.EmptyTypes);
            if (getMethod is null) return;
            object comp = getMethod.MakeGenericMethod(compType).Invoke(entity, null)!;

            // path を辿って値を書き換え (nested ValueType はスタック走査 + 逆伝搬)
            if (!TryWalkAndSet(comp, path.Split('.'), valEl))
                return;

            // AddComponent<T>(comp) で書き戻す
            var addMethod = typeof(Friflo.Engine.ECS.Entity).GetMethods()
                .FirstOrDefault(m => m.Name == nameof(Friflo.Engine.ECS.Entity.AddComponent)
                                     && m.IsGenericMethodDefinition
                                     && m.GetParameters().Length == 1);
            addMethod?.MakeGenericMethod(compType).Invoke(entity, new[] { comp });
        }
        catch { /* 不正 payload は無視 */ }
    }

    /// <summary>
    /// UI プロパティ書き換え。arg は JSON:
    /// <c>{ op:"ui.set", path:"0.1.2", name:"color", value:"#ff0080" }</c>
    /// <para>path は tree の dotted-index (root=0 起点)。実装は UI runtime の hook が必要 —
    /// 現状 DevTools の書き戻し API は「イベントを DiagnosticListener に再 emit する」だけで
    /// user code (UiHost owner) が購読して反映する。</para>
    /// </summary>
    private void HandleUiSet(object? arg)
    {
        if (arg is not System.Text.Json.JsonElement el) return;
        // ui.set は Runtime hook を持つ user 実装が拾えるよう Diagnostics に転送する。
        // Luxel.UI 側で購読して Widget を書き換える responsibility はコアではなく統合レイヤ側にある。
        EngineDiagnostics.Emit("Luxel.UiSetRequest", el);
    }

    private static bool TryWalkAndSet(object root, string[] path, System.Text.Json.JsonElement valEl)
    {
        // 各階層で FieldInfo を取り、末端フィールドに coerce した値をセット。
        // 途中の ValueType は boxed object を経由するので SetValue 後に親へ返す必要がある。
        var stack = new System.Collections.Generic.List<(object obj, System.Reflection.FieldInfo f)>();
        object current = root;
        for (int i = 0; i < path.Length - 1; i++)
        {
            var f = current.GetType().GetField(path[i], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (f is null) return false;
            var next = f.GetValue(current);
            if (next is null) return false;
            stack.Add((current, f));
            current = next;
        }
        var leaf = current.GetType().GetField(path[^1], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (leaf is null) return false;
        object? coerced = CoerceValue(valEl, leaf.FieldType);
        if (coerced is null && leaf.FieldType.IsValueType) return false;
        leaf.SetValue(current, coerced);
        // 逆順に伝搬 (ValueType の box 差し戻し)
        for (int i = stack.Count - 1; i >= 0; i--)
            stack[i].f.SetValue(stack[i].obj, current);
        return true;
    }

    private static object? CoerceValue(System.Text.Json.JsonElement el, System.Type target)
    {
        if (target == typeof(float)) return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetSingle() : float.TryParse(el.GetString(), out var v) ? v : 0f;
        if (target == typeof(double)) return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetDouble() : 0.0;
        if (target == typeof(int)) return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetInt32() : 0;
        if (target == typeof(uint)) return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetUInt32() : 0u;
        if (target == typeof(long)) return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetInt64() : 0L;
        if (target == typeof(bool)) return el.ValueKind == System.Text.Json.JsonValueKind.True || (el.ValueKind == System.Text.Json.JsonValueKind.False ? false : el.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b);
        if (target == typeof(string)) return el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : el.ToString();
        return null;   // 非対応型 (Matrix4x4 全体を差し替える等は path 経由で M11 単位に細分するべき)
    }

    /// <summary>InputStack 上の全 context / action のスナップショットを Diagnostics に emit する。</summary>
    private void EmitInputStateSnapshot()
    {
        var stack = _loop.InputStack;
        if (stack is null) return;
        var ctxs = stack.Contexts;
        var out_ = new DiagInputContext[ctxs.Count];
        // Contexts は下位→上位。Push 順そのまま。上位を先に見せたいので逆順にする。
        for (int i = 0; i < ctxs.Count; i++)
        {
            var ctx = ctxs[ctxs.Count - 1 - i];
            var acts = new DiagInputAction[ctx.Actions.Count];
            for (int j = 0; j < ctx.Actions.Count; j++)
            {
                var a = ctx.Actions[j];
                (string kind, string val) = a switch
                {
                    ButtonAction b => ("Button", b.Value.Value ? "true" : "false"),
                    Axis1DAction a1 => ("Axis1D", a1.Value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                    Axis2DAction a2 => ("Axis2D", $"{{\"X\":{a2.Value.Value.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},\"Y\":{a2.Value.Value.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}}}"),
                    _ => (a.GetType().Name, "\"?\""),
                };
                acts[j] = new DiagInputAction(a.Name, kind, a.IsActive.Value, val);
            }
            // Suspended は InputStack 内部で管理されており外から取得できないため、ここでは false 固定 (将来 API 拡張時に対応)
            out_[i] = new DiagInputContext(ctx.Name, false, acts);
        }
        EngineDiagnostics.Emit(EngineDiagnostics.InputState, new DiagInputState(out_));
    }

    /// <summary>UiRegistry に登録された全 UiHost の tree snapshot を配列で emit する。
    /// 各 host の <see cref="UiSurfaceKind"/>/placement は host 側では持たないので "Screen" 固定。
    /// (Scene が抱える UiSurface と結び付けたい場合は user が name 命名で区別する。)</summary>
    private void EmitUiTreesSnapshot()
    {
        var reg = _loop.UiRegistry;
        if (reg is null) return;
        var snap = reg.Snapshot();
        var entries = new Luxel.UI.DebugTreeEntry[snap.Length];
        for (int i = 0; i < snap.Length; i++)
        {
            var (name, root) = snap[i];
            entries[i] = new Luxel.UI.DebugTreeEntry(name, "Screen", root, (int)(root?.W ?? 0), (int)(root?.H ?? 0));
        }
        EngineDiagnostics.Emit(EngineDiagnostics.Trees, new Luxel.UI.DebugTreeSet(entries));
    }

    /// <summary>.NET ランタイム状態 (GC / Memory / Threads) を Diagnostics に emit する。</summary>
    private static void EmitRuntimeSnapshot()
    {
        long mem = GC.GetTotalMemory(forceFullCollection: false);
        int workingMB = (int)(Environment.WorkingSet / (1024 * 1024));
        EngineDiagnostics.Emit(EngineDiagnostics.Runtime, new DiagRuntime(
            TotalMemoryBytes: mem,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            ThreadCount: System.Diagnostics.Process.GetCurrentProcess().Threads.Count,
            WorkingSetMB: workingMB));
    }

    /// <summary>登録 AudioBus / AudioSource + Mixer 状態のスナップショットを Diagnostics に emit する。</summary>
    private void EmitAudioSnapshot()
    {
        var reg = _loop.AudioRegistry;
        if (reg is null) return;
        var buses = new DiagAudioBus[reg.Buses.Count];
        for (int i = 0; i < reg.Buses.Count; i++)
        {
            var b = reg.Buses[i];
            buses[i] = new DiagAudioBus(b.Name, b.Parent?.Name, b.Volume.Peek(), b.EffectiveVolume);
        }
        var srcs = new DiagAudioSource[reg.Sources.Count];
        for (int i = 0; i < reg.Sources.Count; i++)
        {
            var (name, s) = reg.Sources[i];
            srcs[i] = new DiagAudioSource(
                name, s.IsPlaying, s.Volume.Peek(), s.Pitch.Peek(), s.Pan.Peek(), s.Bus?.Name);
        }
        int active = reg.Mixer?.ActiveVoiceCount ?? 0;
        EngineDiagnostics.Emit(EngineDiagnostics.Audio, new DiagAudio(active, buses, srcs));
    }

    /// <summary>登録 UiSurface のメタと累積描画状況を Diagnostics に emit する。</summary>
    private void EmitSurfacesSnapshot()
    {
        var arr = new DiagSurface[_surfaces.Count];
        for (int i = 0; i < _surfaces.Count; i++)
        {
            var s = _surfaces[i];
            arr[i] = new DiagSurface(
                s.Name, s.Kind.ToString(), (int)s.Width, (int)s.Height, s.Format.ToString(),
                s.RateHz, s.RenderedFrames, s.LastRenderedTime, s.NeedsRedraw);
        }
        EngineDiagnostics.Emit(EngineDiagnostics.Surfaces, new DiagSurfaces(arr));
    }

    private void RunWorlds(string phase, UpdateTick tick)
    {
        foreach (var w in _worlds) w.RunPhase(phase, tick);
    }

    private void AddSurfacePasses(LuxelRG.RenderGraph rg, FrameTime time)
    {
        foreach (var s in _surfaces)
        {
            if (!s.ShouldRender(time)) continue;
            var handle = rg.ImportTexture(s.Target, s.Name);
            var captured = s;
            var capturedTime = time;
            rg.AddPass($"ui:{s.Name}", LuxelRG.PassQueue.Graphics).Write(handle)
              .Execute(pctx =>
                  captured.Draw?.Invoke(new SurfaceDrawContext(pctx, handle, captured, capturedTime)));
            s.LastRenderedTime = time.TotalSeconds;
            s.RenderedFrames++;
            s.NeedsRedraw = false;
        }
    }
}
