namespace Luxel.Graphics.RenderSystem;

/// <summary>A direct 1:1 presentation target acquired for one render opportunity.</summary>
public sealed record PresentationTarget(
    GpuBuffer Buffer,
    uint StridePixels,
    uint Width,
    uint Height);

public interface IPresentationTargetLease : IAsyncDisposable
{
    PresentationTarget Target { get; }
}

/// <summary>Owns backend target acquisition, present, pacing results, and drain.</summary>
public interface IPresentationScheduler : IAsyncDisposable
{
    ValueTask<IPresentationTargetLease> AcquireAsync(CancellationToken token);
    ValueTask PresentAsync(IPresentationTargetLease target, CancellationToken token);
    ValueTask DrainAsync(CancellationToken token);
}

/// <summary>
/// Executes presentation feature sets as a separate transaction. Generation commit is performed by the
/// coordinator only after this method has completed both GPU work and PresentAsync successfully.
/// </summary>
public sealed class PresentationRunner(
    GpuDevice device,
    IPresentationScheduler scheduler) : IRenderCadenceRunner
{
    public async ValueTask<RenderCadenceExecutionResult> ExecuteAsync(
        RenderOpportunity opportunity,
        RenderSystemFrameSnapshot frame,
        IReadOnlyList<DueRenderFeatureSet> featureSets,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(featureSets);
        if (featureSets.Count == 0) return RenderCadenceExecutionResult.Succeeded;

        IRenderFeature[] features = new HashSet<IRenderFeature>(
            featureSets.SelectMany(set => set.FeatureSet.Features),
            ReferenceEqualityComparer.Instance).ToArray();
        try
        {
            await using IPresentationTargetLease lease = await scheduler.AcquireAsync(token);
            RenderSystemFrameSnapshot presentationFrame = frame with
            {
                Resources = frame.Resources.With(lease.Target),
            };

            using var graph = new RenderGraph.RenderGraph(device);
            var context = new RenderFeatureContext(graph, opportunity, presentationFrame);
            foreach (IRenderFeature feature in features) feature.AddPasses(context);

            if (graph.PassCount > 0)
            {
                using GpuCommandBuffer commandBuffer = device.MainQueue.StartCommandRecording();
                graph.Execute(commandBuffer);
                commandBuffer.Finish();
                await device.MainQueue.SubmitAsync(commandBuffer, token);
            }

            await scheduler.PresentAsync(lease, token);
            Complete(features, succeeded: true);
            return RenderCadenceExecutionResult.Succeeded;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Complete(features, succeeded: false);
            throw;
        }
        catch
        {
            Complete(features, succeeded: false);
            return RenderCadenceExecutionResult.Failed;
        }
    }

    private static void Complete(IEnumerable<IRenderFeature> features, bool succeeded)
    {
        foreach (IRenderFeatureBatchObserver observer in features.OfType<IRenderFeatureBatchObserver>())
            observer.CompleteBatch(succeeded);
    }

    public async ValueTask DrainAsync(CancellationToken token)
    {
        await device.MainQueue.WaitIdleAsync(token);
        await scheduler.DrainAsync(token);
    }
}

/// <summary>Adapts the current synchronous GpuSurface API to the direct presentation scheduler contract.</summary>
public sealed class DirectGpuSurfacePresentationScheduler(
    GpuSurface surface,
    Func<CancellationToken, ValueTask<PresentationTarget>> acquire) : IPresentationScheduler
{
    private bool _disposed;

    public async ValueTask<IPresentationTargetLease> AcquireAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PresentationTarget target = await acquire(token);
        return new Lease(target);
    }

    public ValueTask PresentAsync(IPresentationTargetLease target, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        token.ThrowIfCancellationRequested();
        PresentationTarget value = target.Target;
        surface.Present(value.Buffer, value.StridePixels, value.Width, value.Height);
        return ValueTask.CompletedTask;
    }

    public ValueTask DrainAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private sealed class Lease(PresentationTarget target) : IPresentationTargetLease
    {
        public PresentationTarget Target { get; } = target;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
