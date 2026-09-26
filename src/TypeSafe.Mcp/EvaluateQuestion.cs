using System.ComponentModel;

namespace TypeSafe.Mcp;

[Description("One question for typesafe_evaluate. Fill only the fields that match its type.")]
public sealed record EvaluateQuestion(
    [property: Description("Question type: 'noul' (yes/no probability), 'choice' (one label from a set) or 'score' (ordered rubric).")]
    string Type,
    [property: Description("What to judge about the state. May reference state fields with backticked paths, e.g. `ticket.body`.")]
    string Instructions,
    [property: Description("noul only, optional: what makes the statement true.")]
    string? TrueCriteria = null,
    [property: Description("noul only, optional: what makes the statement false.")]
    string? FalseCriteria = null,
    [property: Description("choice only, required: label -> description of when that label applies.")]
    Dictionary<string, string>? Choices = null,
    [property: Description("score only, required: rubric levels ordered from lowest to highest (at least two).")]
    string[]? Levels = null)
{
    internal Question ToQuestion(string name)
    {
        try
        {
            return Type?.Trim().ToLowerInvariant() switch
            {
                "noul" => QuestionFactory.Noul(Instructions, TrueCriteria, FalseCriteria),
                "choice" => QuestionFactory.Choice(Instructions, Choices),
                "score" => QuestionFactory.Score(Instructions, Levels),
                _ => throw new TypeSafeError($"unknown type '{Type}'. Use 'noul', 'choice' or 'score'.")
            };
        }
        catch (TypeSafeError exception)
        {
            throw new TypeSafeError($"Question '{name}': {exception.Message}", exception);
        }
    }
}
