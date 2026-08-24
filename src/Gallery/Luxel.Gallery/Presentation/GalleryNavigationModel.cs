namespace Luxel.Gallery.Presentation;

public enum GalleryNavigationNodeKind
{
    Category,
    Component,
    Group,
    Story,
}

/// <summary>Story metadata required by navigation chrome without changing story execution.</summary>
public sealed record GalleryNavigationStory(
    StoryInfo Info,
    string CanonicalPath,
    StoryKind Kind,
    StoryOwnership? Ownership,
    GalleryCompatibility? Compatibility,
    string? CapabilityNote,
    bool RealWindowOnly,
    StoryRegistrationKind RegistrationKind,
    GeneratedComponentStoryDescriptor? ProductionComponent,
    IReadOnlyList<string> Aliases,
    string? ShortDescription,
    string? LongDescription);

/// <summary>One immutable node in the shared category/component/story display tree.</summary>
public sealed record GalleryNavigationNode(
    string Segment,
    string DisplayLabel,
    string CanonicalPath,
    GalleryNavigationNodeKind Kind,
    GalleryNavigationStory? Story,
    string? DefaultStoryPath,
    IReadOnlyList<GalleryNavigationNode> Children)
{
    /// <summary>Component nodes open Docs by default; story nodes retain their exact canonical path.</summary>
    public string? TargetPath => Kind == GalleryNavigationNodeKind.Component
        ? DefaultStoryPath
        : Story?.CanonicalPath ?? DefaultStoryPath;
}

/// <summary>Host-neutral navigation tree derived from a <see cref="StoryCatalog"/>.</summary>
public sealed class GalleryNavigationModel
{
    internal GalleryNavigationModel(IReadOnlyList<GalleryNavigationNode> categories)
    {
        Categories = categories;
        Stories = Traverse(categories).Where(node => node.Story is not null)
            .Select(node => node.Story!)
            .ToArray();
    }

    public IReadOnlyList<GalleryNavigationNode> Categories { get; }
    public IReadOnlyList<GalleryNavigationStory> Stories { get; }

    public GalleryNavigationNode? FindNode(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        return Traverse(Categories).FirstOrDefault(node => node.CanonicalPath == canonicalPath);
    }

    public GalleryNavigationStory? FindStory(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        return Stories.FirstOrDefault(story => story.CanonicalPath == canonicalPath);
    }

    private static IEnumerable<GalleryNavigationNode> Traverse(IEnumerable<GalleryNavigationNode> nodes)
    {
        foreach (GalleryNavigationNode node in nodes)
        {
            yield return node;
            foreach (GalleryNavigationNode child in Traverse(node.Children)) yield return child;
        }
    }
}

/// <summary>Builds component-oriented display navigation while preserving every registered story path.</summary>
public static class GalleryNavigationBuilder
{
    public static GalleryNavigationModel Build(StoryCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        IReadOnlyList<StoryInfo> stories = StoryPresentationOrder.Apply(catalog.All);
        var componentPaths = stories.Select(InferComponentPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToHashSet(StringComparer.Ordinal);
        var roots = new List<MutableNode>();
        int order = 0;

        foreach (StoryInfo story in stories)
        {
            string[] segments = story.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;
            List<MutableNode> level = roots;
            string prefix = string.Empty;
            for (int index = 0; index < segments.Length; index++)
            {
                string segment = segments[index];
                prefix = index == 0 ? segment : $"{prefix}/{segment}";
                MutableNode? node = level.FirstOrDefault(item => item.Segment == segment);
                if (node is null)
                {
                    GalleryNavigationNodeKind kind = NodeKind(
                        prefix, index, index == segments.Length - 1, componentPaths);
                    node = new MutableNode(segment, prefix, kind, order++);
                    level.Add(node);
                }
                if (index == segments.Length - 1) node.Story = story;
                level = node.Children;
            }
        }

        var storyPaths = catalog.All.Select(story => story.Path).ToHashSet(StringComparer.Ordinal);
        GalleryNavigationNode[] categories = roots
            .OrderBy(node => node.Order)
            .Select(node => Freeze(node, catalog, storyPaths))
            .ToArray();
        return new GalleryNavigationModel(categories);
    }

    private static GalleryNavigationNode Freeze(
        MutableNode node,
        StoryCatalog catalog,
        IReadOnlySet<string> storyPaths)
    {
        IEnumerable<MutableNode> orderedChildren = node.Kind == GalleryNavigationNodeKind.Component
            ? node.Children.OrderBy(ComponentChildRank).ThenBy(child => child.Order)
            : node.Children.OrderBy(child => child.Order);
        GalleryNavigationNode[] children = orderedChildren
            .Select(child => Freeze(child, catalog, storyPaths))
            .ToArray();
        GalleryNavigationStory? story = node.Story is null ? null : new GalleryNavigationStory(
            node.Story,
            node.Story.Path,
            node.Story.Kind,
            node.Story.Ownership,
            node.Story.Ownership?.Compatibility,
            node.Story.CapabilityNote,
            node.Story.RealWindowOnly,
            node.Story.RegistrationKind,
            node.Story.ProductionComponent,
            catalog.AliasesFor(node.Story.Path),
            node.Story.ShortDescription,
            node.Story.LongDescription);
        string docsPath = $"{node.CanonicalPath}/Docs";
        string? defaultStoryPath = node.Kind == GalleryNavigationNodeKind.Component && storyPaths.Contains(docsPath)
            ? docsPath
            : null;
        string displayLabel = node.Kind switch
        {
            GalleryNavigationNodeKind.Component => node.Segment,
            GalleryNavigationNodeKind.Story when node.Story?.Kind is StoryKind.Docs
                or StoryKind.Basic
                or StoryKind.Playground => GalleryLabels.StoryKindLabel(node.Story.Kind),
            GalleryNavigationNodeKind.Story when node.Segment == "Overview" => GalleryLabels.RouteGroupLabel(node.Segment),
            GalleryNavigationNodeKind.Story => node.Segment,
            _ => GalleryLabels.RouteGroupLabel(node.Segment),
        };
        return new GalleryNavigationNode(
            node.Segment,
            displayLabel,
            node.CanonicalPath,
            node.Kind,
            story,
            defaultStoryPath,
            children);
    }

    private static GalleryNavigationNodeKind NodeKind(
        string path,
        int segmentIndex,
        bool isStory,
        IReadOnlySet<string> componentPaths)
    {
        if (componentPaths.Contains(path)) return GalleryNavigationNodeKind.Component;
        if (isStory) return GalleryNavigationNodeKind.Story;
        if (segmentIndex == 0) return GalleryNavigationNodeKind.Category;
        return IsBelowComponent(path, componentPaths)
            ? GalleryNavigationNodeKind.Group
            : GalleryNavigationNodeKind.Category;
    }

    private static bool IsBelowComponent(string path, IReadOnlySet<string> componentPaths)
    {
        int slash = path.LastIndexOf('/');
        while (slash > 0)
        {
            path = path[..slash];
            if (componentPaths.Contains(path)) return true;
            slash = path.LastIndexOf('/');
        }
        return false;
    }

    private static string? InferComponentPath(StoryInfo story)
    {
        if (story.ProductionComponent is not null) return story.ProductionComponent.RoutePrefix;
        string[] segments = story.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;
        if (story.Kind is StoryKind.Docs or StoryKind.Basic or StoryKind.Playground)
            return string.Join('/', segments, 0, segments.Length - 1);

        string? group = story.Kind switch
        {
            StoryKind.Example => "Examples",
            StoryKind.State => "States",
            StoryKind.AccessibilityFixture => "Accessibility",
            StoryKind.TestFixture => "Test",
            _ => null,
        };
        if (group is null) return null;
        int groupIndex = Array.LastIndexOf(segments, group);
        return groupIndex > 0 ? string.Join('/', segments, 0, groupIndex) : null;
    }

    private static int ComponentChildRank(MutableNode node) => node.Segment switch
    {
        "Docs" => 0,
        "Playground" => 1,
        "Basic" => 2,
        "Examples" => 3,
        "States" => 4,
        "Accessibility" => 5,
        "Test" => 6,
        _ => 100,
    };

    private sealed class MutableNode(
        string segment,
        string canonicalPath,
        GalleryNavigationNodeKind kind,
        int order)
    {
        public string Segment { get; } = segment;
        public string CanonicalPath { get; } = canonicalPath;
        public GalleryNavigationNodeKind Kind { get; } = kind;
        public int Order { get; } = order;
        public StoryInfo? Story { get; set; }
        public List<MutableNode> Children { get; } = [];
    }
}
