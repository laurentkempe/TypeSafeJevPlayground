using System.Net;
using System.Text.Json;
using Xunit;

namespace TypeSafe.Cli.Tests;

public sealed class CommandsTests
{
    private const string NoulSuccessResponse = """
        {
          "model": "jev-latest",
          "answers": {
            "result": {
              "type": "noul",
              "noul": 0.88
            }
          },
          "usage": {
            "input_tokens": 42,
            "output_tokens": 5
          }
        }
        """;

    private const string ChoiceSuccessResponse = """
        {
          "model": "jev-latest",
          "answers": {
            "result": {
              "type": "choice",
              "choice": "auth",
              "confidence": 0.94,
              "probabilities": {
                "auth": 0.94,
                "db": 0.06
              }
            }
          },
          "usage": {
            "input_tokens": 60,
            "output_tokens": 10
          }
        }
        """;

    private const string ScoreSuccessResponse = """
        {
          "model": "jev-latest",
          "answers": {
            "result": {
              "type": "score",
              "score": 2.75,
              "confidence": 0.88,
              "legend": {
                "1": "low",
                "2": "medium",
                "3": "high"
              },
              "probabilities": {
                "1": 0.05,
                "2": 0.15,
                "3": 0.80
              }
            }
          },
          "usage": {
            "input_tokens": 55,
            "output_tokens": 12
          }
        }
        """;

    private const string EvaluateSuccessResponse = """
        {
          "model": "jev-latest",
          "answers": {
            "is_spam": {
              "type": "noul",
              "noul": 0.02
            },
            "category": {
              "type": "choice",
              "choice": "support",
              "confidence": 0.98,
              "probabilities": {
                "support": 0.98,
                "sales": 0.02
              }
            }
          },
          "usage": {
            "input_tokens": 120,
            "output_tokens": 25
          }
        }
        """;

    private const string ModelsSuccessResponse = """
        {
          "models": [
            {
              "name": "jev-latest",
              "description": "General purpose System One model",
              "release_date": "2026-08-01"
            }
          ]
        }
        """;

    [Fact]
    public async Task Noul_SuccessfulExecution_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(NoulSuccessResponse));

        var result = await harness.RunAsync(["noul", "The user is asking for a refund", "Is this a refund request?", "--api-key", "test-key"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TypeSafe System One Evaluation", result.StdOut);
        Assert.Contains("Result: True (88.0%)", result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Fact]
    public async Task Noul_JsonMode_OutputsStrictJson()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(NoulSuccessResponse));

        var result = await harness.RunAsync(["noul", "Some text", "Is this valid?", "--json", "--api-key", "test-key"]);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.StdOut);
        Assert.Equal("jev-latest", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(0.88, doc.RootElement.GetProperty("answers").GetProperty("result").GetProperty("noul").GetDouble(), 2);
    }

    [Fact]
    public async Task Noul_StdinPiping_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(NoulSuccessResponse));

        var result = await harness.RunAsync(["noul", "-", "Is this piped?", "--api-key", "test-key"], stdinText: "Piped input content");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Result: True (88.0%)", result.StdOut);
    }

    [Fact]
    public async Task Noul_MissingApiKey_ExitCode1()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(NoulSuccessResponse));

        var prevKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", null);
            var result = await harness.RunAsync(["noul", "text", "question?"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("API key", result.StdErr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", prevKey);
        }
    }

    [Fact]
    public async Task Choice_SuccessfulExecution_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(ChoiceSuccessResponse));

        var result = await harness.RunAsync([
            "choice",
            "Error 500 when verifying token",
            "Classify error",
            "-c", "auth=Authentication problem",
            "-c", "db=Database connection failed",
            "--api-key", "test-key"
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Choice: auth (Confidence: 94.0%)", result.StdOut);
        Assert.Contains("• auth: 94.0%", result.StdOut);
        Assert.Contains("• db: 6.0%", result.StdOut);
    }

    [Fact]
    public async Task Choice_InvalidFormat_ExitCode2()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(ChoiceSuccessResponse));

        var result = await harness.RunAsync([
            "choice",
            "Error",
            "Classify",
            "-c", "invalid_choice_without_equals_or_colon",
            "--api-key", "test-key"
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Invalid choice", result.StdErr);
    }

    [Fact]
    public async Task Score_SuccessfulExecution_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(ScoreSuccessResponse));

        var result = await harness.RunAsync([
            "score",
            "Severe outage on payment gateway",
            "Rate severity",
            "-l", "low",
            "-l", "medium",
            "-l", "high",
            "--api-key", "test-key"
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Score: 2.75 (Confidence: 88.0%)", result.StdOut);
        Assert.Contains("• 3: high", result.StdOut);
    }

    [Fact]
    public async Task Score_LessThanTwoLevels_ExitCode2()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(ScoreSuccessResponse));

        var result = await harness.RunAsync([
            "score",
            "text",
            "instructions",
            "-l", "only-one-level",
            "--api-key", "test-key"
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("At least two rubric levels", result.StdErr);
    }

    [Fact]
    public async Task Evaluate_InlineQuestions_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(EvaluateSuccessResponse));

        var questionsJson = """
            {
              "is_spam": {
                "type": "noul",
                "instructions": "Is this message spam?"
              },
              "category": {
                "type": "choice",
                "instructions": "Classify message intent",
                "choices": {
                  "support": "Customer support request",
                  "sales": "Sales inquiry"
                }
              }
            }
            """;

        var result = await harness.RunAsync([
            "evaluate",
            "Hello, I need help resetting my password",
            "--questions", questionsJson,
            "--api-key", "test-key"
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[is_spam] (noul)", result.StdOut);
        Assert.Contains("[category] (choice)", result.StdOut);
    }

    [Fact]
    public async Task Evaluate_FileQuestions_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(EvaluateSuccessResponse));

        var tempFile = Path.GetTempFileName();
        try
        {
            var questionsJson = """
                {
                  "is_spam": {
                    "type": "noul",
                    "instructions": "Is this message spam?"
                  }
                }
                """;
            await File.WriteAllTextAsync(tempFile, questionsJson);

            var result = await harness.RunAsync([
                "evaluate",
                "Hello world",
                "--file", tempFile,
                "--api-key", "test-key"
            ]);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task Evaluate_MissingQuestions_ExitCode2()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(EvaluateSuccessResponse));

        var result = await harness.RunAsync([
            "evaluate",
            "Hello world",
            "--api-key", "test-key"
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Questions must be provided", result.StdErr);
    }

    [Fact]
    public async Task Models_SuccessfulExecution_ExitCode0()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(ModelsSuccessResponse));

        var result = await harness.RunAsync(["models", "--api-key", "test-key"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Available TypeSafe Models:", result.StdOut);
        Assert.Contains("jev-latest (Released: 2026-08-01)", result.StdOut);
    }

    [Fact]
    public async Task ApiError_ReturnsExitCode1()
    {
        var errorJson = """
            {
              "type": "invalid_request_error",
              "message": "Invalid state payload"
            }
            """;
        var response = MockHttpMessageHandler.CreateJsonResponse(errorJson, HttpStatusCode.BadRequest);
        response.Headers.Add("x-typesafe-request-id", "req-12345");
        using var harness = new CliTestHarness(response);

        var result = await harness.RunAsync(["noul", "text", "question?", "--api-key", "test-key"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("TypeSafe API error: 400 Invalid state payload (request id: req-12345)", result.StdErr);
    }

    private const string BoxSuccessResponse = """
        {
          "model": "jev-preview",
          "answers": {
            "result": {
              "type": "box",
              "box": { "top": 0.1, "bottom": 0.75, "left": 0.2, "right": 0.6 },
              "confidence": null
            }
          },
          "usage": {
            "input_tokens": null,
            "output_tokens": 3
          }
        }
        """;

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4];

    private static (CliTestHarness Harness, List<(HttpRequestMessage Request, string Body)> Captured) CreateCapturingHarness(string responseJson)
    {
        var captured = new List<(HttpRequestMessage, string)>();
        var harness = new CliTestHarness(request =>
        {
            captured.Add((request, request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            return MockHttpMessageHandler.CreateJsonResponse(responseJson);
        });
        return (harness, captured);
    }

    private static async Task<T> WithDefaultModelUnsetAsync<T>(Func<Task<T>> action)
    {
        var previous = Environment.GetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", null);
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", previous);
        }
    }

    [Fact]
    public async Task Box_ImageFileState_SendsMultipartImageRequestWithPreviewModel()
    {
        var (harness, captured) = CreateCapturingHarness(BoxSuccessResponse);
        using var harnessScope = harness;
        var imagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes);
        try
        {
            var result = await WithDefaultModelUnsetAsync(() =>
                harness.RunAsync(["box", $"@{imagePath}", "Locate the pig", "--api-key", "test-key"]));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Tokens: n/a in / 3 out", result.StdOut);
            Assert.Contains("Box: top=0.1, bottom=0.75, left=0.2, right=0.6", result.StdOut);
            Assert.Contains("Confidence: unknown", result.StdOut);

            var (request, body) = Assert.Single(captured);
            Assert.Equal("true", Assert.Single(request.Headers.GetValues("TypeSafe-Danger-Zone-Images")));
            Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
            Assert.Contains("\"model\":\"jev-preview\"", body);
            Assert.Contains("\"type\":\"box\"", body);
            Assert.Contains("\"assets\":{", body);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task Box_ExplicitModel_IsNotOverridden()
    {
        var (harness, captured) = CreateCapturingHarness(BoxSuccessResponse);
        using var harnessScope = harness;
        var imagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes);
        try
        {
            var result = await harness.RunAsync(["box", $"@{imagePath}", "Locate the pig", "-m", "custom-model", "--api-key", "test-key"]);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"model\":\"custom-model\"", Assert.Single(captured).Body);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task Box_JsonMode_OutputsBoxAndNullableFields()
    {
        var (harness, _) = CreateCapturingHarness(BoxSuccessResponse);
        using var harnessScope = harness;
        var imagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes);
        try
        {
            var result = await harness.RunAsync(["box", $"@{imagePath}", "Locate the pig", "--json", "--api-key", "test-key"]);

            Assert.Equal(0, result.ExitCode);
            using var doc = JsonDocument.Parse(result.StdOut);
            var answer = doc.RootElement.GetProperty("answers").GetProperty("result");
            Assert.Equal("box", answer.GetProperty("type").GetString());
            Assert.Equal(0.75, answer.GetProperty("box").GetProperty("bottom").GetDouble(), 2);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("usage").GetProperty("input_tokens").ValueKind);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task Noul_ImageOption_AttachesNamedAsset()
    {
        var (harness, captured) = CreateCapturingHarness(NoulSuccessResponse);
        using var harnessScope = harness;
        var imagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes);
        try
        {
            var result = await harness.RunAsync([
                "noul", "Photo: <photo>", "Does the photo show an animal?",
                "--image", $"photo={imagePath}",
                "--api-key", "test-key"
            ]);

            Assert.Equal(0, result.ExitCode);
            var (request, body) = Assert.Single(captured);
            Assert.True(request.Headers.Contains("TypeSafe-Danger-Zone-Images"));
            Assert.Contains("\"assets\":{\"\\u003Cphoto\\u003E\":{\"type\":\"image\"", body);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task Noul_TextOnly_DoesNotSendImagesHeader()
    {
        var (harness, captured) = CreateCapturingHarness(NoulSuccessResponse);
        using var harnessScope = harness;

        var result = await harness.RunAsync(["noul", "plain text", "Is this text?", "--api-key", "test-key"]);

        Assert.Equal(0, result.ExitCode);
        var (request, _) = Assert.Single(captured);
        Assert.False(request.Headers.Contains("TypeSafe-Danger-Zone-Images"));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Evaluate_BoxQuestion_UsesPreviewModel()
    {
        var (harness, captured) = CreateCapturingHarness(BoxSuccessResponse.Replace("\"result\"", "\"pig\""));
        using var harnessScope = harness;
        var imagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(imagePath, PngBytes);
        try
        {
            var result = await WithDefaultModelUnsetAsync(() => harness.RunAsync([
                "evaluate", $"@{imagePath}",
                "--questions", """{ "pig": { "type": "box", "instructions": "Locate the pig" } }""",
                "--api-key", "test-key"
            ]));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("[pig] (box)", result.StdOut);
            Assert.Contains("\"model\":\"jev-preview\"", Assert.Single(captured).Body);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task Image_MissingFile_ExitCode2()
    {
        using var harness = new CliTestHarness(MockHttpMessageHandler.CreateJsonResponse(NoulSuccessResponse));

        var result = await harness.RunAsync([
            "noul", "<photo>", "Is there a dog?",
            "--image", "photo=does-not-exist.png",
            "--api-key", "test-key"
        ]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Image file not found", result.StdErr);
    }
}
