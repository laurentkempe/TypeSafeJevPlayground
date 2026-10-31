using System.CommandLine;

namespace TypeSafe.Cli.Commands;

public static class ChoiceCommand
{
    public static Command Create(CliContext context, GlobalOptionDefinitions globals)
    {
        var command = new Command("choice", "Pick exactly one label from an unordered set of options (classification/routing).");

        var stateArg = new Argument<string?>("state")
        {
            Description = "State payload to evaluate, @filepath, or '-' (or omitted when piped) for standard input.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var instructionsArg = new Argument<string?>("instructions")
        {
            Description = "What to judge about the state.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var choiceOption = new Option<string[]>("--choice", "-c")
        {
            Description = "Choice option in 'label=description' format. Pass multiple times.",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore
        };

        var imageOption = SystemOneRunner.CreateImageOption();

        command.Add(stateArg);
        command.Add(instructionsArg);
        command.Add(choiceOption);
        command.Add(imageOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);
            var stateRaw = parseResult.GetValue(stateArg);
            var instructionsRaw = parseResult.GetValue(instructionsArg);
            var choiceArguments = parseResult.GetValue(choiceOption) ?? [];
            var imageArguments = parseResult.GetValue(imageOption) ?? [];

            return await CommandExecutor.ExecuteAsync(context, globalOptions, async (client, ct) =>
            {
                var isRedirected = context.IsInputRedirected;
                string? effectiveState = stateRaw;
                string? effectiveInstructions = instructionsRaw;

                if (string.IsNullOrWhiteSpace(effectiveInstructions) && !string.IsNullOrWhiteSpace(effectiveState) && isRedirected && effectiveState != "-")
                {
                    effectiveInstructions = effectiveState;
                    effectiveState = "-";
                }

                if (string.IsNullOrWhiteSpace(effectiveInstructions))
                {
                    throw new CliValidationException("Instructions argument is required.");
                }

                if (choiceArguments.Length == 0)
                {
                    throw new CliValidationException("At least one --choice (-c) option is required in 'label=description' format.");
                }

                var question = QuestionFactory.ParseChoice(effectiveInstructions, choiceArguments);
                await SystemOneRunner.RunAsync(
                    context,
                    globalOptions,
                    client,
                    effectiveState,
                    new Dictionary<string, Question> { ["result"] = question },
                    imageArguments,
                    ct).ConfigureAwait(false);
            }, cancellationToken);
        });

        return command;
    }
}
