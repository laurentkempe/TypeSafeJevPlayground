using System.Globalization;
using System.Text.Json;

namespace TypeSafe.Cli.Formatting;

public static class OutputFormatter
{
    public static void WriteEvaluation(
        EvaluationResult result,
        bool jsonOutput,
        TextWriter? stdout = null)
    {
        var outWriter = stdout ?? Console.Out;

        if (jsonOutput)
        {
            var json = JsonSerializer.Serialize(result, TypeSafeCliJsonSerializerContext.Default.EvaluationResult);
            outWriter.WriteLine(json);
            return;
        }

        outWriter.WriteLine("TypeSafe System One Evaluation");
        outWriter.WriteLine($"Model: {result.Model}");
        outWriter.WriteLine($"Tokens: {FormatTokens(result.Usage.InputTokens)} in / {FormatTokens(result.Usage.OutputTokens)} out");
        outWriter.WriteLine();

        if (result.Answers.Count == 1 && result.Answers.ContainsKey("result"))
        {
            WriteAnswerBody(result.Answers["result"], outWriter, indent: "");
        }
        else
        {
            foreach (var (key, answer) in result.Answers)
            {
                outWriter.WriteLine($"[{key}] ({answer.Type})");
                WriteAnswerBody(answer, outWriter, indent: "  ");
                outWriter.WriteLine();
            }
        }
    }

    public static void WriteModels(
        IReadOnlyList<ModelCard> models,
        bool jsonOutput,
        TextWriter? stdout = null)
    {
        var outWriter = stdout ?? Console.Out;

        if (jsonOutput)
        {
            var json = JsonSerializer.Serialize(models, TypeSafeCliJsonSerializerContext.Default.IReadOnlyListModelCard);
            outWriter.WriteLine(json);
            return;
        }

        outWriter.WriteLine("Available TypeSafe Models:");
        foreach (var model in models)
        {
            outWriter.WriteLine($"  • {model.Name} (Released: {model.ReleaseDate})");
            outWriter.WriteLine($"    {model.Description}");
        }
    }

    public static void WriteError(
        string message,
        bool jsonOutput = false,
        TextWriter? stderr = null)
    {
        var errWriter = stderr ?? Console.Error;
        errWriter.WriteLine($"Error: {message}");
    }

    public static void WriteVerbose(
        string message,
        bool verbose,
        TextWriter? stderr = null)
    {
        if (!verbose) return;
        var errWriter = stderr ?? Console.Error;
        errWriter.WriteLine($"[verbose] {message}");
    }

    private static string FormatTokens(int? tokens) =>
        tokens?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private static string FormatCoordinate(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static void WriteAnswerBody(AnswerResult answer, TextWriter writer, string indent)
    {
        switch (answer.Type.ToLowerInvariant())
        {
            case "noul" when answer.Noul.HasValue:
                var prob = answer.Noul.Value;
                var verdict = prob >= 0.5 ? "True" : "False";
                writer.WriteLine($"{indent}Result: {verdict} ({prob * 100:0.0}%)");
                break;

            case "choice":
                if (answer.Choice is not null)
                {
                    var confStr = answer.Confidence.HasValue ? $" (Confidence: {answer.Confidence.Value * 100:0.0}%)" : "";
                    writer.WriteLine($"{indent}Choice: {answer.Choice}{confStr}");
                }
                if (answer.Probabilities is not null && answer.Probabilities.Count > 0)
                {
                    writer.WriteLine($"{indent}Probabilities:");
                    foreach (var (lbl, p) in answer.Probabilities)
                    {
                        writer.WriteLine($"{indent}  • {lbl}: {p * 100:0.0}%");
                    }
                }
                break;

            case "score":
                if (answer.Score.HasValue)
                {
                    var confStr = answer.Confidence.HasValue ? $" (Confidence: {answer.Confidence.Value * 100:0.0}%)" : "";
                    writer.WriteLine($"{indent}Score: {answer.Score.Value.ToString("0.00", CultureInfo.InvariantCulture)}{confStr}");
                }
                if (answer.Legend is not null && answer.Legend.Count > 0)
                {
                    writer.WriteLine($"{indent}Legend:");
                    foreach (var (lvl, desc) in answer.Legend)
                    {
                        writer.WriteLine($"{indent}  • {lvl}: {desc}");
                    }
                }
                if (answer.Probabilities is not null && answer.Probabilities.Count > 0)
                {
                    writer.WriteLine($"{indent}Probabilities:");
                    foreach (var (lvl, p) in answer.Probabilities)
                    {
                        writer.WriteLine($"{indent}  • {lvl}: {p * 100:0.0}%");
                    }
                }
                break;

            case "box" when answer.Box is not null:
                var box = answer.Box;
                var boxConfidence = answer.Confidence.HasValue ? $"{answer.Confidence.Value * 100:0.0}%" : "unknown";
                writer.WriteLine($"{indent}Box: top={FormatCoordinate(box.Top)}, bottom={FormatCoordinate(box.Bottom)}, left={FormatCoordinate(box.Left)}, right={FormatCoordinate(box.Right)}");
                writer.WriteLine($"{indent}Confidence: {boxConfidence}");
                break;

            default:
                writer.WriteLine($"{indent}Type: {answer.Type}");
                break;
        }
    }
}
