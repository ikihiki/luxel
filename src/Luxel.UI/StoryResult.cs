using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Luxel.UI;

public enum StoryResultKind
{
    Markdown,
    Widget,
}

/// <summary>Markdown へ埋め込む canonical story reference。</summary>
public sealed record StoryReference(string Path, StoryArgs Args)
{
    public static StoryReference To(string path) => new(path, StoryArgs.Empty);
    public static StoryReference To(string path, object? args) => new(path, StoryArgs.FromObject(args));
}

/// <summary>型付き story args の immutable wire representation。</summary>
public sealed class StoryArgs
{
    private readonly IReadOnlyDictionary<string, JsonElement> _values;

    public static StoryArgs Empty { get; } = new(new Dictionary<string, JsonElement>(StringComparer.Ordinal));
    public IReadOnlyDictionary<string, JsonElement> Values => _values;

    public StoryArgs(IEnumerable<KeyValuePair<string, JsonElement>> values)
    {
        var copy = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string key, JsonElement value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            copy[key] = value.Clone();
        }
        _values = new ReadOnlyDictionary<string, JsonElement>(copy);
    }

    public bool TryGet(string name, out JsonElement value) => _values.TryGetValue(name, out value);

    public string ToJson() => JsonSerializer.Serialize(_values);

    public static StoryArgs Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("Story args must be a JSON object.");
        return new StoryArgs(document.RootElement.EnumerateObject()
            .Select(property => KeyValuePair.Create(property.Name, property.Value)));
    }

    public static StoryArgs FromObject(object? value)
    {
        if (value is null) return Empty;
        if (value is StoryArgs args) return args;
        JsonElement element = JsonSerializer.SerializeToElement(value);
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Story args must serialize to a JSON object.", nameof(value));
        return new StoryArgs(element.EnumerateObject()
            .Select(property => KeyValuePair.Create(property.Name, property.Value)));
    }
}

public sealed record StoryArgDefinition(
    string Name,
    string Type,
    JsonElement DefaultValue,
    string? Description = null,
    int Order = 1000,
    double? Min = null,
    double? Max = null,
    double? Step = null);

public sealed class StoryArgOptions<T>
{
    public string? Description { get; init; }
    public int Order { get; init; } = 1000;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
}

/// <summary>
/// Story の semantic result。target-typed interpolated string は Markdown、Widget は暗黙変換で Widget result になる。
/// </summary>
[InterpolatedStringHandler]
public sealed class StoryResult
{
    private readonly StringBuilder? _markdown;
    private readonly List<StoryReference>? _references;

    public StoryResultKind Kind { get; }
    public Widget? Widget { get; }
    public string Markdown => _markdown?.ToString() ?? string.Empty;
    public IReadOnlyList<StoryReference> References => _references is null ? Array.Empty<StoryReference>() : _references;

    public StoryResult(int literalLength, int formattedCount)
    {
        Kind = StoryResultKind.Markdown;
        _markdown = new StringBuilder(literalLength + formattedCount * 24);
        _references = new List<StoryReference>(formattedCount);
    }

    private StoryResult(Widget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        Kind = StoryResultKind.Widget;
        Widget = widget;
    }

    public static implicit operator StoryResult(Widget widget) => new(widget);

    public void AppendLiteral(string value) => _markdown!.Append(value);

    public void AppendFormatted(StoryReference reference)
    {
        int index = _references!.Count;
        _references.Add(reference);
        if (_markdown!.Length > 0 && _markdown[^1] != '\n') _markdown.Append('\n');
        _markdown.Append("```luxel-story\n").Append(index).Append("\n```");
    }

    public void AppendFormatted<T>(T value)
    {
        if (value is StoryReference reference)
        {
            AppendFormatted(reference);
            return;
        }
        if (value is Widget widget)
            throw new InvalidOperationException("Widget holes are not supported in StoryResult Markdown. Use StoryReference.To(...).");
        _markdown!.Append(value?.ToString() ?? string.Empty);
    }

    public override string ToString() => Kind == StoryResultKind.Markdown ? Markdown : Widget?.ToString() ?? string.Empty;
}
