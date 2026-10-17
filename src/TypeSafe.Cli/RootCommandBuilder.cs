using System.CommandLine;
using TypeSafe.Cli.Commands;

namespace TypeSafe.Cli;

public static class RootCommandBuilder
{
    public static RootCommand Build(CliContext? context = null)
    {
        var effectiveContext = context ?? new CliContext();
        var rootCommand = new RootCommand("TypeSafe CLI tool for System One evaluations and model inspection.");

        var globals = new GlobalOptionDefinitions();
        globals.AddToCommand(rootCommand);

        rootCommand.Add(NoulCommand.Create(effectiveContext, globals));
        rootCommand.Add(ChoiceCommand.Create(effectiveContext, globals));
        rootCommand.Add(ScoreCommand.Create(effectiveContext, globals));
        rootCommand.Add(EvaluateCommand.Create(effectiveContext, globals));
        rootCommand.Add(ModelsCommand.Create(effectiveContext, globals));

        return rootCommand;
    }
}
