using System.Net;
using System.Text;

namespace TypeSafe.Cli.Tests;

public sealed record CliExecutionResult(
    int ExitCode,
    string StdOut,
    string StdErr);

public sealed class CliTestHarness : IDisposable
{
    private readonly MockHttpMessageHandler? _mockHandler;
    private readonly HttpClient? _httpClient;

    public CliTestHarness(HttpResponseMessage? defaultResponse = null)
    {
        if (defaultResponse != null)
        {
            _mockHandler = new MockHttpMessageHandler(defaultResponse);
            _httpClient = new HttpClient(_mockHandler);
        }
    }

    public CliTestHarness(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
    {
        _mockHandler = new MockHttpMessageHandler(handlerFunc);
        _httpClient = new HttpClient(_mockHandler);
    }

    public IReadOnlyList<HttpRequestMessage> SentRequests => _mockHandler?.SentRequests ?? [];

    public async Task<CliExecutionResult> RunAsync(
        string[] args,
        string? stdinText = null)
    {
        using var stdoutWriter = new StringWriter();
        using var stderrWriter = new StringWriter();
        using var stdinReader = stdinText != null ? new StringReader(stdinText) : new StringReader(string.Empty);

        var context = new CliContext
        {
            Out = stdoutWriter,
            Error = stderrWriter,
            In = stdinReader,
            IsInputRedirected = stdinText != null,
            HttpClient = _httpClient
        };

        var rootCommand = RootCommandBuilder.Build(context);
        var exitCode = await rootCommand.Parse(args, RootCommandBuilder.ParserConfiguration).InvokeAsync();

        return new CliExecutionResult(
            exitCode,
            stdoutWriter.ToString(),
            stderrWriter.ToString());
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockHandler?.Dispose();
    }
}

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
    private readonly List<HttpRequestMessage> _sentRequests = [];

    public MockHttpMessageHandler(HttpResponseMessage fixedResponse)
    {
        _handler = _ => fixedResponse;
    }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    public IReadOnlyList<HttpRequestMessage> SentRequests => _sentRequests;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _sentRequests.Add(request);
        var response = _handler(request);
        return Task.FromResult(response);
    }

    public static HttpResponseMessage CreateJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
