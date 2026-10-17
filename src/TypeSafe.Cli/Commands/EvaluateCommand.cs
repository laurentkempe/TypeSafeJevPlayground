using System.CommandLine;
using System.Text.Json;
using TypeSafe.Cli.Formatting;
using TypeSafe.Cli.IO;

namespace TypeSafe.Cli.Commands;

public static class EvaluateCommand
{
    public static Command Create(CliContext context, GlobalOptionDefinitions globals)
    {
        var command = new Command("evaluate", "Ask any mix of named noul, choice, score and box questions in a single request.");

        var stateArg = new Argument<string?>("state")
        {
            Description = "State payload to evaluate, @filepath, or '-' (or omitted when piped) for standard input.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var fileOption = new Option<string?>("--file", "-f")
        {
            Description = "Path to JSON file containing questions dictionary."
        };

        var questionsOption = new Option<string?>("--questions", "-q")
        {
            Description = "Inline JSON string containing questions dictionary."
        };

        var imageOption = SystemOneRunner.CreateImageOption();

        command.Add(stateArg);
        command.Add(fileOption);
        command.Add(questionsOption);
        command.Add(imageOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var globalOptions = globals.Extract(parseResult);
            var stateRaw = parseResult.GetValue(stateArg);
            var filePath = parseResult.GetValue(fileOption);
            var inlineQuestions = parseResult.GetValue(questionsOption);
            var imageArguments = parseResult.GetValue(imageOption) ?? [];

            return await CommandExecutor.ExecuteAsync(context, globalOptions, async (client, ct) =>
            {
                var isRedirected = context.IsInputRedirected;
                string? effectiveState = stateRaw;

                if (string.IsNullOrWhiteSpace(effectiveState) && isRedirected)
                {
                    effectiveState = "-";
                }

                if (string.IsNullOrWhiteSpace(effectiveState))
                {
                    throw new CliValidationException("State argument is required or must be provided via standard input.");
                }

                string questionsJson;
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    OutputFormatter.WriteVerbose($"Reading questions from file: {filePath}", globalOptions.Verbose, context.Error);
                    questionsJson = await InputReader.ReadFileOrTextAsync(filePath, ct).ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(inlineQuestions))
                {
                    questionsJson = inlineQuestions;
                }
                else
                {
                    throw new CliValidationException("Questions must be provided using --file <path> or --questions <json>.");
                }

                Dictionary<string, Question> questions;
                try
                {
                    var evalQuestions = JsonSerializer.Deserialize(
                        questionsJson,
                        TypeSafeCliJsonSerializerContext.Default.DictionaryStringEvaluateQuestion);

                    if (evalQuestions is null || evalQuestions.Count == 0)
                    {
                        throw new CliValidationException("Questions payload cannot be empty.");
                    }

                    questions = evalQuestions.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToQuestion(pair.Key));
                }
                catch (JsonException ex)
                {
                    throw new CliValidationException($"Failed to parse questions JSON: {ex.Message}", ex);
                }

                await SystemOneRunner.RunAsync(context, globalOptions, client, effectiveState, questions, imageArguments, ct)
                    .ConfigureAwait(false);
            }, cancellationToken);
        });

        return command;
    }
}
