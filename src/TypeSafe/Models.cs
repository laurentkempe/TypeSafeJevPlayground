using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafe;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public closed record Question;

public sealed record NoulCriteria
{
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? True { get; init; }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? False { get; init; }
}

public sealed record NoulQuestion : Question
{
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Instructions { get; init; }

    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}

public sealed record ChoiceQuestion : Question
{
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required object? Instructions { get; init; }

    [JsonPropertyName("criteria")]
    public required IReadOnlyDictionary<string, object?> Criteria { get; init; }
}

public sealed record ScoreQuestion : Question
{
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required object? Instructions { get; init; }

    [JsonPropertyName("criteria")]
    public required IReadOnlyList<object?> Criteria { get; init; }
}

public static class Questions
{
    public static NoulQuestion Noul(object? instructions = null, NoulCriteria? criteria = null)
    {
        EntryType.Validate(instructions, nameof(instructions));
        if (criteria is not null)
        {
            EntryType.Validate(criteria.True, "criteria.true");
            EntryType.Validate(criteria.False, "criteria.false");
        }

        return new NoulQuestion { Instructions = instructions, Criteria = criteria };
    }

    public static ChoiceQuestion Choice(
        object? instructions,
        IReadOnlyDictionary<string, object?> criteria)
    {
        EntryType.Validate(instructions, nameof(instructions));
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0)
            throw new TypeSafeError("Choice criteria cannot be empty.");
        foreach (var (key, value) in criteria)
        {
            if (string.IsNullOrEmpty(key))
                throw new TypeSafeError("Choice criterion names cannot be empty.");
            EntryType.Validate(value, $"criteria.{key}");
        }

        return new ChoiceQuestion { Instructions = instructions, Criteria = criteria };
    }

    public static ScoreQuestion Score(object? instructions, IReadOnlyList<object?> criteria)
    {
        EntryType.Validate(instructions, nameof(instructions));
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count < 2)
            throw new TypeSafeError("Score criteria must contain at least two entries.");
        for (var index = 0; index < criteria.Count; index++)
            EntryType.Validate(criteria[index], $"criteria[{index}]");

        return new ScoreQuestion { Instructions = instructions, Criteria = criteria };
    }
}

internal static class EntryType
{
    public static void Validate(object? value, string name)
    {
        if (value is null or string)
            return;

        if (value is JsonElement element &&
            element.ValueKind is JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array)
            return;

        try
        {
            var kind = JsonSerializer.SerializeToElement(value, TypeSafeClient.JsonOptions).ValueKind;
            if (kind is JsonValueKind.Object or JsonValueKind.Array)
                return;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new TypeSafeError($"{name} must be a JSON string, object, array, or null.", exception);
        }

        throw new TypeSafeError($"{name} must be a JSON string, object, array, or null.");
    }
}

public sealed record SystemOneRequest
{
    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required object? State { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyDictionary<string, Question> Questions { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? AdditionalProperties { get; init; }
}

public closed record Answer
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public sealed record NoulAnswer : Answer
{
    public override string Type => "noul";

    [JsonPropertyName("noul")]
    public required double Noul { get; init; }
}

public sealed record ChoiceAnswer : Answer
{
    public override string Type => "choice";

    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }
}

public sealed record ScoreAnswer : Answer
{
    public override string Type => "score";

    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("legend")]
    public required IReadOnlyDictionary<string, object?> Legend { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }
}

public sealed record Usage
{
    [JsonPropertyName("input_tokens")]
    public required int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required int OutputTokens { get; init; }
}

public sealed record SystemOneResult
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("answers")]
    public required IReadOnlyDictionary<string, Answer> Answers { get; init; }

    [JsonPropertyName("usage")]
    public required Usage Usage { get; init; }
}

public sealed record ModelCard
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("release_date")]
    public required string ReleaseDate { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SystemOneRequest))]
[JsonSerializable(typeof(SystemOneResult))]
[JsonSerializable(typeof(IReadOnlyList<ModelCard>))]
[JsonSerializable(typeof(List<ModelCard>))]
[JsonSerializable(typeof(Question))]
[JsonSerializable(typeof(NoulQuestion))]
[JsonSerializable(typeof(ChoiceQuestion))]
[JsonSerializable(typeof(ScoreQuestion))]
[JsonSerializable(typeof(NoulCriteria))]
[JsonSerializable(typeof(Answer))]
[JsonSerializable(typeof(NoulAnswer))]
[JsonSerializable(typeof(ChoiceAnswer))]
[JsonSerializable(typeof(ScoreAnswer))]
[JsonSerializable(typeof(Usage))]
public partial class TypeSafeJsonSerializerContext : JsonSerializerContext
{
}
