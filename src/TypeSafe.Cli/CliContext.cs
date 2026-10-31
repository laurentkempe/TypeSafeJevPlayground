namespace TypeSafe.Cli;

public sealed class CliContext
{
    public TextWriter Out { get; init; } = Console.Out;
    public TextWriter Error { get; init; } = Console.Error;
    public TextReader In { get; init; } = Console.In;
    public bool IsInputRedirected { get; init; } = Console.IsInputRedirected;
    public HttpClient? HttpClient { get; init; }
}
