# Game loop, scene, and rendering architecture

## Ownership

- The Host starts exactly one `IGameLoop` through `GameLoopHostedService`.
- The standard `GameLoop` owns the application iteration and obtains one `RenderOpportunity` from `IRenderFrameScheduler`.
- `GameSceneSystem` owns scene lifecycle, ordered slots, state transitions, command commit, and immutable render assignment snapshots.
- `CadenceExecutionCoordinator` evaluates schedules, unions selected Sets, dispatches shared runners, and commits observed generations only after success.
- `RenderGraphCadenceRunner` owns the normal RenderGraph, command recording, GPU submission, and completion boundary.
- `PresentationRunner` owns the direct presentation graph transaction. `IPresentationScheduler` owns backend acquire, present, pacing result handling, and drain.

Scenes and Features never execute a graph, submit a command buffer, acquire a presentation target, or present.

## One iteration

```text
wait for one RenderOpportunity
  -> poll input / update InputStack
  -> FixedUpdate 0..N times
  -> Update once
  -> commit scene commands
  -> capture one immutable RenderSystemFrameSnapshot
  -> execute due normal cadences
  -> execute pending AfterSuccess presentation
  -> pump resources/audio/diagnostics
```

Cleanup drains the coordinator once and then shuts scenes down in reverse slot order.

## Scene lifecycle

`IGameScene` implements `LoadAsync`, `ConfigureRendering`, `FixedUpdate`, `Update`, and `UnloadAsync`. Initial scenes are enqueued by `IGameSceneBootstrap`; the Host does not know a scene type.

Scene commands are committed only at frame boundaries:

- `Push`
- `Replace`
- `Remove`
- `SetState` (`Running`, `Paused`, `Sleeping`)
- `SetPolicy` for input/focus policy
- `RebuildAssignments`

A candidate is loaded and its assignments are materialized before activation. A failed candidate is unloaded once and the active slot set remains unchanged. Replace publishes the candidate before unloading the previous scene.

## Feature Sets and assignment

A Feature only implements:

```csharp
public interface IRenderFeature
{
    void AddPasses(RenderFeatureContext context);
}
```

Assignment is external:

```csharp
assignments.Register(RenderFeatureSets.Opaque, worldFeature);
assignments.Register(RenderFeatureSets.Opaque, decals, particles);
```

Membership uses reference identity. Re-registering the same instance in the same Set is idempotent; empty registration is a no-op; null elements are rejected. The same Feature instance may be assigned to multiple Sets, with correctness owned by the application.

The global `RenderFeatureSetOrder` controls Set collection only. Feature enumeration order, scene slot order, DI registration order, and `Register` call order are not GPU ordering contracts.

## Cadences

A `RenderCadenceConfiguration` contains only an ID, display name, runner ID, schedule, unordered Set membership, and frame identity policy. Initial schedules are:

- `EveryOpportunity`
- `FixedRate`
- `Invalidated`
- `Manual`
- `AfterSuccess`

Selections are unioned per runner and deduplicated per Set. Fixed-rate rendering does not catch up more than once per opportunity. Invalidated and manual state use generations; observed generations commit only after successful execution. `AfterSuccess` keeps an uncommitted pending generation so a failed presentation retries even when the source cadence is not due again.

## RenderGraph ordering

GPU ordering is derived from resource and explicit control dependencies. Stable symbolic resource version IDs allow a consumer to be contributed before its producer; all producers, predecessors, consumers, and control targets are resolved after Feature collection. Compile rejects structural errors required to build a valid DAG, including foreign handles, missing or multiple producers, unknown predecessors/control targets, and cycles.

External outputs must be exported. Passes with no resource output but intentional external effects must be marked as side effects. Culling, resource lifetime, aliasing, and barriers are calculated after topological ordering.

Set composition has no required-output or runner-domain contract in RenderSystem. Application code owns those semantics; release builds do not perform preflight validation for unknown Sets, omitted global order entries, invalid rates, unknown runners, or cross-runner Set reuse.

## Direct presentation

The initial path is one normal render result to one presentation:

```text
normal render completion
  -> AfterSuccess
  -> acquire target lease
  -> build and submit presentation graph
  -> wait for GPU completion
  -> PresentAsync
```

Normal output publication is not rolled back when presentation fails. Presentation Set generations and the downstream `AfterSuccess` generation commit only after `PresentAsync` succeeds.

## Framework UI

`Luxel.Framework.UI` uses the same RenderSystem execution model for real windows. Each `WindowHost` owns a per-window `CadenceExecutionCoordinator` because each surface has an independent presentation target. Its `PresentUi` Set is scheduled with `CadenceSchedule.Invalidated()` and executed by `PresentationRunner`; `UiContent` contributes retained 2D passes but does not create a graph, record commands, submit, or present directly.

Static UI state therefore produces no GPU work. Deferred widget realization and active animations request logical updates, retained-canvas generation changes invalidate `PresentUi`, and successful presentation commits the observed generation. Idle transition state machines do not keep the UI active. Platform window events are still polled until `IWindowBackend` gains a wakeable wait API, but event polling is independent from rendering and is not a 60 Hz UI cadence.

## Host configuration

```csharp
LuxelHostBuilder.Create(args)
    .UseGpu(...)
    .UseStandardCadences()
    .ConfigureRendering(rendering =>
    {
        rendering.Assignments.Register(AppSets.DebugOverlay, debugFeature);
        rendering.FeatureSets.Order.InsertAfter(
            RenderFeatureSets.WorldUi,
            AppSets.DebugOverlay);
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<IGameSceneBootstrap, AppBootstrap>();
    })
    .AddGameLoop<GameLoop>()
    .Build();
```

`UseStandardCadences()` may be called once. Configuration is sealed during `Build`; runtime invalidation/manual requests mutate trigger state, not configuration.

## Removed legacy API

The runtime `IScene`, old `GameScene`, `SceneManager`, `StartupScene`, `SceneLoopServices`, and `AddScene<T>()` are not part of the new architecture and must not be reintroduced as compatibility wrappers. Editor `SceneDoc`, asset `AssetScene`, `.scene.json` data, and frame-local 2D scene commands are separate concepts.
