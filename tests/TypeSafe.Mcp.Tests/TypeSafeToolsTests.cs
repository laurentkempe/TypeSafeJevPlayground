using System.Text.Json;
using ModelContextProtocol;
using Xunit;

namespace TypeSafe.Mcp.Tests;

public class TypeSafeToolsTests
{
    [Fact]
    public async Task NullableUsageIsPreservedInMcpResults()
    {
        var (tools, _) = TestHarness.Create(TestHarness.NoulResponse
            .Replace("\"input_tokens\":12", "\"input_tokens\":null")
            .Replace("\"output_tokens\":1", "\"output_tokens\":null"));
        var result = await tools.Noul("text", "Is it spam?");
        Assert.Null(result.Usage.InputTokens);
        Assert.Null(result.Usage.OutputTokens);
        var json = JsonSerializer.Serialize(result, McpToolJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("usage").GetProperty("inputTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("usage").GetProperty("outputTokens").ValueKind);
    }

    [Fact]
    public async Task NoulSendsQuestionAndReturnsProbability()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.NoulResponse);

        var result = await tools.Noul("Refund me now!", "The customer is angry.", "Hostile tone", "Calm tone");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/v1/systemone", request.Uri.AbsolutePath);
        var json = request.Json!.Value;
        Assert.Equal("Refund me now!", json.GetProperty("state").GetString());
        Assert.Equal("jev-latest", json.GetProperty("model").GetString());
        var question = json.GetProperty("questions").GetProperty("result");
        Assert.Equal("noul", question.GetProperty("type").GetString());
        Assert.Equal("The customer is angry.", question.GetProperty("instructions").GetString());
        Assert.Equal("Hostile tone", question.GetProperty("criteria").GetProperty("true").GetString());
        Assert.Equal("Calm tone", question.GetProperty("criteria").GetProperty("false").GetString());

        Assert.Equal("jev-latest", result.Model);
        var answer = result.Answers["result"];
        Assert.Equal("noul", answer.Type);
        Assert.Equal(0.91, answer.Noul);
        Assert.Equal(12, result.Usage.InputTokens);
        Assert.Equal(1, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task NoulWithoutCriteriaOmitsCriteria()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.NoulResponse);

        await tools.Noul("text", "Is it spam?");

        var question = handler.Requests[0].Json!.Value.GetProperty("questions").GetProperty("result");
        Assert.False(question.TryGetProperty("criteria", out _));
    }

    [Fact]
    public async Task ChoiceSendsCriteriaDictionaryAndReturnsProbabilities()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.ChoiceResponse);

        var result = await tools.Choice(
            """{"subject":"Invoice wrong"}""",
            "Route the `subject`.",
            new Dictionary<string, string> { ["billing"] = "Payment issues", ["bug"] = "Product defects" });

        var json = handler.Requests[0].Json!.Value;
        Assert.Equal(JsonValueKind.Object, json.GetProperty("state").ValueKind);
        Assert.Equal("Invoice wrong", json.GetProperty("state").GetProperty("subject").GetString());
        var question = json.GetProperty("questions").GetProperty("result");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        Assert.Equal("Payment issues", question.GetProperty("criteria").GetProperty("billing").GetString());
        Assert.Equal("Product defects", question.GetProperty("criteria").GetProperty("bug").GetString());

        var answer = result.Answers["result"];
        Assert.Equal("billing", answer.Choice);
        Assert.Equal(0.8, answer.Confidence);
        Assert.Equal(0.2, answer.Probabilities!["bug"]);
        Assert.Null(answer.Noul);
    }

    [Fact]
    public async Task ScoreSendsOrderedLevelsAndReturnsLegend()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.ScoreResponse);

        var result = await tools.Score("Server down", "How urgent is it?", ["low", "medium", "high"]);

        var criteria = handler.Requests[0].Json!.Value
            .GetProperty("questions").GetProperty("result").GetProperty("criteria");
        Assert.Equal(["low", "medium", "high"], criteria.EnumerateArray().Select(e => e.GetString()!).ToArray());

        var answer = result.Answers["result"];
        Assert.Equal("score", answer.Type);
        Assert.Equal(1.7, answer.Score);
        Assert.Equal(0.6, answer.Confidence);
        Assert.Equal(3, answer.Legend!.Count);
        Assert.Equal(0.8, answer.Probabilities!["2"]);
    }

    [Fact]
    public async Task ModelArgumentOverridesDefault()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.NoulResponse);

        await tools.Noul("text", "Is it spam?", model: "jev-1");

        Assert.Equal("jev-1", handler.Requests[0].Json!.Value.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ConfiguredModelIsUsedWhenNoArgument()
    {
        var (tools, handler) = TestHarness.Create(
            TestHarness.NoulResponse,
            options: new TypeSafeMcpOptions { ApiKey = "test-key-123456", Model = "jev-configured" });

        await tools.Noul("text", "Is it spam?");

        Assert.Equal("jev-configured", handler.Requests[0].Json!.Value.GetProperty("model").GetString());
    }

    [Fact]
    public async Task BaseUrlOptionIsUsed()
    {
        var (tools, handler) = TestHarness.Create(
            TestHarness.NoulResponse,
            options: new TypeSafeMcpOptions { ApiKey = "test-key-123456", BaseUrl = new Uri("https://example.test/") });

        await tools.Noul("text", "Is it spam?");

        Assert.Equal("https://example.test/v1/systemone", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task ChoiceWithoutChoicesIsAToolError()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.ChoiceResponse);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Choice("text", "Route it", []));

        Assert.Contains("Choice criteria cannot be empty", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ScoreWithOneLevelIsAToolError()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.ScoreResponse);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Score("text", "Rate it", ["only"]));

        Assert.Contains("at least two", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmptyStateIsAToolError()
    {
        var (tools, handler) = TestHarness.Create(TestHarness.NoulResponse);

        await Assert.ThrowsAsync<McpException>(() => tools.Noul("  ", "Is it spam?"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListModelsMapsModelCards()
    {
        var (tools, handler) = TestHarness.Create(
            """{"models":[{"name":"jev-latest","description":"Latest","release_date":"2026-01-01"}]}""");

        var models = await tools.ListModels();

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith("/v1/models", handler.Requests[0].Uri.AbsolutePath);
        var model = Assert.Single(models);
        Assert.Equal(new ModelInfo("jev-latest", "Latest", "2026-01-01"), model);
    }
}
