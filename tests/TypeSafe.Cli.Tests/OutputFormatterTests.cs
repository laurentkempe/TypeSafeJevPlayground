using System.Text.Json;
using TypeSafe.Cli.Formatting;
using Xunit;

namespace TypeSafe.Cli.Tests;

public sealed class OutputFormatterTests
{
    [Fact]
    public void WriteEvaluation_HumanMode_Noul()
    {
        var result = new EvaluationResult(
            "jev-latest",
            new Dictionary<string, AnswerResult>
            {
                ["result"] = new("noul", Noul: 0.874)
            },
            new UsageResult(100, 20));

        using var sw = new StringWriter();
        OutputFormatter.WriteEvaluation(result, jsonOutput: false, stdout: sw);
        var output = sw.ToString();

        Assert.Contains("TypeSafe System One Evaluation", output);
        Assert.Contains("Model: jev-latest", output);
        Assert.Contains("Tokens: 100 in / 20 out", output);
        Assert.Contains("Result: True (87.4%)", output);
    }

    [Fact]
    public void WriteEvaluation_HumanMode_Choice()
    {
        var result = new EvaluationResult(
            "jev-latest",
            new Dictionary<string, AnswerResult>
            {
                ["result"] = new("choice",
                    Choice: "auth",
                    Confidence: 0.95,
                    Probabilities: new Dictionary<string, double>
                    {
                        ["auth"] = 0.95,
                        ["db"] = 0.05
                    })
            },
            new UsageResult(50, 10));

        using var sw = new StringWriter();
        OutputFormatter.WriteEvaluation(result, jsonOutput: false, stdout: sw);
        var output = sw.ToString();

        Assert.Contains("Choice: auth (Confidence: 95.0%)", output);
        Assert.Contains("• auth: 95.0%", output);
        Assert.Contains("• db: 5.0%", output);
    }

    [Fact]
    public void WriteEvaluation_HumanMode_Score()
    {
        var result = new EvaluationResult(
            "jev-latest",
            new Dictionary<string, AnswerResult>
            {
                ["result"] = new("score",
                    Score: 2.85,
                    Confidence: 0.89,
                    Legend: new Dictionary<string, object?>
                    {
                        ["1"] = "low",
                        ["2"] = "medium",
                        ["3"] = "high"
                    },
                    Probabilities: new Dictionary<string, double>
                    {
                        ["1"] = 0.05,
                        ["2"] = 0.10,
                        ["3"] = 0.85
                    })
            },
            new UsageResult(75, 15));

        using var sw = new StringWriter();
        OutputFormatter.WriteEvaluation(result, jsonOutput: false, stdout: sw);
        var output = sw.ToString();

        Assert.Contains("Score: 2.85 (Confidence: 89.0%)", output);
        Assert.Contains("• 1: low", output);
        Assert.Contains("• 3: 85.0%", output);
    }

    [Fact]
    public void WriteEvaluation_JsonMode_OutputsValidJson()
    {
        var result = new EvaluationResult(
            "jev-latest",
            new Dictionary<string, AnswerResult>
            {
                ["result"] = new("noul", Noul: 0.92)
            },
            new UsageResult(120, 10));

        using var sw = new StringWriter();
        OutputFormatter.WriteEvaluation(result, jsonOutput: true, stdout: sw);
        var json = sw.ToString();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("jev-latest", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.92, doc.RootElement.GetProperty("answers").GetProperty("result").GetProperty("noul").GetDouble(), 2);
    }

    [Fact]
    public void WriteModels_HumanAndJson()
    {
        var models = new List<ModelCard>
        {
            new() { Name = "jev-latest", Description = "Latest model", ReleaseDate = "2026-08-01" }
        };

        using var swHuman = new StringWriter();
        OutputFormatter.WriteModels(models, jsonOutput: false, stdout: swHuman);
        Assert.Contains("jev-latest (Released: 2026-08-01)", swHuman.ToString());

        using var swJson = new StringWriter();
        OutputFormatter.WriteModels(models, jsonOutput: true, stdout: swJson);
        using var doc = JsonDocument.Parse(swJson.ToString());
        Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array);
        Assert.Equal("jev-latest", doc.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public void WriteError_WritesToStderr()
    {
        using var swErr = new StringWriter();
        OutputFormatter.WriteError("Sample error message", jsonOutput: false, stderr: swErr);
        Assert.Equal($"Error: Sample error message{Environment.NewLine}", swErr.ToString());
    }
}
