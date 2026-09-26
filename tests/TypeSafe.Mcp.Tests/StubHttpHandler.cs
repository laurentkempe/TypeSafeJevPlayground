using System.Net;
using System.Text;
using System.Text.Json;
using TypeSafe.Mcp.Tools;

namespace TypeSafe.Mcp.Tests;

internal sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            content is null ? null : JsonDocument.Parse(content).RootElement.Clone()));
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, JsonElement? Json);

internal static class TestHarness
{
    public const string NoulResponse =
        """{"model":"jev-latest","answers":{"result":{"type":"noul","noul":0.91}},"usage":{"input_tokens":12,"output_tokens":1}}""";

    public const string ChoiceResponse =
        """{"model":"jev-latest","answers":{"result":{"type":"choice","choice":"billing","confidence":0.8,"probabilities":{"billing":0.8,"bug":0.2}}},"usage":{"input_tokens":20,"output_tokens":1}}""";

    public const string ScoreResponse =
        """{"model":"jev-latest","answers":{"result":{"type":"score","score":1.7,"confidence":0.6,"legend":{"0":"low","1":"medium","2":"high"},"probabilities":{"0":0.1,"1":0.1,"2":0.8}}},"usage":{"input_tokens":30,"output_tokens":1}}""";

    public static (TypeSafeTools Tools, StubHttpHandler Handler) Create(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        TypeSafeMcpOptions? options = null)
    {
        var handler = new StubHttpHandler(status, body);
        var provider = new TypeSafeClientProvider(
            options ?? new TypeSafeMcpOptions { ApiKey = "test-key-123456" },
            new HttpClient(handler));
        return (new TypeSafeTools(new TypeSafeEvaluator(provider)), handler);
    }
}
