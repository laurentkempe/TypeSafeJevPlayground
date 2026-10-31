using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafe.Cli;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EvaluationResult))]
[JsonSerializable(typeof(AnswerResult))]
[JsonSerializable(typeof(UsageResult))]
[JsonSerializable(typeof(BoxCoordinates))]
[JsonSerializable(typeof(ModelCard))]
[JsonSerializable(typeof(IReadOnlyList<ModelCard>))]
[JsonSerializable(typeof(List<ModelCard>))]
[JsonSerializable(typeof(EvaluateQuestion))]
[JsonSerializable(typeof(Dictionary<string, EvaluateQuestion>))]
[JsonSerializable(typeof(Dictionary<string, Question>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
public partial class TypeSafeCliJsonSerializerContext : JsonSerializerContext
{
}
