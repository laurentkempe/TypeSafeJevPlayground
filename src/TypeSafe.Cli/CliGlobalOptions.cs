namespace TypeSafe.Cli;

public sealed record CliGlobalOptions(
    string? ApiKey = null,
    Uri? BaseUrl = null,
    string? Model = null,
    bool Json = false,
    bool Verbose = false);
