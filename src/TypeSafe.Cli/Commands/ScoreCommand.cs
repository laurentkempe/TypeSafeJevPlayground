using System.CommandLine;

namespace TypeSafe.Cli.Commands;

public static class ScoreCommand
{
    public static Command Create(CliContext context, GlobalOptionDefinitions globals)
    {
        var command = new Command("score", "Place a state on an ordered rubric (severity, urgency, quality, etc.).");

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

        var levelOption = new Option<string[]>("--level", "-l")
        {
            Description = "Rubric levels ordered from lowest to highest. Pass multiple times (at least two required).",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.OneOrMore
        };

        var imageOption = SystemOneRunner.CreateImageOption();

        command.Add(stateArg);
        command.Add(instructionsArg);
        command.Add(levelOption);
        command.Add(imageOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);
            var stateRaw = parseResult.GetValue(stateArg);
            var instructionsRaw = parseResult.GetValue(instructionsArg);
            var levelArguments = parseResult.GetValue(levelOption) ?? [];
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

                if (levelArguments.Length < 2)
                {
                    throw new CliValidationException("At least two rubric levels (--level / -l) are required.");
                }

                var question = QuestionFactory.Score(effectiveInstructions, levelArguments);
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
