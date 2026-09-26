using System.Net;
using ModelContextProtocol;
using Xunit;

namespace TypeSafe.Mcp.Tests;

public class EvaluateToolTests
{
    private const string MixedResponse =
        """
        {"model":"jev-latest","answers":{
          "is_spam":{"type":"noul","noul":0.05},
          "topic":{"type":"choice","choice":"billing","confidence":0.9,"probabilities":{"billing":0.9,"bug":0.1}},
          "urgency":{"type":"score","score":0.4,"confidence":0.7,"legend":{"0":"low","1":"high"},"probabilities":{"0":0.6,"1":0.4}}
        },"usage":{"input_tokens":40,"output_tokens":3}}
        """;

    [Fact]
    public async Task MixedQuestionsAreSentInOneRequest()
    {
        var (tools, handler) = TestHarness.Create(MixedResponse);

        var result = await tools.Evaluate(
            """{"body":"My invoice is wrong"}""",
            new Dictionary<string, EvaluateQuestion>
            {
                ["is_spam"] = new("noul", "The `body` is spam."),
                ["topic"] = new("choice", "Topic of `body`.",
                    Choices: new Dictionary<string, string> { ["billing"] = "Payments", ["bug"] = "Defects" }),
                ["urgency"] = new("Score", "How urgent?", Levels: ["low", "high"])
            },
            model: "jev-1");

        var request = Assert.Single(handler.Requests);
        var json = request.Json!.Value;
        Assert.Equal("jev-1", json.GetProperty("model").GetString());
        var questions = json.GetProperty("questions");
        Assert.Equal("noul", questions.GetProperty("is_spam").GetProperty("type").GetString());
        Assert.Equal("choice", questions.GetProperty("topic").GetProperty("type").GetString());
        Assert.Equal("score", questions.GetProperty("urgency").GetProperty("type").GetString());
        Assert.Equal(2, questions.GetProperty("urgency").GetProperty("criteria").GetArrayLength());

        Assert.Equal(3, result.Answers.Count);
        Assert.Equal(0.05, result.Answers["is_spam"].Noul);
        Assert.Equal("billing", result.Answers["topic"].Choice);
        Assert.Equal(0.4, result.Answers["urgency"].Score);
        Assert.Equal(3, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task EmptyQuestionsIsAToolError()
    {
        var (tools, handler) = TestHarness.Create(MixedResponse);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Evaluate("text", []));

        Assert.Contains("At least one question", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnknownTypeIsAToolError()
    {
        var (tools, handler) = TestHarness.Create(MixedResponse);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Evaluate(
            "text",
            new Dictionary<string, EvaluateQuestion> { ["q"] = new("rank", "Rank it") }));

        Assert.Contains("Question 'q'", error.Message);
        Assert.Contains("unknown type 'rank'", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("score")]
    public async Task MissingTypeSpecificFieldsIsAToolError(string type)
    {
        var (tools, handler) = TestHarness.Create(MixedResponse);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Evaluate(
            "text",
            new Dictionary<string, EvaluateQuestion> { ["q"] = new(type, "Judge it") }));

        Assert.Contains("Question 'q'", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnauthorizedMapsToToolError()
    {
        var (tools, _) = TestHarness.Create("""{"error":"Invalid API key"}""", HttpStatusCode.Unauthorized);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Evaluate(
            "text",
            new Dictionary<string, EvaluateQuestion> { ["q"] = new("noul", "Is it spam?") }));

        Assert.Contains("401 Invalid API key", error.Message);
        Assert.IsType<AuthenticationError>(error.InnerException);
    }

    [Fact]
    public async Task RateLimitMapsToToolError()
    {
        var (tools, handler) = TestHarness.Create("""{"error":"Slow down"}""", HttpStatusCode.TooManyRequests);

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Evaluate(
            "text",
            new Dictionary<string, EvaluateQuestion> { ["q"] = new("noul", "Is it spam?") }));

        Assert.Contains("429 Slow down", error.Message);
        Assert.IsType<RateLimitError>(error.InnerException);
        Assert.True(handler.Requests.Count >= 1);
    }
}
