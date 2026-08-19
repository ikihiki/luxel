using System.Text.Json;
using Luxel.ValueDocument;

namespace Luxel.ValueDocument.Tests;

public sealed class JsonValueCodecTests
{
    [Theory]
    [InlineData("a/b", "a~1b")]
    [InlineData("a~b", "a~0b")]
    [InlineData("~/", "~0~1")]
    [InlineData("", "")]
    public void JsonPointerEscapesAndUnescapesSegments(string value, string encoded)
    {
        Assert.Equal(encoded, JsonPointer.Escape(value));
        Assert.True(JsonPointer.TryUnescape(encoded, out string decoded));
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void JsonPointerProjectionResolvesEscapedObjectNamesAndArrayIndexes()
    {
        ValueNode root = Parse("{\"a/b\":{\"~key\":[10]}}");

        Assert.True(JsonPointer.TryResolve(root, "/a~1b/~0key/0", out ValueNode? node));
        var number = Assert.IsType<ValueScalarNode>(node);
        Assert.Equal("10", number.NumberLexeme);
        Assert.False(JsonPointer.TryResolve(root, "/a~1b/~0key/01", out _));
        Assert.False(JsonPointer.TryUnescape("bad~2escape", out _));
    }

    [Theory]
    [InlineData("900719925474099312345678901234567890")]
    [InlineData("-1234567890.123456789012345678901234567890e+123")]
    [InlineData("0.000000000000000000000000000000000000001")]
    public void NumberLexemeRoundTripsExactly(string number)
    {
        ValueNode root = Parse(number);

        Assert.Equal(number, JsonValueCodec.Serialize(root));
        Assert.Equal(number, Assert.IsType<ValueScalarNode>(root).NumberLexeme);
    }

    [Fact]
    public void ObjectPropertyOrderIsPreservedInBothSerializationModes()
    {
        ValueNode root = Parse("{\"z\":1,\"a\":2,\"m\":3}");
        var obj = Assert.IsType<ValueObjectNode>(root);

        Assert.Equal(["z", "a", "m"], obj.Properties.Select(property => property.Name));
        Assert.Equal("{\"z\":1,\"a\":2,\"m\":3}", JsonValueCodec.Serialize(root));
        Assert.Equal("{\n  \"z\": 1,\n  \"a\": 2,\n  \"m\": 3\n}", JsonValueCodec.Serialize(root, indented: true));
    }

    [Fact]
    public void DuplicateObjectKeyProducesPointerAwareDiagnosticAndNoRoot()
    {
        JsonValueParseResult result = JsonValueCodec.Parse("{\n  \"a\": 1,\n  \"a\": 2\n}");

        Assert.False(result.Success);
        Assert.Null(result.Root);
        ValueDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("Duplicate", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("/a", diagnostic.Pointer);
        Assert.Equal(3, diagnostic.Line);
        Assert.True(diagnostic.Column > 1);
        Assert.True(diagnostic.Offset > 0);
    }

    [Fact]
    public void JsonElementConversionReturnsIndependentCloneWithExactShape()
    {
        ValueNode root = Parse("{\"items\":[true,null,1.25],\"name\":\"value\"}");

        JsonElement element = JsonValueCodec.ToJsonElement(root);

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.True(element.GetProperty("items")[0].GetBoolean());
        Assert.Equal(JsonValueKind.Null, element.GetProperty("items")[1].ValueKind);
        Assert.Equal("1.25", element.GetProperty("items")[2].GetRawText());
        Assert.Equal("value", element.GetProperty("name").GetString());
    }

    [Fact]
    public void ParseErrorContainsSourceLocation()
    {
        JsonValueParseResult result = JsonValueCodec.Parse("{\n  \"a\": [1,\n}");

        Assert.False(result.Success);
        ValueDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ValueDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Offset > 0);
        Assert.True(diagnostic.Line >= 2);
        Assert.True(diagnostic.Column >= 1);
    }

    private static ValueNode Parse(string json)
    {
        JsonValueParseResult result = JsonValueCodec.Parse(json);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        return Assert.IsAssignableFrom<ValueNode>(result.Root);
    }
}
