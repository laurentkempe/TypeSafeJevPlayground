using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace TypeSafe.Mcp;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EvaluationResult))]
[JsonSerializable(typeof(AnswerResult))]
[JsonSerializable(typeof(UsageResult))]
[JsonSerializable(typeof(ModelInfo))]
[JsonSerializable(typeof(IReadOnlyList<ModelInfo>))]
[JsonSerializable(typeof(EvaluateQuestion))]
[JsonSerializable(typeof(Dictionary<string, EvaluateQuestion>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class McpJsonContext : JsonSerializerContext;

internal static class McpToolJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Insert(0, McpJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }
}
