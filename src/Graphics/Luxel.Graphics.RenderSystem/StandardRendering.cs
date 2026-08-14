using System.Collections.ObjectModel;

namespace Luxel.Graphics.RenderSystem;

public static class RenderFeatureSets
{
    public static readonly RenderFeatureSetId UiContent = new("ui-content");
    public static readonly RenderFeatureSetId ScenePreparation = new("scene-preparation");
    public static readonly RenderFeatureSetId Opaque = new("opaque");
    public static readonly RenderFeatureSetId Transparent = new("transparent");
    public static readonly RenderFeatureSetId WorldUi = new("world-ui");
    public static readonly RenderFeatureSetId PostProcess = new("post-process");
    public static readonly RenderFeatureSetId RenderOutput = new("render-output");
    public static readonly RenderFeatureSetId AcquirePresentedColor = new("acquire-presented-color");
    public static readonly RenderFeatureSetId PresentUi = new("present-ui");
    public static readonly RenderFeatureSetId OutputTransform = new("output-transform");
    public static readonly RenderFeatureSetId PresentOutput = new("present-output");
}

public static class RenderCadences
{
    public static readonly RenderCadenceId SurfaceContent = new("surface-content");
    public static readonly RenderCadenceId SceneRender = new("scene-render");
    public static readonly RenderCadenceId Presentation = new("presentation");
}

public static class RenderCadenceRunners
{
    public static readonly RenderCadenceRunnerId RenderGraph = new("render-graph");
    public static readonly RenderCadenceRunnerId Presentation = new("presentation");
}

public sealed class RenderFeatureSetCatalog
{
    private readonly Dictionary<RenderFeatureSetId, RenderFeatureSetDefinition> _definitions = [];
    private bool _sealed;

    public RenderFeatureSetOrder Order { get; } = new();
    public IReadOnlyDictionary<RenderFeatureSetId, RenderFeatureSetDefinition> Definitions
        => new ReadOnlyDictionary<RenderFeatureSetId, RenderFeatureSetDefinition>(_definitions);

    public RenderFeatureSetCatalog Add(RenderFeatureSetDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        _definitions[definition.Id] = definition;
        return this;
    }

    public void Seal()
    {
        _sealed = true;
        Order.Seal();
    }

    private void EnsureMutable()
    {
        if (_sealed) throw new InvalidOperationException("The render feature set catalog is sealed.");
    }
}

public sealed class RenderCadenceCatalog
{
    private readonly List<RenderCadenceConfiguration> _items = [];
    private bool _sealed;

    public IReadOnlyList<RenderCadenceConfiguration> Items => _items;

    public RenderCadenceCatalog Add(RenderCadenceConfiguration configuration)
    {
        if (_sealed) throw new InvalidOperationException("The render cadence catalog is sealed.");
        ArgumentNullException.ThrowIfNull(configuration);
        _items.Add(configuration);
        return this;
    }

    public void Seal() => _sealed = true;
}

public sealed class RenderSystemConfiguration(
    RenderFeatureSetCatalog featureSets,
    RenderCadenceCatalog cadences,
    IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> globalAssignments)
{
    public RenderFeatureSetCatalog FeatureSets { get; } = featureSets;
    public RenderCadenceCatalog Cadences { get; } = cadences;
    public IReadOnlyDictionary<RenderFeatureSetId, CompiledRenderFeatureSet> GlobalAssignments { get; } = globalAssignments;
}
