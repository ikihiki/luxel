using System.Globalization;
using System.Text.Json;
using Luxel.ValueDocument;

namespace Luxel.Gallery;

/// <summary>Maps Gallery arg schema metadata to the shared value-document descriptor model.</summary>
public static class StoryArgValueDescriptor
{
    public static ValueDescriptor Create(StoryArgDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ValueDescriptor(
            new DescriptorId($"story-arg:{definition.Name}"),
            ShapeOf(definition.DefaultValue),
            EditorOf(definition.EditorKind),
            clrType: null,
            displayName: definition.Name,
            description: definition.Description,
            order: definition.Order,
            numeric: NumericOf(definition),
            options: (definition.Options ?? []).Select(option => new ValueDescriptorOption(OptionLabel(option), option)),
            defaultValue: ParseRoot(definition.DefaultValue),
            codecId: "gallery-story-arg-json",
            annotations: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gallery.type"] = definition.Type,
                ["gallery.editor"] = definition.EditorKind.ToString(),
            });
    }

    private static ValueShape ShapeOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => ValueShape.Object,
        JsonValueKind.Array => ValueShape.Array,
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => ValueShape.Scalar,
        _ => ValueShape.Any,
    };

    private static ValueEditorKind EditorOf(StoryArgEditorKind editor) => editor switch
    {
        StoryArgEditorKind.Json => ValueEditorKind.Json,
        StoryArgEditorKind.Text => ValueEditorKind.Text,
        StoryArgEditorKind.Boolean => ValueEditorKind.Boolean,
        StoryArgEditorKind.Number => ValueEditorKind.Number,
        StoryArgEditorKind.Enum or StoryArgEditorKind.Preset => ValueEditorKind.Enum,
        StoryArgEditorKind.Color => ValueEditorKind.Color,
        StoryArgEditorKind.Length => ValueEditorKind.Length,
        _ => ValueEditorKind.Default,
    };

    private static ValueNumericConstraint? NumericOf(StoryArgDefinition definition)
    {
        if (definition.Min is null && definition.Max is null && definition.Step is null) return null;
        return new ValueNumericConstraint(ToDecimal(definition.Min), ToDecimal(definition.Max), ToDecimal(definition.Step));
    }

    private static decimal? ToDecimal(double? value)
        => value is null ? null : Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture);

    private static string OptionLabel(string option)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(option);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString() ?? string.Empty
                : option;
        }
        catch (JsonException)
        {
            return option;
        }
    }

    internal static ValueNode ParseRoot(JsonElement value)
        => JsonValueCodec.Parse(value.GetRawText()).Root
            ?? throw new FormatException("The story arg value is not valid JSON.");
}

public sealed record StoryJsonArgDiagnostic(string Message, int Line, int Column, string? Source);

/// <summary>Owns the raw draft/candidate/accepted state for one Gallery JSON arg.</summary>
public sealed class StoryJsonArgDocument
{
    private readonly ValueDocument.ValueDocument _document;

    public StoryJsonArgDocument(StoryArgDefinition definition, JsonElement acceptedValue, Action<JsonElement> commit)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(commit);
        if (definition.EditorKind != StoryArgEditorKind.Json)
            throw new ArgumentException("A JSON arg document requires a JSON editor definition.", nameof(definition));

        Definition = definition;
        Descriptor = StoryArgValueDescriptor.Create(definition);
        _document = new ValueDocument.ValueDocument(StoryArgValueDescriptor.ParseRoot(acceptedValue), (candidate, _) =>
        {
            commit(JsonValueCodec.ToJsonElement(candidate));
            return ValueCommitResult.Accepted();
        });
        Tree = new ValueTreeController(_document);
    }

    public ValueDocument.ValueDocument ValueDocument => _document;
    public ValueTreeController Tree { get; }

    public StoryArgDefinition Definition { get; }
    public ValueDescriptor Descriptor { get; }
    public string Text => _document.RawDraft?.Text ?? JsonValueCodec.Serialize(_document.AcceptedRoot);
    public bool IsDirty => _document.RawDraft?.IsDirty == true;
    public bool IsInvalid => _document.Diagnostics.Any(diagnostic => diagnostic.Severity == ValueDiagnosticSeverity.Error);
    public StoryJsonArgDiagnostic? Diagnostic => _document.Diagnostics.FirstOrDefault() is { } diagnostic
        ? new StoryJsonArgDiagnostic(diagnostic.Message, diagnostic.Line, diagnostic.Column, diagnostic.Source)
        : null;

    public bool ValidateRawDraft() => _document.ValidateRawDraft();
    public bool IsTreeReadOnly => IsDirty;
    public long Revision => _document.Revision;

    public void SetRawDraft(string text) => _document.SetRawDraft(text);

    public bool Apply()
    {
        if (!IsDirty) return true;
        return _document.ApplyRawDraft().Success;
    }

    public bool Format(bool indented)
    {
        JsonValueParseResult parsed = JsonValueCodec.Parse(Text);
        if (!parsed.Success)
        {
            _document.ApplyRawDraft();
            return false;
        }

        _document.SetRawDraft(JsonValueCodec.Serialize(parsed.Root!, indented));
        return true;
    }

    public void Discard() => _document.DiscardRawDraft();

    public bool RefreshAccepted(JsonElement acceptedValue, string? externalVersion = null)
    {
        ValueNode root = StoryArgValueDescriptor.ParseRoot(acceptedValue);
        if (string.Equals(JsonValueCodec.Serialize(root), JsonValueCodec.Serialize(_document.AcceptedRoot), StringComparison.Ordinal))
            return true;
        return _document.RefreshExternal(root, externalVersion);
    }
}
