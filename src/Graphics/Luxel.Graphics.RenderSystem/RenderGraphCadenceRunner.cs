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

        try
        {
            using var graph = new RenderGraph.RenderGraph(device);
            var context = new RenderFeatureContext(graph, opportunity, frame);
            foreach (DueRenderFeatureSet set in featureSets)
                foreach (IRenderFeature feature in set.FeatureSet.Features)
                    feature.AddPasses(context);

            if (graph.PassCount == 0) return RenderCadenceExecutionResult.Succeeded;

            using GpuCommandBuffer commandBuffer = device.MainQueue.StartCommandRecording();
            graph.Execute(commandBuffer);
            commandBuffer.Finish();
            await device.MainQueue.SubmitAsync(commandBuffer, token);
            return RenderCadenceExecutionResult.Succeeded;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RenderCadenceExecutionResult.Failed;
        }
    }

    public ValueTask DrainAsync(CancellationToken token) => device.MainQueue.WaitIdleAsync(token);
}
