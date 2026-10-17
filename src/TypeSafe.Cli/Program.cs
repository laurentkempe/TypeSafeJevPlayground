using System.CommandLine;

namespace TypeSafe.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = RootCommandBuilder.Build();
        return await rootCommand.Parse(args).InvokeAsync();
    }
}
