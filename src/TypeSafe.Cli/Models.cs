using System.Text.Json.Serialization;

namespace TypeSafe.Cli;

public sealed record EvaluationResult(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("answers")] IReadOnlyDictionary<string, AnswerResult> Answers,
    [property: JsonPropertyName("usage")] UsageResult Usage);

public sealed record AnswerResult(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("noul")] double? Noul = null,
    [property: JsonPropertyName("choice")] string? Choice = null,
    [property: JsonPropertyName("score")] double? Score = null,
    [property: JsonPropertyName("confidence"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Confidence = null,
    [property: JsonPropertyName("probabilities")] IReadOnlyDictionary<string, double>? Probabilities = null,
    [property: JsonPropertyName("legend")] IReadOnlyDictionary<string, object?>? Legend = null,
    [property: JsonPropertyName("box")] BoxCoordinates? Box = null);

public sealed record UsageResult(
    [property: JsonPropertyName("input_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? InputTokens,
    [property: JsonPropertyName("output_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? OutputTokens);
