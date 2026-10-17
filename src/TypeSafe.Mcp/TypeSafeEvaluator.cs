using ModelContextProtocol;

namespace TypeSafe.Mcp;

public sealed class TypeSafeEvaluator(TypeSafeClientProvider provider)
{
    public async Task<EvaluationResult> EvaluateAsync(
        string state,
        IReadOnlyDictionary<string, Question> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new McpException("state cannot be empty.");
        if (questions is null || questions.Count == 0)
            throw new McpException("At least one question is required.");

        var client = provider.GetClient();
        var request = new SystemOneRequest
        {
            State = StateParser.Parse(state),
            Questions = questions,
            Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim()
        };

        var result = await Call(() => client.SystemOneAsync(request, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return Map(result);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var client = provider.GetClient();
        var models = await Call(() => client.ModelsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        return models.Select(card => new ModelInfo(card.Name, card.Description, card.ReleaseDate)).ToList();
    }

    internal static async Task<T> Call<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (ApiError exception)
        {
            var requestId = exception.RequestId is null ? "" : $" (request id: {exception.RequestId})";
            throw new McpException($"TypeSafe API error: {exception.Message}{requestId}", exception);
        }
        catch (TypeSafeError exception)
        {
            throw new McpException($"TypeSafe error: {exception.Message}", exception);
        }
    }

    internal static T Build<T>(Func<T> factory)
    {
        try
        {
            return factory();
        }
        catch (TypeSafeError exception)
        {
            throw new McpException($"Invalid question: {exception.Message}", exception);
        }
    }

    internal static EvaluationResult Map(SystemOneResult result) =>
        new(
            result.Model,
            result.Answers.ToDictionary(pair => pair.Key, pair => Map(pair.Value)),
            new UsageResult(result.Usage.InputTokens, result.Usage.OutputTokens));

    private static AnswerResult Map(Answer answer) => answer switch
    {
        NoulAnswer noul => new AnswerResult(noul.Type, Noul: noul.Noul),
        ChoiceAnswer choice => new AnswerResult(
            choice.Type,
            Choice: choice.Choice,
            Confidence: choice.Confidence,
            Probabilities: choice.Probabilities),
        ScoreAnswer score => new AnswerResult(
            score.Type,
            Score: score.Score,
            Confidence: score.Confidence,
            Probabilities: score.Probabilities,
            Legend: score.Legend),
        BoxAnswer box => new AnswerResult(box.Type, Confidence: box.Confidence, Box: box.Box)
    };
}
