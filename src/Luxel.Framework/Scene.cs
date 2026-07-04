using System.Diagnostics;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Luxel.Audio;
using Luxel.Diagnostics;
using Luxel.Ecs;
using Luxel.Input;
using Luxel.Resources;
using LuxelRG = Luxel.RenderGraph;

namespace Luxel.Framework;

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
public readonly record struct UpdateContext(FrameTime Time);
public readonly record struct LateUpdateContext(FrameTime Time);
public readonly record struct PreRenderContext(FrameTime Time, LuxelRG.RenderGraph RenderGraph);
public readonly record struct RenderContext(FrameTime Time, LuxelRG.RenderGraph RenderGraph);
public readonly record struct PostRenderContext(FrameTime Time);

// ==================== SceneLoopServices (loop 依存注入用) ====================

/// <summary>
/// <see cref="GameScene"/> のゲームループが必要とする外部サービスの束。
/// Framework が DI で組み立て、Scene の ctor に渡す。
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
    int FrameDelayMs = 16);

// ==================== GameScene (標準抽象) ====================

/// <summary>
/// 標準的な動作をする抽象 Scene。<see cref="RunAsync"/> にゲームループの while が入り、以下を一括管理する:
/// <list type="bullet">
/// <item>FrameTime の更新</item>
/// <item>Input source の Poll / InputStack.Update</item>
/// <item>6 Phase (EarlyUpdate / Update / LateUpdate / PreRender / Render / PostRender) 順の実行
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
        Phase.EarlyUpdate.Name, Phase.Update.Name, Phase.LateUpdate.Name,
        Phase.PreRender.Name,   Phase.Render.Name, Phase.PostRender.Name,
    };

    /// <summary><see cref="UiSurface"/> を登録する。RunAsync が Render phase で RateHz に従い各 surface の Draw pass を RG に追加する。</summary>
    protected void AddSurface(UiSurface surface)
    {
        if (!_surfaces.Contains(surface)) _surfaces.Add(surface);
    }

    /// <summary>登録済 surface (読み取り専用) ─ user の OnRender が composite などで参照する。</summary>
    protected IReadOnlyList<UiSurface> Surfaces => _surfaces;

    // ==================== Phase virtual フック (user override) ====================
    protected virtual void OnEarlyUpdate(EarlyUpdateContext ctx) { }
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

    public async Task RunAsync(CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        var phaseSw = new Stopwatch();
        double prev = 0;
        long frame = 0;
        double fpsAvg = 0;
        int ecsEmitCounter = 0;   // Ecs snapshot は emit コスト高 → 30 フレームに 1 回

        RegisterCommands();

        while (!token.IsCancellationRequested)
        {
            // pause 中は step 要求が来るまで busy-wait せず短いスリープでコマンドを Drain し続ける
            _loop.Commands?.Drain();
            if (_paused && _stepRequests <= 0)
            {
                // pause でも state emit は続けて DevTools UI を最新に保つ
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.EngineState))
                    EngineDiagnostics.Emit(EngineDiagnostics.EngineState, new DiagEngineState(true));
                try { await Task.Delay(20, token); } catch (OperationCanceledException) { break; }
                continue;
            }
            if (_stepRequests > 0) _stepRequests--;

            double now = sw.Elapsed.TotalSeconds;
            float dt = (float)Math.Clamp(now - prev, 0.0001, 0.1);
            prev = now;
            var time = new FrameTime(frame++, dt, now);
            var tick = new UpdateTick(dt, (float)now);

            bool perfEnabled = EngineDiagnostics.IsEnabled(EngineDiagnostics.Perf);
            DiagPhaseTiming[]? phaseTimings = perfEnabled ? new DiagPhaseTiming[6] : null;
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

            // === EarlyUpdate / Update / LateUpdate ===
            phaseSw.Restart();
            OnEarlyUpdate(new EarlyUpdateContext(time));
            RunWorlds(Phase.EarlyUpdate.Name, tick);
            if (phaseTimings is not null) phaseTimings[0] = new DiagPhaseTiming("EarlyUpdate", phaseSw.Elapsed.TotalMilliseconds);

            phaseSw.Restart();
            OnUpdate(new UpdateContext(time));
            RunWorlds(Phase.Update.Name, tick);
            if (phaseTimings is not null) phaseTimings[1] = new DiagPhaseTiming("Update", phaseSw.Elapsed.TotalMilliseconds);

            phaseSw.Restart();
            OnLateUpdate(new LateUpdateContext(time));
            RunWorlds(Phase.LateUpdate.Name, tick);
            if (phaseTimings is not null) phaseTimings[2] = new DiagPhaseTiming("LateUpdate", phaseSw.Elapsed.TotalMilliseconds);

            // === PreRender / Render (RenderGraph 生成～Submit) ===
            using (var rg = new LuxelRG.RenderGraph(_loop.Device))
            {
                phaseSw.Restart();
                OnPreRender(new PreRenderContext(time, rg));
                RunWorlds(Phase.PreRender.Name, tick);
                AddSurfacePasses(rg, time);
                if (phaseTimings is not null) phaseTimings[3] = new DiagPhaseTiming("PreRender", phaseSw.Elapsed.TotalMilliseconds);

                phaseSw.Restart();
                OnRender(new RenderContext(time, rg));
                RunWorlds(Phase.Render.Name, tick);

                using var cmd = _loop.Device.MainQueue.StartCommandRecording();
                rg.Execute(cmd);
                cmd.Finish();
                _loop.Device.MainQueue.SubmitAndWait(cmd);
                if (phaseTimings is not null) phaseTimings[4] = new DiagPhaseTiming("Render", phaseSw.Elapsed.TotalMilliseconds);
            }

            // === PostRender ===
            phaseSw.Restart();
            OnPostRender(new PostRenderContext(time));
            RunWorlds(Phase.PostRender.Name, tick);
            if (phaseTimings is not null) phaseTimings[5] = new DiagPhaseTiming("PostRender", phaseSw.Elapsed.TotalMilliseconds);

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
                EngineDiagnostics.Emit(EngineDiagnostics.Perf,
                    new DiagPerf(time.Frame, dt * 1000.0, fpsAvg, phaseTimings, systems.ToArray()));
            }
            // ECS / Surfaces / Audio / Runtime / UI Trees スナップショットは 30 フレームに 1 回 (≒ 0.5s @ 60fps)
            if (++ecsEmitCounter >= 30)
            {
                ecsEmitCounter = 0;
                if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Ecs)) EmitEcsSnapshot();
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

            // フレーム上限
            try { await Task.Delay(_loop.FrameDelayMs, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>登録 World の全 entity と持ち component の値 (JSON) を集めて Diagnostics に emit する。</summary>
    private void EmitEcsSnapshot()
    {
        var worlds = new DiagWorld[_worlds.Count];
        for (int i = 0; i < _worlds.Count; i++)
        {
            var w = _worlds[i];
            var entities = new List<DiagEntity>();
            foreach (var e in w.Store.Entities)
            {
                var comps = new List<DiagComponent>();
                foreach (var c in e.Components)
                {
                    string json;
                    // c.Value は Debugger 表示用に obsolete 指定されているが、DevTools でも同じ用途
                    #pragma warning disable CS0618
                    try { json = System.Text.Json.JsonSerializer.Serialize(c.Value, EcsJsonOptions); }
                    catch (Exception ex) { json = $"\"<serialize failed: {ex.GetType().Name}>\""; }
                    #pragma warning restore CS0618
                    comps.Add(new DiagComponent(c.Type.Type.Name, json));
                }
                var tagTypes = new List<string>();
                foreach (var t in e.Tags) tagTypes.Add(t.Type.Name);
                entities.Add(new DiagEntity(e.Id, comps.ToArray(), tagTypes.ToArray()));
            }
            worlds[i] = new DiagWorld($"World#{i}", w.Store.Count, entities.ToArray());
        }
        EngineDiagnostics.Emit(EngineDiagnostics.Ecs, new DiagEcs(worlds));
    }

    private static readonly System.Text.Json.JsonSerializerOptions EcsJsonOptions = new()
    {
        IncludeFields = true,   // struct field も出す (Matrix4x4/Vector3/Vector4 等の public field)
        WriteIndented = false,
    };

    private void RegisterCommands()
    {
        var cmds = _loop.Commands;
        if (cmds is null) return;
        cmds.Register("engine.pause",  _ => _paused = true);
        cmds.Register("engine.resume", _ => _paused = false);
        cmds.Register("engine.step",   _ => { _paused = true; _stepRequests++; });
        cmds.Register("ecs.set",       arg => HandleEcsSet(arg));
        cmds.Register("ui.set",        arg => HandleUiSet(arg));
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
            int worldIdx  = el.GetProperty("world").GetInt32();
            int entityId  = el.GetProperty("entityId").GetInt32();
            string typeNm = el.GetProperty("componentType").GetString() ?? "";
            string path   = el.GetProperty("path").GetString() ?? "";
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
        if (target == typeof(float))  return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetSingle() : float.TryParse(el.GetString(), out var v) ? v : 0f;
        if (target == typeof(double)) return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetDouble() : 0.0;
        if (target == typeof(int))    return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetInt32()  : 0;
        if (target == typeof(uint))   return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetUInt32() : 0u;
        if (target == typeof(long))   return el.ValueKind == System.Text.Json.JsonValueKind.Number ? el.GetInt64()  : 0L;
        if (target == typeof(bool))   return el.ValueKind == System.Text.Json.JsonValueKind.True || (el.ValueKind == System.Text.Json.JsonValueKind.False ? false : el.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b);
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
    /// 各 host の <see cref="Luxel.UI.UiSurfaceKind"/>/placement は host 側では持たないので "Screen" 固定。
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
            TotalMemoryBytes:  mem,
            Gen0Collections:   GC.CollectionCount(0),
            Gen1Collections:   GC.CollectionCount(1),
            Gen2Collections:   GC.CollectionCount(2),
            ThreadCount:       System.Diagnostics.Process.GetCurrentProcess().Threads.Count,
            WorkingSetMB:      workingMB));
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
