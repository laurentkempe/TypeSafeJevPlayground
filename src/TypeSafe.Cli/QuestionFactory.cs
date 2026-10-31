namespace TypeSafe.Cli;

public static class QuestionFactory
{
    public static NoulQuestion Noul(string? instructions, string? trueCriteria, string? falseCriteria)
    {
        RequireInstructions(instructions);
        var criteria = trueCriteria is null && falseCriteria is null
            ? null
            : new NoulCriteria { True = trueCriteria, False = falseCriteria };
        return Questions.Noul(instructions!, criteria);
    }

    public static ChoiceQuestion Choice(string? instructions, IReadOnlyDictionary<string, string>? choices)
    {
        RequireInstructions(instructions);
        if (choices is null || choices.Count == 0)
            throw new TypeSafeError("Choice criteria cannot be empty.");
        return Questions.Choice(
            instructions!,
            choices.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
    }

    public static ChoiceQuestion ParseChoice(string? instructions, IEnumerable<string> choiceArguments)
    {
        RequireInstructions(instructions);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in choiceArguments)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;

            int separatorIndex = entry.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = entry.IndexOf(':');
            }

            if (separatorIndex <= 0 || separatorIndex == entry.Length - 1)
            {
                throw new CliValidationException($"Invalid choice '{entry}'. Expected format 'label=description'.");
            }

            var label = entry[..separatorIndex].Trim();
            var description = entry[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(label))
            {
                throw new CliValidationException($"Invalid choice '{entry}'. Label cannot be empty.");
            }

            dict[label] = description;
        }

        if (dict.Count == 0)
        {
            throw new CliValidationException("At least one choice (--choice label=description) is required.");
        }

        return Choice(instructions, dict);
    }

    public static ScoreQuestion Score(string? instructions, IReadOnlyList<string>? levels)
    {
        RequireInstructions(instructions);
        if (levels is null || levels.Count < 2)
            throw new CliValidationException("Score criteria must contain at least two levels.");
        return Questions.Score(instructions!, levels.Select(level => (object?)level).ToList());
    }

    public static BoxQuestion Box(string? instructions)
    {
        RequireInstructions(instructions);
        return Questions.Box(instructions);
    }

    private static void RequireInstructions(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            throw new CliValidationException("instructions cannot be empty.");
    }
}
