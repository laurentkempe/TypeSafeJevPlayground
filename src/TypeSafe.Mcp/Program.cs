using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TypeSafe.Mcp.Tools;

namespace TypeSafe.Mcp;

public static class Program
{
    internal static readonly IReadOnlyDictionary<string, string> SwitchMappings = new Dictionary<string, string>
    {
        ["--api-key"] = "TypeSafe:ApiKey",
        ["--base-url"] = "TypeSafe:BaseUrl",
        ["--model"] = "TypeSafe:Model"
    };

    public static async Task Main(string[] args)
    {
        using var host = CreateBuilder(args).Build();
        await host.RunAsync().ConfigureAwait(false);
    }

    internal static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.AddCommandLine(args, SwitchMappings.ToDictionary());

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.Configure<TypeSafeMcpOptions>(
            builder.Configuration.GetSection(TypeSafeMcpOptions.SectionName));
        builder.Services.AddSingleton<TypeSafeClientProvider>();
        builder.Services.AddSingleton<TypeSafeEvaluator>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<TypeSafeTools>(McpToolJson.Options);

        return builder;
    }
}
