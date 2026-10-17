using System.CommandLine;
using TypeSafe.Cli.Commands;

namespace TypeSafe.Cli;

public static class RootCommandBuilder
{
    // '@path' is the CLI's own file-input syntax (state, questions, images), so response files are disabled.
    public static ParserConfiguration ParserConfiguration { get; } = new() { ResponseFileTokenReplacer = null };

    public static RootCommand Build(CliContext? context = null)
    {
        var effectiveContext = context ?? new CliContext();
        var rootCommand = new RootCommand("TypeSafe CLI tool for System One evaluations and model inspection.");

        var globals = new GlobalOptionDefinitions();
        globals.AddToCommand(rootCommand);

        rootCommand.Add(NoulCommand.Create(effectiveContext, globals));
        rootCommand.Add(ChoiceCommand.Create(effectiveContext, globals));
        rootCommand.Add(ScoreCommand.Create(effectiveContext, globals));
        rootCommand.Add(BoxCommand.Create(effectiveContext, globals));
        rootCommand.Add(EvaluateCommand.Create(effectiveContext, globals));
        rootCommand.Add(ModelsCommand.Create(effectiveContext, globals));

        return rootCommand;
    }
}
