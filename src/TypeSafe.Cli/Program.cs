using System.CommandLine;

namespace TypeSafe.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = RootCommandBuilder.Build();
        var parseResult = rootCommand.Parse(args, RootCommandBuilder.ParserConfiguration);
        var exitCode = await parseResult.InvokeAsync();
        return parseResult.Errors.Count > 0 ? 2 : exitCode;
    }
}
