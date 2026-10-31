using System.CommandLine;

namespace TypeSafe.Cli.Commands;

public sealed class GlobalOptionDefinitions
{
    public Option<string?> ApiKey { get; } = new("--api-key", "-a")
    {
        Description = "TypeSafe API key (or set TYPESAFE_API_KEY environment variable)."
    };

    public Option<Uri?> BaseUrl { get; } = new("--base-url")
    {
        Description = "TypeSafe API base URL (or set TYPESAFE_BASE_URL environment variable)."
    };

    public Option<string?> Model { get; } = new("--model", "-m")
    {
        Description = "Model name to use for evaluation (or set TYPESAFE_DEFAULT_MODEL environment variable)."
    };

    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Output result strictly as machine-readable JSON."
    };

    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Enable verbose diagnostic output to stderr."
    };

    public GlobalOptionDefinitions()
    {
        ApiKey.Recursive = true;
        BaseUrl.Recursive = true;
        Model.Recursive = true;
        Json.Recursive = true;
        Verbose.Recursive = true;
    }

    public void AddToCommand(Command command)
    {
        command.Add(ApiKey);
        command.Add(BaseUrl);
        command.Add(Model);
        command.Add(Json);
        command.Add(Verbose);
    }

    public CliGlobalOptions Extract(ParseResult parseResult)
    {
        return new CliGlobalOptions(
            ApiKey: parseResult.GetValue(ApiKey),
            BaseUrl: parseResult.GetValue(BaseUrl),
            Model: parseResult.GetValue(Model),
            Json: parseResult.GetValue(Json),
            Verbose: parseResult.GetValue(Verbose));
    }
}
