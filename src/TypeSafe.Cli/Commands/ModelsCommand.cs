using System.CommandLine;
using TypeSafe.Cli.Formatting;

namespace TypeSafe.Cli.Commands;

public static class ModelsCommand
{
    public static Command Create(CliContext context, GlobalOptionDefinitions globals)
    {
        var command = new Command("models", "List the TypeSafe AI models available to your API key.");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);

            return await CommandExecutor.ExecuteAsync(context, globalOptions, async (client, ct) =>
            {
                OutputFormatter.WriteVerbose("Fetching available TypeSafe models...", globalOptions.Verbose, context.Error);
                var models = await client.ModelsAsync(cancellationToken: ct).ConfigureAwait(false);
                OutputFormatter.WriteModels(models, globalOptions.Json, context.Out);
            }, cancellationToken);
        });

        return command;
    }
}
