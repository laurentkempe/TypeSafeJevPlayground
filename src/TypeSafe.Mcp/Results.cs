using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TypeSafe.Mcp;

[Description("Result of a TypeSafe System One evaluation.")]
public sealed record EvaluationResult(
    [property: Description("Model that produced the answers.")] string Model,
    [property: Description("Answers keyed by question name.")] IReadOnlyDictionary<string, AnswerResult> Answers,
    [property: Description("Token usage of the request.")] UsageResult Usage);

[Description("Answer to a single question. Fields depend on the question type.")]
public sealed record AnswerResult(
    [property: Description("Question type: noul, choice, score or box.")] string Type,
    [property: Description("noul only: probability (0-1) that the statement is true; near 0.5 means undecided.")] double? Noul = null,
    [property: Description("choice only: the most likely label.")] string? Choice = null,
    [property: Description("score only: probability-weighted position on the rubric.")] double? Score = null,
    [property: Description("choice/score: confidence of the answer (0-1).")] double? Confidence = null,
    [property: Description("choice/score: probability per label or level.")] IReadOnlyDictionary<string, double>? Probabilities = null,
    [property: Description("score only: mapping between score values and rubric levels.")] IReadOnlyDictionary<string, object?>? Legend = null,
    [property: Description("box only: normalized image coordinates.")] BoxCoordinates? Box = null);

public sealed record UsageResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? InputTokens,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? OutputTokens);

public sealed record ModelInfo(string Name, string Description, string ReleaseDate);
