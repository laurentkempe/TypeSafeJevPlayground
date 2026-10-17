using System.Text.Json.Serialization;

namespace TypeSafe.Cli;

public sealed record EvaluateQuestion
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("instructions")]
    public required string Instructions { get; init; }

    [JsonPropertyName("true_criteria")]
    [JsonInclude]
    public string? TrueCriteriaSnake { get; init; }

    [JsonPropertyName("trueCriteria")]
    [JsonInclude]
    public string? TrueCriteriaCamel { get; init; }

    [JsonIgnore]
    public string? TrueCriteria => TrueCriteriaSnake ?? TrueCriteriaCamel;

    [JsonPropertyName("false_criteria")]
    [JsonInclude]
    public string? FalseCriteriaSnake { get; init; }

    [JsonPropertyName("falseCriteria")]
    [JsonInclude]
    public string? FalseCriteriaCamel { get; init; }

    [JsonIgnore]
    public string? FalseCriteria => FalseCriteriaSnake ?? FalseCriteriaCamel;

    [JsonPropertyName("choices")]
    public Dictionary<string, string>? Choices { get; init; }

    [JsonPropertyName("levels")]
    public List<string>? Levels { get; init; }

    public Question ToQuestion(string name)
    {
        try
        {
            return Type?.Trim().ToLowerInvariant() switch
            {
                "noul" => QuestionFactory.Noul(Instructions, TrueCriteria, FalseCriteria),
                "choice" => QuestionFactory.Choice(Instructions, Choices),
                "score" => QuestionFactory.Score(Instructions, Levels),
                _ => throw new TypeSafeError($"unknown type '{Type}'. Expected 'noul', 'choice' or 'score'.")
            };
        }
        catch (TypeSafeError exception)
        {
            throw new TypeSafeError($"Question '{name}': {exception.Message}", exception);
        }
    }
}
