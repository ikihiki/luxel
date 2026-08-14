namespace Luxel.Graphics.RenderSystem;

public sealed class RenderGraphCadenceRunner(GpuDevice device) : IRenderCadenceRunner
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
            using var graph = new RenderGraph.RenderGraph(device);
            var context = new RenderFeatureContext(graph, opportunity, frame);
            foreach (IRenderFeature feature in features) feature.AddPasses(context);

            if (graph.PassCount > 0)
            {
                using GpuCommandBuffer commandBuffer = device.MainQueue.StartCommandRecording();
                graph.Execute(commandBuffer);
                commandBuffer.Finish();
                await device.MainQueue.SubmitAsync(commandBuffer, token);
            }
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

    public ValueTask DrainAsync(CancellationToken token) => device.MainQueue.WaitIdleAsync(token);
}
