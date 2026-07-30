using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Luxel.UI;

namespace Luxel.Gallery;

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

    public StoryArgs With(string name, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var copy = new Dictionary<string, JsonElement>(_values, StringComparer.Ordinal) { [name] = value.Clone() };
        return new StoryArgs(copy);
    }

    public StoryArgs WithDefaults(IReadOnlyList<StoryArgDefinition> definitions)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (StoryArgDefinition definition in definitions) values[definition.Name] = definition.DefaultValue.Clone();
        foreach ((string name, JsonElement value) in _values) values[name] = value.Clone();
        return new StoryArgs(values);
    }

    public StoryArgs WithoutDefaults(IReadOnlyList<StoryArgDefinition> definitions)
    {
        var defaults = definitions.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
        return new StoryArgs(_values.Where(pair => !defaults.TryGetValue(pair.Key, out StoryArgDefinition? definition)
            || !StoryArgCodec.CanonicalEquals(pair.Value, definition.DefaultValue)));
    }

    public string ToJson() => StoryArgCodec.SerializeObject(_values);

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
    double? Step = null,
    IReadOnlyList<string>? Options = null)
{
    /// <summary>Creates a canonical static schema entry without building the story.</summary>
    public static StoryArgDefinition Create<T>(string name, string type, T defaultValue,
        string? description = null, int order = 1000, double? min = null, double? max = null,
        double? step = null, IReadOnlyList<string>? options = null)
        => new(name, type, StoryArgCodec.Serialize(defaultValue), description, order, min, max, step, options);
}

public sealed class StoryArgOptions<T>
{
    public string? Description { get; init; }
    public int Order { get; init; } = 1000;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    /// <summary>Optional compile-time generated parser for safe IParsable values.</summary>
    public Func<JsonElement, T>? Parser { get; init; }
}

/// <summary>Canonical story arg encoding shared by schema, URLs, manifests and browser messages.</summary>
public static class StoryArgCodec
{
    public static JsonElement Serialize<T>(T value)
    {
        // Story schemas are created during explicit catalog registration, including browser-WASM where
        // reflection-based System.Text.Json metadata is intentionally unavailable. Keep supported wire
        // primitives fully reflection-free.
        object? boxed = value;
        return boxed switch
        {
            null => ParseElement("null"),
            Length length => StringElement(length.ToString()),
            Enum enumeration => StringElement(enumeration.ToString()),
            uint color => StringElement(WidgetDebugCodec.FormatColor(color)),
            string text => StringElement(text),
            bool boolean => ParseElement(boolean ? "true" : "false"),
            byte number => NumberElement(number),
            sbyte number => NumberElement(number),
            short number => NumberElement(number),
            ushort number => NumberElement(number),
            int number => NumberElement(number),
            long number => NumberElement(number),
            ulong number => NumberElement(number),
            float number => NumberElement(number),
            double number => NumberElement(number),
            decimal number => NumberElement(number),
            _ => throw new NotSupportedException($"Story arg schema type '{typeof(T).Name}' requires an explicit wire parser."),
        };
    }

    private static JsonElement StringElement(string value)
        => ParseElement("\"" + JsonEncodedText.Encode(value).ToString() + "\"");

    private static JsonElement NumberElement<TNumber>(TNumber value) where TNumber : IFormattable
        => ParseElement(value.ToString(null, System.Globalization.CultureInfo.InvariantCulture));

    private static JsonElement ParseElement(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static bool CanonicalEquals(JsonElement left, JsonElement right)
        => string.Equals(CanonicalJson(left), CanonicalJson(right), StringComparison.Ordinal);

    internal static string SerializeObject(IEnumerable<KeyValuePair<string, JsonElement>> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach ((string name, JsonElement value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CanonicalJson(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return value.GetRawText();
        return SerializeObject(value.EnumerateObject()
            .Select(property => KeyValuePair.Create(property.Name, property.Value)));
    }
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

    private StoryResult(string markdown, IReadOnlyList<StoryReference> references)
    {
        Kind = StoryResultKind.Markdown;
        _markdown = new StringBuilder(markdown ?? string.Empty);
        _references = new List<StoryReference>(references);
    }

    /// <summary>Creates semantic Markdown with pre-authored luxel-story fence placeholders.</summary>
    public static StoryResult FromMarkdown(string markdown, params StoryReference[] references)
        => new(markdown, references ?? Array.Empty<StoryReference>());

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
