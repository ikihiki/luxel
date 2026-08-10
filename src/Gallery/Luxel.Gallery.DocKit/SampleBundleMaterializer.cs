using System.Text;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Expands a bundle dependency graph into a deterministic, copyable directory tree.</summary>
public static class SampleBundleMaterializer
{
    public static IReadOnlyList<SampleBundleInfo> DependencyClosure(string bundleId)
    {
        var result = new List<SampleBundleInfo>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            SampleBundleInfo bundle = SampleBundleRegistry.Find(id)
                ?? throw new InvalidOperationException($"Unknown sample bundle: {id}");
            if (!visiting.Add(id)) throw new InvalidOperationException($"Sample bundle dependency cycle includes '{id}'.");
            foreach (string dependency in bundle.Dependencies ?? []) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
            result.Add(bundle);
        }

        Visit(bundleId);
        return result;
    }

    public static IReadOnlyList<string> Materialize(string repositoryRoot, string bundleId, string destinationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        string outputRoot = Path.GetFullPath(destinationRoot);
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new InvalidOperationException($"Sample destination already exists: {outputRoot}");
        string stagingRoot = outputRoot + ".tmp-" + Guid.NewGuid().ToString("N");
        var outputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (SampleBundleInfo bundle in DependencyClosure(bundleId))
            foreach (SampleFileInfo file in bundle.Files)
            {
                if (file.EffectiveMode == SampleFileMode.Glob)
                {
                    string sourceDirectory = SafeSource(repositoryRoot, file.Path);
                    if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
                    foreach (string source in Directory.GetFiles(sourceDirectory, file.AssetGlob!, SearchOption.AllDirectories)
                                 .Where(path => !HasBuildSegment(Path.GetRelativePath(sourceDirectory, path)))
                                 .OrderBy(path => path, StringComparer.Ordinal))
                    {
                        string relative = Path.GetRelativePath(sourceDirectory, source);
                        Plan(file, File.ReadAllBytes(source), Path.Combine(file.OutputPath, relative));
                    }
                    continue;
                }

                byte[] content;
                if (file.EffectiveMode == SampleFileMode.Generated)
                {
                    content = Utf8(file.Wrapper ?? throw new InvalidOperationException($"Generated sample file '{file.Path}' requires Wrapper content."));
                }
                else if (file.EffectiveMode == SampleFileMode.Region)
                {
                    content = Utf8(ExtractRegion(File.ReadAllText(SafeSource(repositoryRoot, file.Path)), file.Path,
                        file.Region ?? throw new InvalidOperationException($"Region sample file '{file.Path}' requires Region.")));
                }
                else
                {
                    content = File.ReadAllBytes(SafeSource(repositoryRoot, file.Path));
                }

                if (file.Wrapper is not null && file.EffectiveMode != SampleFileMode.Generated)
                {
                    if (file.Kind == SampleFileKind.Asset)
                        throw new InvalidOperationException($"Binary sample file '{file.Path}' cannot use Wrapper.");
                    string text = Encoding.UTF8.GetString(content);
                    int token = file.Wrapper.IndexOf("{content}", StringComparison.Ordinal);
                    if (token < 0 || file.Wrapper.IndexOf("{content}", token + 9, StringComparison.Ordinal) >= 0)
                        throw new InvalidOperationException($"Wrapper for '{file.Path}' must contain exactly one '{{content}}' token.");
                    content = Utf8(file.Wrapper.Replace("{content}", text, StringComparison.Ordinal));
                }
                Plan(file, content, file.OutputPath);
            }

            foreach ((string relative, byte[] bytes) in outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string destination = SafeDestination(stagingRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            }
            Directory.Move(stagingRoot, outputRoot);
            return outputs.Keys.Order(StringComparer.Ordinal).Select(path => Path.Combine(outputRoot, path)).ToArray();
        }
        catch
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
            throw;
        }

        void Plan(SampleFileInfo file, byte[] content, string relativeDestination)
        {
            string normalized = Path.GetRelativePath(stagingRoot, SafeDestination(stagingRoot, relativeDestination));
            if (outputs.TryGetValue(normalized, out byte[]? existing))
            {
                if (existing.AsSpan().SequenceEqual(content)) return;
                switch (file.MergeRule)
                {
                    case SampleMergeRule.KeepFirst: return;
                    case SampleMergeRule.Replace: break;
                    case SampleMergeRule.Append: content = [.. existing, .. content]; break;
                    default: throw new InvalidOperationException($"Sample bundle path conflict: {relativeDestination}");
                }
            }
            outputs[normalized] = content;
        }

        static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static bool HasBuildSegment(string relativePath)
        => relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or ".git");

    private static string SafeSource(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal))
            throw new InvalidOperationException($"Sample source escapes repository root: {relative}");
        if (!File.Exists(full) && !Directory.Exists(full)) throw new FileNotFoundException("Sample source is missing.", relative);
        return full;
    }

    private static string SafeDestination(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal))
            throw new InvalidOperationException($"Sample destination escapes output root: {relative}");
        return full;
    }

    private static string ExtractRegion(string source, string path, string region)
    {
        string begin = $"docs:begin {region}";
        string end = $"docs:end {region}";
        int beginAt = source.IndexOf(begin, StringComparison.Ordinal);
        int endAt = source.IndexOf(end, StringComparison.Ordinal);
        if (beginAt < 0 || endAt <= beginAt || source.IndexOf(begin, beginAt + begin.Length, StringComparison.Ordinal) >= 0
            || source.IndexOf(end, endAt + end.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Sample source region '{region}' is invalid or duplicated in {path}.");
        int contentStart = source.IndexOf('\n', beginAt);
        int endLineStart = source.LastIndexOf('\n', endAt);
        if (contentStart < 0 || endLineStart <= contentStart) throw new InvalidOperationException($"Sample source region '{region}' is empty in {path}.");
        return source[(contentStart + 1)..endLineStart] + Environment.NewLine;
    }
}
