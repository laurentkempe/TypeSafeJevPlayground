using System.CommandLine;

namespace TypeSafe.Cli.Commands;

public static class BoxCommand
{
    public static Command Create(CliContext context, GlobalOptionDefinitions globals)
    {
        var command = new Command(
            "box",
            "Locate something in an image and get a normalized bounding box (experimental; defaults to jev-preview).");

        var stateArg = new Argument<string?>("state")
        {
            Description = "State to evaluate: @imagepath (PNG/JPEG), text/JSON payload referencing --image assets, or '-' for standard input.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var instructionsArg = new Argument<string?>("instructions")
        {
            Description = "What to locate in the image.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var imageOption = SystemOneRunner.CreateImageOption();

        command.Add(stateArg);
        command.Add(instructionsArg);
        command.Add(imageOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);
            var stateRaw = parseResult.GetValue(stateArg);
            var instructionsRaw = parseResult.GetValue(instructionsArg);
            var imageArguments = parseResult.GetValue(imageOption) ?? [];

            return await CommandExecutor.ExecuteAsync(context, globalOptions, async (client, ct) =>
            {
                string? effectiveState = stateRaw;
                string? effectiveInstructions = instructionsRaw;

                if (string.IsNullOrWhiteSpace(effectiveInstructions) && !string.IsNullOrWhiteSpace(effectiveState) && context.IsInputRedirected && effectiveState != "-")
                {
                    effectiveInstructions = effectiveState;
                    effectiveState = "-";
                }

                if (string.IsNullOrWhiteSpace(effectiveInstructions))
                {
                    throw new CliValidationException("Instructions argument is required.");
                }

                var question = QuestionFactory.Box(effectiveInstructions);
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
