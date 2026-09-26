using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TypeSafe.Mcp.Tools;

[McpServerToolType]
public sealed class TypeSafeTools(TypeSafeEvaluator evaluator)
{
    internal const string ResultKey = "result";

    private const string StateDescription =
        "The state to evaluate: plain text, or a JSON object/array (sent as structured JSON). " +
        "Include everything the judgement depends on.";

    private const string InstructionsDescription =
        "What to judge about the state. Instructions may reference state fields with backticked paths, e.g. `ticket.body`.";

    private const string ModelDescription =
        "Optional model name (see typesafe_list_models). Defaults to the server's configured model (jev-latest).";

    [McpServerTool(Name = "typesafe_noul", Title = "TypeSafe Noul (yes/no probability)", ReadOnly = true,
        Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Asks TypeSafe System One a yes/no question about a state and returns a calibrated probability (0-1) that the " +
        "statement is true. Use for binary checks such as 'is this message spam?'. A noul near 1 means true, near 0 " +
        "means false and near 0.5 means undecided. The answer is keyed 'result'.")]
    public Task<EvaluationResult> Noul(
        [Description(StateDescription)] string state,
        [Description(InstructionsDescription + " Phrase it as a statement or yes/no question.")] string instructions,
        [Description("Optional description of what makes the statement true.")] string? trueCriteria = null,
        [Description("Optional description of what makes the statement false.")] string? falseCriteria = null,
        [Description(ModelDescription)] string? model = null,
        CancellationToken cancellationToken = default)
    {
        var question = TypeSafeEvaluator.Build(() => QuestionFactory.Noul(instructions, trueCriteria, falseCriteria));
        return evaluator.EvaluateAsync(state, Single(question), model, cancellationToken);
    }

    [McpServerTool(Name = "typesafe_choice", Title = "TypeSafe Choice (classification)", ReadOnly = true,
        Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Asks TypeSafe System One to pick exactly one label from an unordered set of options (classification, routing, " +
        "intent detection). Returns the chosen label, its confidence and a probability per label. The answer is keyed 'result'.")]
    public Task<EvaluationResult> Choice(
        [Description(StateDescription)] string state,
        [Description(InstructionsDescription)] string instructions,
        [Description("Options as label -> description of when that label applies. At least one entry; labels must be unique and non-empty.")]
        Dictionary<string, string> choices,
        [Description(ModelDescription)] string? model = null,
        CancellationToken cancellationToken = default)
    {
        var question = TypeSafeEvaluator.Build(() => QuestionFactory.Choice(instructions, choices));
        return evaluator.EvaluateAsync(state, Single(question), model, cancellationToken);
    }

    [McpServerTool(Name = "typesafe_score", Title = "TypeSafe Score (ordered rubric)", ReadOnly = true,
        Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Asks TypeSafe System One to place a state on an ordered rubric (severity, urgency, sentiment, quality...). " +
        "Returns a probability-weighted score, a confidence, a legend mapping score values to levels and a probability " +
        "per level. The answer is keyed 'result'.")]
    public Task<EvaluationResult> Score(
        [Description(StateDescription)] string state,
        [Description(InstructionsDescription)] string instructions,
        [Description("Rubric levels ordered from lowest to highest, each describing that level. At least two entries.")]
        string[] levels,
        [Description(ModelDescription)] string? model = null,
        CancellationToken cancellationToken = default)
    {
        var question = TypeSafeEvaluator.Build(() => QuestionFactory.Score(instructions, levels));
        return evaluator.EvaluateAsync(state, Single(question), model, cancellationToken);
    }

    [McpServerTool(Name = "typesafe_evaluate", Title = "TypeSafe evaluate (multiple questions)", ReadOnly = true,
        Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Asks TypeSafe System One any mix of named noul, choice and score questions about the same state in a single " +
        "request (evaluated in parallel; cheaper and faster than separate calls). Returns one typed answer per question " +
        "name: noul -> probability; choice -> label, confidence, probabilities; score -> score, confidence, legend, probabilities.")]
    public Task<EvaluationResult> Evaluate(
        [Description(StateDescription)] string state,
        [Description("Questions keyed by a unique name of your choice (e.g. 'is_spam', 'topic', 'urgency'). At least one.")]
        Dictionary<string, EvaluateQuestion> questions,
        [Description(ModelDescription)] string? model = null,
        CancellationToken cancellationToken = default)
    {
        var mapped = TypeSafeEvaluator.Build(() =>
        {
            if (questions is null || questions.Count == 0)
                throw new TypeSafeError("At least one question is required.");
            var result = new Dictionary<string, Question>();
            foreach (var (name, question) in questions)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new TypeSafeError("Question names cannot be empty.");
                if (question is null)
                    throw new TypeSafeError($"Question '{name}' cannot be null.");
                result[name] = question.ToQuestion(name);
            }
            return result;
        });
        return evaluator.EvaluateAsync(state, mapped, model, cancellationToken);
    }

    [McpServerTool(Name = "typesafe_list_models", Title = "TypeSafe models", ReadOnly = true,
        Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists the TypeSafe models available to the configured API key (name, description, release date).")]
    public Task<IReadOnlyList<ModelInfo>> ListModels(CancellationToken cancellationToken = default) =>
        evaluator.ListModelsAsync(cancellationToken);

    private static Dictionary<string, Question> Single(Question question) => new() { [ResultKey] = question };
}
