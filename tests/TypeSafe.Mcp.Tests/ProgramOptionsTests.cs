using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Xunit;

namespace TypeSafe.Mcp.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}

[Collection(EnvironmentCollection.Name)]
public class ProgramOptionsTests
{
    [Fact]
    public void CommandLineSwitchesBindToOptions()
    {
        using var provider = Program.CreateBuilder(
                ["--api-key", "cli-key", "--base-url", "https://example.test", "--model", "jev-1"])
            .Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TypeSafeMcpOptions>>().Value;

        Assert.Equal("cli-key", options.ApiKey);
        Assert.Equal(new Uri("https://example.test"), options.BaseUrl);
        Assert.Equal("jev-1", options.Model);
    }

    [Fact]
    public void ServicesAreRegistered()
    {
        using var provider = Program.CreateBuilder([]).Services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<TypeSafeClientProvider>());
        Assert.NotNull(provider.GetRequiredService<TypeSafeEvaluator>());
    }

    [Fact]
    public async Task ApiKeyOptionTakesPrecedenceOverEnvironment()
    {
        using var _ = new EnvironmentVariable("TYPESAFE_API_KEY", "env-key-abcdef");
        var (tools, handler) = TestHarness.Create(
            TestHarness.NoulResponse,
            options: new TypeSafeMcpOptions { ApiKey = "cli-key-123456" });

        await tools.Noul("text", "Is it spam?");

        Assert.Equal("Bearer cli-key-123456", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task EnvironmentKeyIsUsedWhenNoOption()
    {
        using var _ = new EnvironmentVariable("TYPESAFE_API_KEY", "env-key-abcdef");
        var (tools, handler) = TestHarness.Create(TestHarness.NoulResponse, options: new TypeSafeMcpOptions());

        await tools.Noul("text", "Is it spam?");

        Assert.Equal("Bearer env-key-abcdef", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task MissingApiKeyIsAToolErrorNotAStartupFailure()
    {
        using var _ = new EnvironmentVariable("TYPESAFE_API_KEY", null);
        using var provider = Program.CreateBuilder([]).Services.BuildServiceProvider();
        var (tools, handler) = TestHarness.Create(TestHarness.NoulResponse, options: new TypeSafeMcpOptions());

        var error = await Assert.ThrowsAsync<McpException>(() => tools.Noul("text", "Is it spam?"));

        Assert.Equal("An API key is required. Set --api-key or TYPESAFE_API_KEY.", error.Message);
        Assert.Empty(handler.Requests);
        Assert.NotNull(provider.GetRequiredService<TypeSafeClientProvider>());
    }

    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariable(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
