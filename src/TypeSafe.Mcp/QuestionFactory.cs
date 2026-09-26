namespace TypeSafe.Mcp;

internal static class QuestionFactory
{
    public static NoulQuestion Noul(string? instructions, string? trueCriteria, string? falseCriteria)
    {
        RequireInstructions(instructions);
        var criteria = trueCriteria is null && falseCriteria is null
            ? null
            : new NoulCriteria { True = trueCriteria, False = falseCriteria };
        return Questions.Noul(instructions, criteria);
    }

    public static ChoiceQuestion Choice(string? instructions, IReadOnlyDictionary<string, string>? choices)
    {
        RequireInstructions(instructions);
        if (choices is null || choices.Count == 0)
            throw new TypeSafeError("Choice criteria cannot be empty.");
        return Questions.Choice(
            instructions,
            choices.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
    }

    public static ScoreQuestion Score(string? instructions, IReadOnlyList<string>? levels)
    {
        RequireInstructions(instructions);
        if (levels is null || levels.Count < 2)
            throw new TypeSafeError("Score criteria must contain at least two entries.");
        return Questions.Score(instructions, levels.Select(level => (object?)level).ToList());
    }

    private static void RequireInstructions(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            throw new TypeSafeError("instructions cannot be empty.");
    }
}
