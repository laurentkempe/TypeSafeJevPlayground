using System.CommandLine;
using TypeSafe.Cli.Formatting;
using TypeSafe.Cli.IO;

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

        command.Add(stateArg);
        command.Add(instructionsArg);
        command.Add(choiceOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);
            var stateRaw = parseResult.GetValue(stateArg);
            var instructionsRaw = parseResult.GetValue(instructionsArg);
            var choiceArguments = parseResult.GetValue(choiceOption) ?? [];

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

                OutputFormatter.WriteVerbose($"Reading state input...", globalOptions.Verbose, context.Error);
                var stateText = await InputReader.ReadStateAsync(effectiveState, context.In, context.IsInputRedirected, ct).ConfigureAwait(false);
                var stateObj = StateParser.Parse(stateText);

                var question = QuestionFactory.ParseChoice(effectiveInstructions, choiceArguments);
                var request = new SystemOneRequest
                {
                    State = stateObj,
                    Questions = new Dictionary<string, Question> { ["result"] = question },
                    Model = globalOptions.Model
                };

                OutputFormatter.WriteVerbose($"Sending evaluation request to TypeSafe API...", globalOptions.Verbose, context.Error);
                var result = await client.SystemOneAsync(request, cancellationToken: ct).ConfigureAwait(false);
                var evalResult = ResultMapper.Map(result);

                OutputFormatter.WriteEvaluation(evalResult, globalOptions.Json, context.Out);
            }, cancellationToken);
        });

        return command;
    }
}
