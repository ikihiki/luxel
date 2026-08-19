using System.Text;
using System.Text.Json;

namespace Luxel.ValueDocument;

public enum ValueDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValueDiagnostic(
    string Message,
    ValueDiagnosticSeverity Severity,
    long Offset,
    int Line,
    int Column,
    string? Pointer = null,
    string? Source = null);

public sealed record JsonValueParseResult(ValueNode? Root, IReadOnlyList<ValueDiagnostic> Diagnostics)
{
    public bool Success => Root is not null && Diagnostics.All(d => d.Severity != ValueDiagnosticSeverity.Error);
}

public static class JsonValueCodec
{
    public static JsonValueParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        var diagnostics = new List<ValueDiagnostic>();
        var factory = new ValueNodeFactory();
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });

        try
        {
            if (!reader.Read())
            {
                diagnostics.Add(new ValueDiagnostic("Expected a JSON value.", ValueDiagnosticSeverity.Error, 0, 1, 1, string.Empty, "json"));
                return new JsonValueParseResult(null, diagnostics);
            }

            ValueNode? root = ParseCurrent(ref reader, utf8, factory, diagnostics, string.Empty);
            if (reader.Read())
            {
                AddDiagnostic(diagnostics, utf8, reader.TokenStartIndex, "Only one top-level JSON value is allowed.", string.Empty);
                root = null;
            }
            if (diagnostics.Any(d => d.Severity == ValueDiagnosticSeverity.Error)) root = null;
            return new JsonValueParseResult(root, diagnostics.AsReadOnly());
        }
        catch (JsonException exception)
        {
            long line = exception.LineNumber ?? 0;
            long column = exception.BytePositionInLine ?? 0;
            long offset = OffsetFromLineAndColumn(utf8, line, column);
            diagnostics.Add(new ValueDiagnostic(
                exception.Message,
                ValueDiagnosticSeverity.Error,
                offset,
                checked((int)line + 1),
                checked((int)column + 1),
                null,
                "json"));
            return new JsonValueParseResult(null, diagnostics.AsReadOnly());
        }
    }

    public static string Serialize(ValueNode root, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            Write(writer, root);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JsonElement ToJsonElement(ValueNode root)
    {
        using JsonDocument document = JsonDocument.Parse(Serialize(root));
        return document.RootElement.Clone();
    }

    public static bool IsValidNumberLexeme(string lexeme)
    {
        if (string.IsNullOrEmpty(lexeme)) return false;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(lexeme));
            return reader.Read() && reader.TokenType == JsonTokenType.Number && !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ValueNode? ParseCurrent(
        ref Utf8JsonReader reader,
        ReadOnlySpan<byte> utf8,
        ValueNodeFactory factory,
        List<ValueDiagnostic> diagnostics,
        string pointer)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var properties = new List<ValueProperty>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        AddDiagnostic(diagnostics, utf8, reader.TokenStartIndex, "Expected an object property name.", pointer);
                        return null;
                    }
                    string name = reader.GetString()!;
                    long nameOffset = reader.TokenStartIndex;
                    string childPointer = pointer + "/" + JsonPointer.Escape(name);
                    bool duplicate = !names.Add(name);
                    if (!reader.Read())
                    {
                        AddDiagnostic(diagnostics, utf8, reader.BytesConsumed, "Expected a value for the object property.", childPointer);
                        return null;
                    }
                    ValueNode? value = ParseCurrent(ref reader, utf8, factory, diagnostics, childPointer);
                    if (duplicate)
                    {
                        AddDiagnostic(diagnostics, utf8, nameOffset, $"Duplicate object property '{name}'.", childPointer);
                    }
                    else if (value is not null)
                    {
                        properties.Add(new ValueProperty(name, value));
                    }
                }
                return new ValueObjectNode(factory.NextId(), properties);
            }
            case JsonTokenType.StartArray:
            {
                var items = new List<ValueNode>();
                int index = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    ValueNode? item = ParseCurrent(ref reader, utf8, factory, diagnostics, pointer + "/" + index);
                    if (item is not null) items.Add(item);
                    index++;
                }
                return new ValueArrayNode(factory.NextId(), items);
            }
            case JsonTokenType.String:
                return ValueScalarNode.FromString(factory.NextId(), reader.GetString()!);
            case JsonTokenType.Number:
            {
                int start = checked((int)reader.TokenStartIndex);
                int length = checked((int)(reader.BytesConsumed - reader.TokenStartIndex));
                return ValueScalarNode.FromNumber(factory.NextId(), Encoding.UTF8.GetString(utf8.Slice(start, length)));
            }
            case JsonTokenType.True:
                return ValueScalarNode.FromBoolean(factory.NextId(), true);
            case JsonTokenType.False:
                return ValueScalarNode.FromBoolean(factory.NextId(), false);
            case JsonTokenType.Null:
                return ValueScalarNode.Null(factory.NextId());
            default:
                AddDiagnostic(diagnostics, utf8, reader.TokenStartIndex, $"Unexpected JSON token {reader.TokenType}.", pointer);
                return null;
        }
    }

    private static void Write(Utf8JsonWriter writer, ValueNode node)
    {
        switch (node)
        {
            case ValueObjectNode obj:
                writer.WriteStartObject();
                foreach (ValueProperty property in obj.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case ValueArrayNode array:
                writer.WriteStartArray();
                foreach (ValueNode item in array.Items) Write(writer, item);
                writer.WriteEndArray();
                break;
            case ValueScalarNode { ScalarKind: ValueScalarKind.Null }:
                writer.WriteNullValue();
                break;
            case ValueScalarNode { ScalarKind: ValueScalarKind.Boolean } scalar:
                writer.WriteBooleanValue(scalar.Boolean);
                break;
            case ValueScalarNode { ScalarKind: ValueScalarKind.String } scalar:
                writer.WriteStringValue(scalar.Text);
                break;
            case ValueScalarNode { ScalarKind: ValueScalarKind.Number } scalar:
                writer.WriteRawValue(scalar.NumberLexeme, skipInputValidation: false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported value node type {node.GetType().Name}.");
        }
    }

    private static void AddDiagnostic(List<ValueDiagnostic> diagnostics, ReadOnlySpan<byte> utf8, long offset, string message, string? pointer)
    {
        (int line, int column) = LineAndColumn(utf8, offset);
        diagnostics.Add(new ValueDiagnostic(message, ValueDiagnosticSeverity.Error, offset, line, column, pointer, "json"));
    }

    private static (int Line, int Column) LineAndColumn(ReadOnlySpan<byte> utf8, long offset)
    {
        int line = 1;
        int column = 1;
        int limit = (int)Math.Min(offset, utf8.Length);
        for (int i = 0; i < limit; i++)
        {
            if (utf8[i] == (byte)'\n') { line++; column = 1; }
            else column++;
        }
        return (line, column);
    }

    private static long OffsetFromLineAndColumn(ReadOnlySpan<byte> utf8, long line, long column)
    {
        long currentLine = 0;
        int index = 0;
        while (index < utf8.Length && currentLine < line)
        {
            if (utf8[index++] == (byte)'\n') currentLine++;
        }
        return Math.Min((long)utf8.Length, index + column);
    }
}
