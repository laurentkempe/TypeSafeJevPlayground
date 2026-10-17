using System.CommandLine;
using TypeSafe.Cli.Formatting;
using TypeSafe.Cli.IO;

namespace TypeSafe.Cli.Commands;

public static class SystemOneRunner
{
    public const string ImagesHeader = "TypeSafe-Danger-Zone-Images";
    public const string BoxModel = "jev-preview";

    public static Option<string[]> CreateImageOption() => new("--image", "-i")
    {
        Description = "Attach an image asset in 'name=path' (or 'path') format, referenced as <name> in the state, instructions or criteria. Pass multiple times. Enables the experimental TypeSafe images API.",
        AllowMultipleArgumentsPerToken = true,
        Arity = ArgumentArity.ZeroOrMore
    };

    public static async Task RunAsync(
        CliContext context,
        CliGlobalOptions globalOptions,
        TypeSafeClient client,
        string? stateArgument,
        IReadOnlyDictionary<string, Question> questions,
        IReadOnlyList<string> imageArguments,
        CancellationToken cancellationToken)
    {
        OutputFormatter.WriteVerbose("Reading state input...", globalOptions.Verbose, context.Error);
        var state = await InputReader.ReadStateValueAsync(stateArgument, context.In, context.IsInputRedirected, cancellationToken).ConfigureAwait(false);
        var assets = await ImageInput.ReadAssetsAsync(imageArguments, cancellationToken).ConfigureAwait(false);

        var usesImages = state is Image || assets.Count > 0;
        var model = ResolveModel(globalOptions, questions);

        var request = new SystemOneRequest
        {
            State = state,
            Questions = questions,
            Model = model,
            Assets = assets.Count > 0 ? assets : null
        };

        RequestOptions? requestOptions = null;
        if (usesImages)
        {
            OutputFormatter.WriteVerbose($"Image input detected; sending {ImagesHeader}: true.", globalOptions.Verbose, context.Error);
            requestOptions = new RequestOptions
            {
                Headers = new Dictionary<string, string> { [ImagesHeader] = "true" }
            };
        }

        OutputFormatter.WriteVerbose(
            $"Sending evaluation request with {questions.Count} question(s) to TypeSafe API...",
            globalOptions.Verbose,
            context.Error);
        var result = await client.SystemOneAsync(request, requestOptions, cancellationToken).ConfigureAwait(false);
        OutputFormatter.WriteEvaluation(ResultMapper.Map(result), globalOptions.Json, context.Out);
    }

    private static string? ResolveModel(
        CliGlobalOptions globalOptions,
        IReadOnlyDictionary<string, Question> questions)
    {
        if (!string.IsNullOrWhiteSpace(globalOptions.Model))
            return globalOptions.Model;

        // Box questions are only supported by jev-preview, so use it unless a model was configured explicitly.
        var configuredModel = Environment.GetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL");
        if (string.IsNullOrWhiteSpace(configuredModel) && questions.Values.Any(question => question is BoxQuestion))
            return BoxModel;

        return null;
    }
}
