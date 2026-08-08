using Luxel.Gallery;

namespace GalleryBrowser;

public sealed class GalleryStoryTreeNode(string name)
{
    public string Name { get; } = name;
    public StoryInfo? Story { get; set; }
    public List<GalleryStoryTreeNode> Children { get; } = [];
    public int Order { get; set; } = int.MaxValue;

    public bool Contains(string path)
        => Story?.Path == path || Children.Any(child => child.Contains(path));
}

internal static class GalleryStoryTree
{
    public static IReadOnlyList<GalleryStoryTreeNode> Build(IEnumerable<StoryInfo> stories)
    {
        var roots = new List<GalleryStoryTreeNode>();
        foreach (StoryInfo story in stories)
        {
            List<GalleryStoryTreeNode> level = roots;
            string[] segments = story.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                GalleryStoryTreeNode? node = level.FirstOrDefault(item => item.Name == segments[index]);
                if (node is null)
                {
                    node = new GalleryStoryTreeNode(segments[index]);
                    level.Add(node);
                }
                node.Order = Math.Min(node.Order, story.Order);
                if (index == segments.Length - 1) node.Story = story;
                level = node.Children;
            }
        }
        Sort(roots);
        return roots;
    }

    private static void Sort(List<GalleryStoryTreeNode> nodes)
    {
        nodes.Sort(static (left, right) =>
        {
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });
        foreach (GalleryStoryTreeNode node in nodes) Sort(node.Children);
    }
}
