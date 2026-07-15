using System.Diagnostics;
using Luxel.Diagnostics;
using Luxel.Ecs;
using LuxelRG = Luxel.RenderGraph;

namespace Luxel.Framework;

/// <summary>
/// SceneGraph 全体のループを所有する。入力とservice pumpは1フレームに1回、各phaseはActiveなSceneを
/// 親から子の順に実行し、描画passは1つのRenderGraphへ合流させる。
/// </summary>
public sealed class SceneRunner(SceneLoopServices services)
{
    private sealed class FrameEntry
    {
        public FrameEntry(SceneNode node, FrameTime time)
        {
            Node = node;
            Time = time;
        }

        public SceneNode Node { get; }
        public IScene Scene => Node.Scene;
        public FrameTime Time { get; }
        public int FixedSteps { get; set; }
    }

    public async Task RunAsync(SceneManager manager, IScene startup, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var phaseClock = new Stopwatch();
        double previous = 0;

        await manager.InitializeAsync(startup);
        try
        {
            while (!cancellationToken.IsCancellationRequested && manager.Root is not null)
            {
                await manager.ApplyPendingAsync();
                if (manager.Root is null) break;

                try { await services.FrameScheduler.WaitForNextFrameAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }

                // idle中に届いたScene操作を、古いSceneをもう1フレーム動かす前に適用する。
                await manager.ApplyPendingAsync();
                if (manager.Root is null) break;

                services.Commands?.Drain();

                // InputはScene数に関わらず1回だけpollする。OnDemand Sceneはこの結果でdirtyになれる。
                if (services.InputBus is not null && services.InputStack is not null)
                {
                    if (services.InputSources is not null)
                        foreach (var source in services.InputSources) source.Poll(services.InputBus);
                    services.InputStack.Update(services.InputBus);
                }

                double now = clock.Elapsed.TotalSeconds;
                double globalRawDelta = now - previous;
                previous = now;
                List<FrameEntry> entries = BuildFrameEntries(manager.GetActiveNodes(), now, globalRawDelta);

                bool perfEnabled = EngineDiagnostics.IsEnabled(EngineDiagnostics.Perf);
                DiagPhaseTiming[]? phaseTimings = perfEnabled ? new DiagPhaseTiming[7] : null;

                phaseClock.Restart();
                foreach (FrameEntry entry in entries)
                    entry.Scene.EarlyUpdate(new EarlyUpdateContext(entry.Time));
                SetTiming(phaseTimings, 0, "EarlyUpdate", phaseClock);

                phaseClock.Restart();
                foreach (FrameEntry entry in entries)
                    RunFixedUpdate(entry);
                SetTiming(phaseTimings, 1, "FixedUpdate", phaseClock);

                phaseClock.Restart();
                foreach (FrameEntry entry in entries)
                    entry.Scene.Update(new UpdateContext(entry.Time));
                SetTiming(phaseTimings, 2, "Update", phaseClock);

                phaseClock.Restart();
                foreach (FrameEntry entry in entries)
                    entry.Scene.LateUpdate(new LateUpdateContext(entry.Time));
                SetTiming(phaseTimings, 3, "LateUpdate", phaseClock);

                using (var renderGraph = new LuxelRG.RenderGraph(services.Device))
                {
                    phaseClock.Restart();
                    foreach (FrameEntry entry in entries)
                        if (entry.Node.EffectiveRenderMode != SceneRenderMode.Frozen)
                            entry.Scene.PreRender(new PreRenderContext(entry.Time, renderGraph));
                    SetTiming(phaseTimings, 4, "PreRender", phaseClock);

                    phaseClock.Restart();
                    foreach (FrameEntry entry in entries)
                        if (entry.Node.EffectiveRenderMode != SceneRenderMode.Frozen)
                            entry.Scene.Render(new RenderContext(entry.Time, renderGraph));

                    using var cmd = services.Device.MainQueue.StartCommandRecording();
                    renderGraph.Execute(cmd);
                    cmd.Finish();
                    services.Device.MainQueue.SubmitAndWait(cmd);
                    SetTiming(phaseTimings, 5, "Render", phaseClock);
                }

                phaseClock.Restart();
                foreach (FrameEntry entry in entries)
                {
                    entry.Scene.PostRender(new PostRenderContext(entry.Time));
                    entry.Scene.FrameCompleted();
                }
                SetTiming(phaseTimings, 6, "PostRender", phaseClock);

                services.Resources?.Pump();
                services.Mixer?.Tick();

                // 既存diagnosticsはroot GameSceneを代表として維持する。phase時間は子Sceneを含む全体値。
                if (manager.Root?.Scene is GameScene rootGame)
                {
                    FrameEntry? rootEntry = entries.FirstOrDefault(e => ReferenceEquals(e.Node, manager.Root));
                    if (rootEntry is not null)
                        rootGame.EmitRunnerDiagnostics(rootEntry.Time, rootEntry.FixedSteps,
                            rootEntry.Node.FixedTimestep!, phaseTimings);
                }

                // phase中に要求されたGraph変更は、次のwaitへ入る前に確定させる。
                await manager.ApplyPendingAsync();
            }
        }
        finally
        {
            await manager.ShutdownAsync();
        }
    }

    private static List<FrameEntry> BuildFrameEntries(SceneNode[] nodes, double now, double globalRawDelta)
    {
        var entries = new List<FrameEntry>(nodes.Length);
        foreach (SceneNode node in nodes)
        {
            IScene scene = node.Scene;
            if (!ShouldRunNode(node)) continue;

            double rawDelta = node.LastRunAt is double last ? now - last : globalRawDelta;
            node.LastRunAt = now;
            node.TotalSeconds += Math.Max(0, rawDelta);
            double timeScale = scene is GameScene timed ? timed.RunnerTimeScale : 1.0;
            float dt = FixedTimestep.ScaleDt(rawDelta, timeScale);
            var time = new FrameTime(node.Frame++, dt, node.TotalSeconds);
            entries.Add(new FrameEntry(node, time));

            if (scene.UsesFixedUpdate && node.FixedTimestep is null)
                node.FixedTimestep = new FixedTimestep(scene.FixedDeltaSeconds, scene.MaxFixedStepsPerFrame);

            if (scene is GameScene perf) perf.PrepareRunnerDiagnostics();
        }
        return entries;
    }

    /// <summary>
    /// node単位の参加判定。親が今回のframeをskipしてもActiveな子は独立に判定するため、
    /// pauseしたGameSceneへ合成したUI Sceneは引き続き入力・描画を処理できる。
    /// </summary>
    internal static bool ShouldRunNode(SceneNode node)
    {
        IScene scene = node.Scene;
        if (node.EffectiveExecutionMode == SceneExecutionMode.OnDemand && !scene.TryBeginFrame())
            return false;
        return scene is not GameScene game || game.TryBeginRunnerFrame();
    }

    private static void RunFixedUpdate(FrameEntry entry)
    {
        if (!entry.Scene.UsesFixedUpdate || entry.Node.FixedTimestep is not { } fixedStep) return;
        int steps = fixedStep.Advance(entry.Time.DeltaSeconds);
        entry.FixedSteps = steps;
        for (int i = 0; i < steps; i++)
            entry.Scene.FixedUpdate(new FixedUpdateContext(
                entry.Time.Frame, entry.Time.TotalSeconds, (float)fixedStep.FixedDt));
        if (entry.Scene is GameScene game) game.SetRunnerAlpha(fixedStep.Alpha);
    }

    private static void SetTiming(DiagPhaseTiming[]? timings, int index, string name, Stopwatch clock)
    {
        if (timings is not null) timings[index] = new DiagPhaseTiming(name, clock.Elapsed.TotalMilliseconds);
    }
}
