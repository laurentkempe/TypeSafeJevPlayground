using System.Text.Json;
using Xunit;

namespace TypeSafe.Mcp.Tests;

public class StateParserTests
{
    [Theory]
    [InlineData("""{"a":1}""", JsonValueKind.Object)]
    [InlineData("""  [1, 2, 3] """, JsonValueKind.Array)]
    public void JsonObjectsAndArraysAreParsed(string state, JsonValueKind kind)
    {
        var element = Assert.IsType<JsonElement>(StateParser.Parse(state));
        Assert.Equal(kind, element.ValueKind);
    }

    [Theory]
    [InlineData("The customer wants a refund.")]
    [InlineData("{not json")]
    [InlineData("42")]
    [InlineData("\"quoted\"")]
    public void EverythingElseIsPlainText(string state)
    {
        Assert.Equal(state, Assert.IsType<string>(StateParser.Parse(state)));
    }
}
