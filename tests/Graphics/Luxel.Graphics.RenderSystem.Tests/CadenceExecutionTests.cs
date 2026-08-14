using Luxel.Graphics.RenderSystem;

namespace Luxel.Graphics.RenderSystem.Tests;

public sealed class CadenceExecutionTests
{
    private static readonly RenderCadenceRunnerId RunnerId = new("render");
    private static readonly RenderFeatureSetId FirstSet = new("first");
    private static readonly RenderFeatureSetId SecondSet = new("second");

    [Fact]
    public async Task DueCadences_UnionSets_AndUseGlobalOrder()
    {
        RenderSystemFrameSnapshot frame = Frame(
            Set(FirstSet, new StubFeature()),
            Set(SecondSet, new StubFeature()));
        var runner = new RecordingRunner();
        var coordinator = Coordinator(
            [
                Cadence("a", CadenceSchedule.EveryOpportunity(), SecondSet),
                Cadence("b", CadenceSchedule.EveryOpportunity(), FirstSet, SecondSet),
            ],
            [FirstSet, SecondSet], runner);

        await coordinator.ExecuteAsync(new RenderOpportunity(1, TimeSpan.Zero, TimeSpan.Zero), frame, default);

        Assert.Single(runner.Executions);
        Assert.Equal([FirstSet, SecondSet], runner.Executions[0].Select(x => x.FeatureSet.Id));
        Assert.Equal(2, runner.Executions[0][1].TriggeredBy.Count);
    }

    [Fact]
    public async Task InvalidatedSet_CommitsObservedGenerationOnlyAfterSuccess()
    {
        var states = new RenderFeatureSetStateRegistry();
        states.Invalidate(FirstSet);
        var runner = new RecordingRunner { Result = RenderCadenceExecutionResult.Failed };
        var coordinator = Coordinator(
            [Cadence("dirty", CadenceSchedule.Invalidated(), FirstSet)],
            [FirstSet], runner, states);
        RenderSystemFrameSnapshot frame = Frame(Set(FirstSet, new StubFeature()));

        await coordinator.ExecuteAsync(new RenderOpportunity(1, TimeSpan.Zero, TimeSpan.Zero), frame, default);
        Assert.True(states.Read(FirstSet).IsDirty);

        runner.Result = RenderCadenceExecutionResult.Succeeded;
        await coordinator.ExecuteAsync(new RenderOpportunity(2, TimeSpan.FromMilliseconds(16), TimeSpan.Zero), frame, default);
        Assert.False(states.Read(FirstSet).IsDirty);
    }

    [Fact]
    public async Task AfterSuccessPending_RetriesWithoutSourceRunningAgain()
    {
        RenderCadenceRunnerId presentationId = new("present");
        RenderFeatureSetId output = new("output");
        var render = new RecordingRunner();
        var present = new RecordingRunner { Result = RenderCadenceExecutionResult.Failed };
        var sourceId = new RenderCadenceId("source");
        var manual = new RenderManualTriggerRegistry();
        var coordinator = new CadenceExecutionCoordinator(
            [
                new RenderCadenceConfiguration(sourceId, "source", RunnerId,
                    CadenceSchedule.Manual(new RenderManualTriggerId("once")),
                    new HashSet<RenderFeatureSetId> { FirstSet }, FrameIdentityPolicy.RenderOpportunity),
                new RenderCadenceConfiguration(new("present"), "present", presentationId,
                    CadenceSchedule.AfterSuccess(sourceId),
                    new HashSet<RenderFeatureSetId> { output }, FrameIdentityPolicy.RenderOpportunity),
            ],
            [FirstSet, output],
            new Dictionary<RenderCadenceRunnerId, IRenderCadenceRunner>
            {
                [RunnerId] = render,
                [presentationId] = present,
            },
            new RenderFeatureSetStateRegistry(),
            manual);
        manual.Request(new RenderManualTriggerId("once"));
        RenderSystemFrameSnapshot frame = Frame(Set(FirstSet, new StubFeature()), Set(output, new StubFeature()));

        await coordinator.ExecuteAsync(new RenderOpportunity(1, TimeSpan.Zero, TimeSpan.Zero), frame, default);
        Assert.Single(present.Executions);

        present.Result = RenderCadenceExecutionResult.Succeeded;
        await coordinator.ExecuteAsync(new RenderOpportunity(2, TimeSpan.FromMilliseconds(16), TimeSpan.Zero), frame, default);
        Assert.Equal(2, present.Executions.Count);
        Assert.Single(render.Executions);
    }

    private static CadenceExecutionCoordinator Coordinator(
        IReadOnlyList<RenderCadenceConfiguration> cadences,
        IReadOnlyList<RenderFeatureSetId> order,
        RecordingRunner runner,
        RenderFeatureSetStateRegistry? states = null)
        => new(cadences, order,
            new Dictionary<RenderCadenceRunnerId, IRenderCadenceRunner> { [RunnerId] = runner },
            states ?? new RenderFeatureSetStateRegistry(), new RenderManualTriggerRegistry());

    private static RenderCadenceConfiguration Cadence(string id, CadenceSchedule schedule, params RenderFeatureSetId[] sets)
        => new(new(id), id, RunnerId, schedule, sets.ToHashSet(), FrameIdentityPolicy.RenderOpportunity);

    private static CompiledRenderFeatureSet Set(RenderFeatureSetId id, params IRenderFeature[] features)
        => new(id, new HashSet<IRenderFeature>(features, ReferenceEqualityComparer.Instance));

    private static RenderSystemFrameSnapshot Frame(params CompiledRenderFeatureSet[] sets)
        => new(default,
            new CompiledRenderFeatureSetRegistry(sets.ToDictionary(x => x.Id)),
            new RenderFrameResourceRegistry());

    private sealed class RecordingRunner : IRenderCadenceRunner
    {
        public List<IReadOnlyList<DueRenderFeatureSet>> Executions { get; } = [];
        public RenderCadenceExecutionResult Result { get; set; } = RenderCadenceExecutionResult.Succeeded;

        public ValueTask<RenderCadenceExecutionResult> ExecuteAsync(
            RenderOpportunity opportunity,
            RenderSystemFrameSnapshot frame,
            IReadOnlyList<DueRenderFeatureSet> featureSets,
            CancellationToken token)
        {
            Executions.Add(featureSets.ToArray());
            return ValueTask.FromResult(Result);
        }

        public ValueTask DrainAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    private sealed class StubFeature : IRenderFeature
    {
        public void AddPasses(RenderFeatureContext context) { }
    }
}
