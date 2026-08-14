using Luxel.Graphics.RenderSystem;

namespace Luxel.Framework.Game;

public sealed class LuxelRenderingBuilder
{
    public RenderFeatureSetCatalog FeatureSets { get; } = new();
    public RenderCadenceCatalog Cadences { get; } = new();
    public RenderFeatureAssignmentBuilder Assignments { get; } = new();

    internal RenderSystemConfiguration Build()
    {
        FeatureSets.Seal();
        Cadences.Seal();
        return new RenderSystemConfiguration(FeatureSets, Cadences, Assignments.Build());
    }
}

internal sealed class DirectPresentationFeature : IRenderFeature
{
    public static DirectPresentationFeature Instance { get; } = new();
    private DirectPresentationFeature() { }
    public void AddPasses(RenderFeatureContext context) { }
}

public sealed class StandardCadenceOptions;

public static class StandardRenderingExtensions
{
    public static LuxelHostBuilder UseStandardCadences(
        this LuxelHostBuilder builder,
        Action<StandardCadenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new StandardCadenceOptions();
        configure?.Invoke(options);
        return builder.ConfigureRendering(rendering =>
        {
            AddStandardFeatureSets(rendering.FeatureSets);
            rendering.Assignments.Register(
                RenderFeatureSets.PresentOutput,
                DirectPresentationFeature.Instance);
            rendering.Cadences
                .Add(new RenderCadenceConfiguration(
                    RenderCadences.SurfaceContent,
                    "Surface Content",
                    RenderCadenceRunners.RenderGraph,
                    CadenceSchedule.Invalidated(),
                    new HashSet<RenderFeatureSetId> { RenderFeatureSets.UiContent },
                    FrameIdentityPolicy.RenderOpportunity))
                .Add(new RenderCadenceConfiguration(
                    RenderCadences.SceneRender,
                    "Scene Render",
                    RenderCadenceRunners.RenderGraph,
                    CadenceSchedule.EveryOpportunity(),
                    new HashSet<RenderFeatureSetId>
                    {
                        RenderFeatureSets.ScenePreparation,
                        RenderFeatureSets.Opaque,
                        RenderFeatureSets.Transparent,
                        RenderFeatureSets.WorldUi,
                        RenderFeatureSets.PostProcess,
                        RenderFeatureSets.RenderOutput,
                    },
                    FrameIdentityPolicy.RenderOpportunity))
                .Add(new RenderCadenceConfiguration(
                    RenderCadences.Presentation,
                    "Presentation",
                    RenderCadenceRunners.Presentation,
                    CadenceSchedule.AfterSuccess(RenderCadences.SceneRender),
                    new HashSet<RenderFeatureSetId>
                    {
                        RenderFeatureSets.AcquirePresentedColor,
                        RenderFeatureSets.PresentUi,
                        RenderFeatureSets.OutputTransform,
                        RenderFeatureSets.PresentOutput,
                    },
                    FrameIdentityPolicy.RenderOpportunity));
        }, standardCadences: true);
    }

    private static void AddStandardFeatureSets(RenderFeatureSetCatalog featureSets)
    {
        Add(RenderFeatureSets.UiContent, "UI Content");
        Add(RenderFeatureSets.ScenePreparation, "Scene Preparation");
        Add(RenderFeatureSets.Opaque, "Opaque");
        Add(RenderFeatureSets.Transparent, "Transparent");
        Add(RenderFeatureSets.WorldUi, "World UI");
        Add(RenderFeatureSets.PostProcess, "Post Process");
        Add(RenderFeatureSets.RenderOutput, "Render Output");
        Add(RenderFeatureSets.AcquirePresentedColor, "Acquire Presented Color");
        Add(RenderFeatureSets.PresentUi, "Present UI");
        Add(RenderFeatureSets.OutputTransform, "Output Transform");
        Add(RenderFeatureSets.PresentOutput, "Present Output");
        return;

        void Add(RenderFeatureSetId id, string name)
        {
            featureSets.Add(new RenderFeatureSetDefinition(id, name));
            featureSets.Order.Add(id);
        }
    }
}
