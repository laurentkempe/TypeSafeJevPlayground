using System.CommandLine;
using TypeSafe.Cli.Formatting;
using TypeSafe.Cli.IO;

namespace TypeSafe.Cli.Commands;

public static class NoulCommand
{
    public static Command Create(CliContext context, GlobalOptionDefinitions globals)
    {
        var command = new Command("noul", "Ask a yes/no question about a state and get a calibrated probability (0-1).");

        var stateArg = new Argument<string?>("state")
        {
            Description = "State payload to evaluate, @filepath, or '-' (or omitted when piped) for standard input.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var instructionsArg = new Argument<string?>("instructions")
        {
            Description = "Statement or yes/no question to judge about the state.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var trueCriteriaOption = new Option<string?>("--true", "-t")
        {
            Description = "Optional description of what makes the statement true."
        };

        var falseCriteriaOption = new Option<string?>("--false", "-f")
        {
            Description = "Optional description of what makes the statement false."
        };

        command.Add(stateArg);
        command.Add(instructionsArg);
        command.Add(trueCriteriaOption);
        command.Add(falseCriteriaOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);
            var stateRaw = parseResult.GetValue(stateArg);
            var instructionsRaw = parseResult.GetValue(instructionsArg);
            var trueCriteria = parseResult.GetValue(trueCriteriaOption);
            var falseCriteria = parseResult.GetValue(falseCriteriaOption);

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

                OutputFormatter.WriteVerbose($"Reading state input...", globalOptions.Verbose, context.Error);
                var stateText = await InputReader.ReadStateAsync(effectiveState, context.In, context.IsInputRedirected, ct).ConfigureAwait(false);
                var stateObj = StateParser.Parse(stateText);

                var question = QuestionFactory.Noul(effectiveInstructions, trueCriteria, falseCriteria);
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
