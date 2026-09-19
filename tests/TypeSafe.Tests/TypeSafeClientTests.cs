using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TypeSafe;
using Xunit;

namespace TypeSafe.Tests;

public sealed class TypeSafeClientTests
{
    private const string ResultJson = """
        {"model":"jev-latest","answers":{"ok":{"type":"noul","noul":0.75},"pick":{"type":"choice","choice":"a","confidence":0.9,"probabilities":{"a":0.9,"b":0.1}},"rating":{"type":"score","score":2,"confidence":0.8,"legend":{"1":"bad","2":{"label":"good"}},"probabilities":{"1":0.2,"2":0.8}}},"usage":{"input_tokens":4,"output_tokens":2}}
        """;

    [Fact]
    public async Task SystemOneSerializesUpstreamRequestAndParsesTypedAnswers()
    {
        string? body = null;
        var handler = new StubHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/systemone", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer secret-value", request.Headers.Authorization!.ToString());
            Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
            Assert.Equal("typesafe-sdk/0.6.0", request.Headers.GetValues("X-TypeSafe-SDK").Single());
            body = await request.Content!.ReadAsStringAsync();
            return Response(HttpStatusCode.OK, ResultJson);
        });
        using var client = Client(handler);

        var result = await client.SystemOneAsync(new SystemOneRequest
        {
            State = new { text = "hello", metadata = new[] { 1, 2 } },
            Questions = new Dictionary<string, Question>
            {
                ["truth"] = Questions.Noul(
                    new { prompt = "Be strict" },
                    new NoulCriteria { True = "yes", False = null }),
                ["pick"] = Questions.Choice(
                    new object?[] { "Choose", new { language = "en" } },
                    new Dictionary<string, object?>
                    {
                        ["a"] = new { label = "A" },
                        ["b"] = null
                    }),
                ["rating"] = Questions.Score(
                    "Rate quality",
                    ["bad", new { label = "good" }])
            },
            AdditionalProperties = new Dictionary<string, object?>
            {
                ["metadata"] = new { trace = true }
            }
        });

        Assert.Equal(0.75, Assert.IsType<NoulAnswer>(result.Answers["ok"]).Noul);
        Assert.Equal(0.9, Assert.IsType<ChoiceAnswer>(result.Answers["pick"]).Probabilities["a"]);
        var score = Assert.IsType<ScoreAnswer>(result.Answers["rating"]);
        Assert.Equal(0.8, score.Probabilities["2"]);
        Assert.IsType<JsonElement>(score.Legend["2"]);

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("hello", root.GetProperty("state").GetProperty("text").GetString());
        Assert.Equal("jev-latest", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("metadata").GetProperty("trace").GetBoolean());
        Assert.Equal("noul", root.GetProperty("questions").GetProperty("truth").GetProperty("type").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("questions").GetProperty("truth").GetProperty("criteria").GetProperty("false").ValueKind);
        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("questions").GetProperty("pick").GetProperty("criteria").GetProperty("a").ValueKind);
        Assert.Equal(
            JsonValueKind.Array,
            root.GetProperty("questions").GetProperty("rating").GetProperty("criteria").ValueKind);
    }

    [Fact]
    public void NoulDefaultsSerializeNullInstructionsAndOptionalCriteriaMembers()
    {
        var json = JsonSerializer.Serialize<Question>(Questions.Noul(criteria: new NoulCriteria()));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("instructions").ValueKind);
        var criteria = document.RootElement.GetProperty("criteria");
        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("true").ValueKind);
        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("false").ValueKind);
    }

    [Fact]
    public void SourceGeneratedContextSerializesAndDeserializesWithoutReflection()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolverChain = { TypeSafeJsonSerializerContext.Default }
        };
        var question = Questions.Noul(instructions: "test instructions", criteria: new NoulCriteria { True = "yes", False = "no" });
        var json = JsonSerializer.Serialize<Question>(question, options);
        Assert.Contains("\"type\":\"noul\"", json);
        Assert.Contains("\"instructions\":\"test instructions\"", json);

        var deserialized = JsonSerializer.Deserialize<Question>(json, options);
        var noul = Assert.IsType<NoulQuestion>(deserialized);
        Assert.Equal("test instructions", noul.Instructions?.ToString());
    }

    [Fact]
    public async Task ProtectedHeadersCannotBeOverridden()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("Bearer secret-value", request.Headers.Authorization!.ToString());
            Assert.Equal("typesafe-sdk/0.6.0", request.Headers.UserAgent.ToString());
            Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
            Assert.Equal("call", request.Headers.GetValues("X-Custom").Single());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            return Response(HttpStatusCode.OK, ResultJson);
        });
        var options = Options();
        options.DefaultHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = "bad",
            ["Accept"] = "text/plain",
            ["User-Agent"] = "bad",
            ["X-Custom"] = "default"
        };
        using var client = new TypeSafeClient(options, new HttpClient(handler));
        await client.SystemOneAsync(Request(), new RequestOptions
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "worse",
                ["Accept"] = "text/xml",
                ["Content-Type"] = "text/plain",
                ["X-Custom"] = "call"
            }
        });
    }

    [Fact]
    public async Task PerCallRetryPolicyLayersAllSettings()
    {
        var attempts = 0;
        var handler = new StubHandler((request, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                Assert.False(request.Headers.Contains("X-TypeSafe-Retry-Count"));
                return Response((HttpStatusCode)418, "{}");
            }

            Assert.Equal("1", request.Headers.GetValues("X-TypeSafe-Retry-Count").Single());
            return Response(HttpStatusCode.OK, """{"models":[]}""");
        });
        using var client = Client(handler);
        await client.ModelsAsync(new RequestOptions
        {
            Retry = new RetryOptions
            {
                MaxRetries = 1,
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                SubtractiveJitter = 0,
                StatusCodes = new HashSet<int> { 418 },
                RespectRetryAfter = false,
                MaxRetryAfter = TimeSpan.Zero,
                RetryConnectionErrors = false,
                RetryTimeouts = false
            }
        });
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryAfterMsTakesPrecedenceAndInvalidServerDelayFallsBack()
    {
        var attempts = 0;
        var stopwatch = Stopwatch.StartNew();
        var handler = new StubHandler((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                var retry = Response(HttpStatusCode.TooManyRequests, "{}");
                retry.Headers.TryAddWithoutValidation("retry-after-ms", "1");
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return retry;
            }
            return Response(HttpStatusCode.OK, """{"models":[]}""");
        });
        using var client = Client(handler);
        await client.ModelsAsync();
        Assert.Equal(2, attempts);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task InvalidRetryAfterMsFallsThroughToRetryAfter()
    {
        var attempts = 0;
        var stopwatch = Stopwatch.StartNew();
        using var client = Client(new StubHandler((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                var retry = Response(HttpStatusCode.TooManyRequests, "{}");
                retry.Headers.TryAddWithoutValidation("retry-after-ms", "invalid");
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return retry;
            }
            return Response(HttpStatusCode.OK, """{"models":[]}""");
        }));

        await client.ModelsAsync(new RequestOptions
        {
            Retry = new RetryOptions
            {
                InitialDelay = TimeSpan.FromSeconds(5),
                MaxDelay = TimeSpan.FromSeconds(5),
                SubtractiveJitter = 0
            }
        });

        Assert.Equal(2, attempts);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task OverCapRetryAfterMsDoesNotFallThroughToRetryAfter()
    {
        var attempts = 0;
        var stopwatch = Stopwatch.StartNew();
        using var client = Client(new StubHandler((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                var retry = Response(HttpStatusCode.TooManyRequests, "{}");
                retry.Headers.TryAddWithoutValidation("retry-after-ms", "61000");
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return retry;
            }
            return Response(HttpStatusCode.OK, """{"models":[]}""");
        }));

        await client.ModelsAsync(new RequestOptions
        {
            Retry = new RetryOptions
            {
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            }
        });

        Assert.Equal(2, attempts);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ParsesValidationAndRawApiErrors()
    {
        var responses = new Queue<HttpResponseMessage>([
            Response(
                (HttpStatusCode)422,
                """{"detail":[{"loc":["body","questions","x"],"msg":"bad"},{"loc":["body","model"],"msg":"missing"}]}"""),
            Response(HttpStatusCode.BadRequest, new string('x', 250), "text/plain")
        ]);
        responses.Peek().Headers.TryAddWithoutValidation("x-typesafe-request-id", "req_1");
        using var client = Client(new StubHandler((_, _) => responses.Dequeue()));
        var noRetry = new RequestOptions { Retry = new RetryOptions { MaxRetries = 0 } };

        var validation = await Assert.ThrowsAsync<UnprocessableEntityError>(
            () => client.ModelsAsync(noRetry));
        Assert.Equal("req_1", validation.RequestId);
        Assert.Contains("questions.x: bad; model: missing", validation.Message);
        Assert.DoesNotContain("body.questions", validation.Message);
        Assert.IsType<JsonElement>(validation.Body);

        var raw = await Assert.ThrowsAsync<BadRequestError>(() => client.ModelsAsync(noRetry));
        Assert.Equal(new string('x', 250), Assert.IsType<string>(raw.Body));
        Assert.DoesNotContain(new string('x', 201), raw.Message);
        Assert.Contains(new string('x', 200), raw.Message);
    }

    [Fact]
    public async Task RateLimitErrorExposesValidRetryDelay()
    {
        var response = Response(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""");
        response.Headers.TryAddWithoutValidation("retry-after-ms", "1250");
        using var client = Client(new StubHandler((_, _) => response));
        var error = await Assert.ThrowsAsync<RateLimitError>(() =>
            client.ModelsAsync(new RequestOptions { Retry = new RetryOptions { MaxRetries = 0 } }));
        Assert.Equal(TimeSpan.FromMilliseconds(1250), error.RetryDelay);
        Assert.Contains("slow down", error.Message);
    }

    [Fact]
    public async Task TimeoutCoversBodyAndCancellationParameterMapsToUserAbort()
    {
        using var timeoutClient = Client(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new DelayedContent("""{"models":[]}""")
            }));
        var timeout = await Assert.ThrowsAsync<ApiTimeoutError>(() =>
            timeoutClient.ModelsAsync(
                new RequestOptions
                {
                    Timeout = TimeSpan.FromMilliseconds(10),
                    Retry = new RetryOptions { MaxRetries = 0 }
                }));
        Assert.IsAssignableFrom<ApiConnectionError>(timeout);
        Assert.Equal(TimeSpan.FromMilliseconds(10), timeout.Timeout);

        using var cancellationClient = Client(new StubHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Response(HttpStatusCode.OK, """{"models":[]}""");
        }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        await Assert.ThrowsAsync<ApiUserAbortError>(() =>
            cancellationClient.ModelsAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task InjectedHttpClientTimeoutMapsToApiTimeoutAndRetries()
    {
        var attempts = 0;
        using var httpClient = new HttpClient(new StubHandler(async (_, token) =>
        {
            attempts++;
            if (attempts == 1)
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Response(HttpStatusCode.OK, """{"models":[]}""");
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(20)
        };
        using var client = new TypeSafeClient(Options(), httpClient);

        var models = await client.ModelsAsync(new RequestOptions
        {
            Retry = new RetryOptions { MaxRetries = 1, RetryTimeouts = true }
        });

        Assert.Empty(models);
        Assert.Equal(2, attempts);
        Assert.Equal(TimeSpan.FromMilliseconds(20), httpClient.Timeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsNonPositivePerCallTimeout(int milliseconds)
    {
        using var client = Client(new StubHandler((_, _) => Response(HttpStatusCode.OK, """{"models":[]}""")));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.ModelsAsync(new RequestOptions
        {
            Timeout = TimeSpan.FromMilliseconds(milliseconds)
        }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveClientTimeout(int milliseconds)
    {
        var options = Options();
        options.Timeout = TimeSpan.FromMilliseconds(milliseconds);
        Assert.Throws<TypeSafeError>(() => new TypeSafeClient(options));
    }

    [Fact]
    public async Task CancellationWhileWaitingToRetryMapsToUserAbort()
    {
        using var client = Client(new StubHandler((_, _) =>
            Response(HttpStatusCode.InternalServerError, "{}")));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAsync<ApiUserAbortError>(() =>
            client.ModelsAsync(
                new RequestOptions
                {
                    Retry = new RetryOptions
                    {
                        MaxRetries = 1,
                        InitialDelay = TimeSpan.FromSeconds(10),
                        MaxDelay = TimeSpan.FromSeconds(10)
                    }
                },
                cts.Token));
    }

    [Fact]
    public async Task RetriesResponseBodyIoFailure()
    {
        var attempts = 0;
        using var client = Client(new StubHandler((_, _) =>
        {
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingContent() }
                : Response(HttpStatusCode.OK, """{"models":[]}""");
        }));
        var models = await client.ModelsAsync();
        Assert.Empty(models);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ResponseWrapperRetainsReadableContentAndRequestId()
    {
        var response = Response(HttpStatusCode.OK, """{"models":[]}""");
        response.Headers.TryAddWithoutValidation("x-typesafe-request-id", "req_2");
        using var client = Client(new StubHandler((_, _) => response));
        using var result = await client.ModelsWithResponseAsync();
        Assert.Equal("req_2", result.RequestId);
        Assert.Equal("""{"models":[]}""", await result.Response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"models":{}}""")]
    [InlineData("""{"models":[{}]}""")]
    [InlineData("""{"models":[{"name":null,"description":"d","release_date":"r"}]}""")]
    [InlineData("""{"models":[{"name":"n","description":null,"release_date":"r"}]}""")]
    [InlineData("""{"models":[{"name":"n","description":"d","release_date":null}]}""")]
    public async Task RejectsMalformedModelsShape(string json)
    {
        using var client = Client(new StubHandler((_, _) => Response(HttpStatusCode.OK, json)));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.ModelsAsync());
    }

    [Fact]
    public async Task RejectsUnknownAnswerType()
    {
        using var client = Client(new StubHandler((_, _) => Response(
            HttpStatusCode.OK,
            """{"model":"m","answers":{"x":{"type":"other"}},"usage":{"input_tokens":1,"output_tokens":1}}""")));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(Request()));
    }

    [Theory]
    [InlineData("""{"model":"m","answers":{"x":{"type":"choice","choice":null,"confidence":0.5,"probabilities":{}}},"usage":{"input_tokens":1,"output_tokens":1}}""")]
    [InlineData("""{"model":"m","answers":{"x":{"type":"choice","choice":"a","confidence":0.5,"probabilities":null}},"usage":{"input_tokens":1,"output_tokens":1}}""")]
    [InlineData("""{"model":"m","answers":{"x":{"type":"score","score":1,"confidence":0.5,"legend":null,"probabilities":{}}},"usage":{"input_tokens":1,"output_tokens":1}}""")]
    [InlineData("""{"model":"m","answers":{"x":{"type":"score","score":1,"confidence":0.5,"legend":{},"probabilities":null}},"usage":{"input_tokens":1,"output_tokens":1}}""")]
    public async Task RejectsNullRequiredAnswerMembers(string json)
    {
        using var client = Client(new StubHandler((_, _) => Response(HttpStatusCode.OK, json)));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(Request()));
    }

    [Fact]
    public async Task ValidatesQuestionsWithSdkErrors()
    {
        Assert.Throws<TypeSafeError>(() => Questions.Score("instructions", ["only"]));
        Assert.Throws<TypeSafeError>(() => Questions.Choice("instructions", new Dictionary<string, object?>()));
        Assert.Throws<TypeSafeError>(() => Questions.Noul(42));
        using var client = Client(new StubHandler((_, _) => Response(HttpStatusCode.OK, ResultJson)));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Questions = new Dictionary<string, Question>()
        }));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0.0, 200)]
    [InlineData(0, -1, 0, 0.0, 200)]
    [InlineData(0, 0, -1, 0.0, 200)]
    [InlineData(0, 0, 0, -0.1, 200)]
    [InlineData(0, 0, 0, 1.1, 200)]
    [InlineData(0, 0, 0, 0.0, 99)]
    [InlineData(0, 0, 0, 0.0, 1000)]
    public void ValidatesRetryConfiguration(
        int retries,
        int initialMilliseconds,
        int maxMilliseconds,
        double jitter,
        int status)
    {
        Assert.Throws<TypeSafeError>(() => new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "key",
            Retry = new RetryOptions
            {
                MaxRetries = retries,
                InitialDelay = TimeSpan.FromMilliseconds(initialMilliseconds),
                MaxDelay = TimeSpan.FromMilliseconds(maxMilliseconds),
                SubtractiveJitter = jitter,
                StatusCodes = new HashSet<int> { status }
            }
        }));
    }

    [Fact]
    public void AcceptsRetryStatus600AndExposesImmutableResolvedSettings()
    {
        var headers = new Dictionary<string, string> { ["X-Test"] = "original" };
        var statuses = new HashSet<int> { 600 };
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "key",
            BaseUrl = new Uri("https://example.test/base"),
            DefaultModel = "model",
            LogLevel = TypeSafeLogLevel.Information,
            Timeout = TimeSpan.FromSeconds(3),
            DefaultHeaders = headers,
            Retry = new RetryOptions { StatusCodes = statuses, MaxRetries = 4 }
        });

        headers["X-Test"] = "changed";
        statuses.Add(601);

        Assert.Equal(client.BaseUrl, client.BaseUri);
        Assert.Equal("model", client.DefaultModel);
        Assert.Equal(TypeSafeLogLevel.Information, client.LogLevel);
        Assert.Equal(TimeSpan.FromSeconds(3), client.Timeout);
        Assert.Equal(4, client.Retry.MaxRetries);
        Assert.Contains(600, client.Retry.StatusCodes);
        Assert.DoesNotContain(601, client.Retry.StatusCodes);
        Assert.Equal("original", client.DefaultHeaders["X-Test"]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)client.DefaultHeaders)["X-Test"] = "mutated");
    }

    [Fact]
    public void RedactsCredentialsCookiesAndLoggerOutput()
    {
        var text = CredentialRedactor.RedactHeaders(new Dictionary<string, IEnumerable<string>>
        {
            ["Authorization"] = ["Bearer long-secret-value"],
            ["x-api-key"] = ["short"],
            ["Cookie"] = ["session=abc; theme=dark"],
            ["Set-Cookie"] = ["session=abc"],
            ["Accept"] = ["application/json"]
        });
        Assert.Contains("Bearer ***alue", text);
        Assert.Contains("x-api-key: ***", text);
        Assert.DoesNotContain("long-secret", text);
        Assert.DoesNotContain("session=abc", text);
        Assert.Contains("Cookie: ***", text);
        Assert.Contains("Set-Cookie: ***", text);
        Assert.Contains("application/json", text);
    }

    [Fact]
    public async Task LoggerNeverReceivesUnredactedApiKey()
    {
        var logger = new RecordingLogger();
        var options = Options();
        options.Logger = logger;
        options.LogLevel = TypeSafeLogLevel.Debug;
        using var client = new TypeSafeClient(
            options,
            new HttpClient(new StubHandler((_, _) => Response(HttpStatusCode.OK, """{"models":[]}"""))));
        await client.ModelsAsync();
        Assert.DoesNotContain("secret-value", logger.Message);
        Assert.Contains("Bearer ***alue", logger.Message);
    }

    [Fact]
    public async Task LoggerRespectsConfiguredLevel()
    {
        var logger = new RecordingLogger();
        var options = Options();
        options.Logger = logger;
        options.LogLevel = TypeSafeLogLevel.Information;
        using var client = new TypeSafeClient(
            options,
            new HttpClient(new StubHandler((_, _) => Response(HttpStatusCode.OK, """{"models":[]}"""))));

        await client.ModelsAsync();

        Assert.DoesNotContain(TypeSafeLogLevel.Debug, logger.Levels);
        Assert.Contains(TypeSafeLogLevel.Information, logger.Levels);
    }

    [Fact]
    public async Task ExplicitOptionsWinBlankEnvironmentIsIgnoredAndBaseUrlIsNormalized()
    {
        var oldKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        var oldUrl = Environment.GetEnvironmentVariable("TYPESAFE_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "environment-key");
            Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "   ");
            var handler = new StubHandler((request, _) =>
            {
                Assert.Equal("Bearer explicit-key", request.Headers.Authorization!.ToString());
                Assert.Equal("example.test", request.RequestUri!.Host);
                Assert.Equal("/base/v1/models", request.RequestUri.AbsolutePath);
                return Response(HttpStatusCode.OK, """{"models":[]}""");
            });
            using var client = new TypeSafeClient(
                new TypeSafeClientOptions
                {
                    ApiKey = "explicit-key",
                    BaseUrl = new Uri("https://example.test/base///")
                },
                new HttpClient(handler));
            await client.ModelsAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", oldUrl);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitBlankKeyOrModelDoesNotFallThroughToEnvironment(bool blankKey)
    {
        var oldKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        var oldModel = Environment.GetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "environment-key");
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", "environment-model");
            Assert.Throws<TypeSafeError>(() => new TypeSafeClient(new TypeSafeClientOptions
            {
                ApiKey = blankKey ? "   " : "explicit-key",
                DefaultModel = blankKey ? "explicit-model" : "   "
            }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", oldModel);
        }
    }

    [Fact]
    public async Task ApiErrorMessagesMatchUpstreamStyle()
    {
        var responses = new Queue<HttpResponseMessage>([
            Response(HttpStatusCode.BadRequest, ""),
            Response(HttpStatusCode.BadRequest, """{"message":"bad input"}"""),
            Response(HttpStatusCode.BadRequest, new string('x', 201), "text/plain")
        ]);
        using var client = Client(new StubHandler((_, _) => responses.Dequeue()));
        var noRetry = new RequestOptions { Retry = new RetryOptions { MaxRetries = 0 } };

        var empty = await Assert.ThrowsAsync<BadRequestError>(() => client.ModelsAsync(noRetry));
        Assert.Equal("400 status code (no body)", empty.Message);
        var detail = await Assert.ThrowsAsync<BadRequestError>(() => client.ModelsAsync(noRetry));
        Assert.Equal("400 bad input", detail.Message);
        var raw = await Assert.ThrowsAsync<BadRequestError>(() => client.ModelsAsync(noRetry));
        Assert.Equal($"400 {new string('x', 200)}…", raw.Message);
    }

    [Theory]
    [InlineData("debug", TypeSafeLogLevel.Debug)]
    [InlineData("info", TypeSafeLogLevel.Information)]
    [InlineData("warn", TypeSafeLogLevel.Warning)]
    [InlineData("error", TypeSafeLogLevel.Error)]
    [InlineData("off", TypeSafeLogLevel.None)]
    public void AcceptsUpstreamEnvironmentLogLevels(
        string environmentValue,
        TypeSafeLogLevel expected)
    {
        var old = Environment.GetEnvironmentVariable("TYPESAFE_LOG_LEVEL");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_LOG_LEVEL", environmentValue);
            using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "key" });
            Assert.Equal(expected, client.LogLevel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_LOG_LEVEL", old);
        }
    }

    [Fact]
    public void InvalidExplicitOrEnvironmentLogLevelThrowsTypeSafeError()
    {
        Assert.Throws<TypeSafeError>(() => new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "key",
            LogLevel = (TypeSafeLogLevel)999
        }));

        var old = Environment.GetEnvironmentVariable("TYPESAFE_LOG_LEVEL");
        try
        {
            Environment.SetEnvironmentVariable("TYPESAFE_LOG_LEVEL", "verbose-ish");
            Assert.Throws<TypeSafeError>(() => new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "key" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_LOG_LEVEL", old);
        }
    }

    [Fact]
    public async Task InjectedHttpClientIsNotDisposed()
    {
        var handler = new StubHandler((_, _) => Response(HttpStatusCode.OK, """{"models":[]}"""));
        var httpClient = new HttpClient(handler);
        var client = new TypeSafeClient(Options(), httpClient);
        client.Dispose();
        using var response = await httpClient.GetAsync("https://example.test/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static SystemOneRequest Request() => new()
    {
        State = "state",
        Questions = new Dictionary<string, Question>
        {
            ["x"] = Questions.Noul("Classify")
        }
    };

    private static TypeSafeClient Client(HttpMessageHandler handler) =>
        new(Options(), new HttpClient(handler));

    private static TypeSafeClientOptions Options() => new()
    {
        ApiKey = "secret-value",
        BaseUrl = new Uri("https://example.test"),
        Retry = new RetryOptions
        {
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(2),
            SubtractiveJitter = 0
        }
    };

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
            _handler = (request, token) => Task.FromResult(handler(request, token));

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class DelayedContent(string value) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(value), cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private sealed class FailingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("body failed"));

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private sealed class RecordingLogger : ITypeSafeLogger
    {
        public string Message { get; private set; } = "";
        public List<TypeSafeLogLevel> Levels { get; } = [];

        public void Log(TypeSafeLogLevel level, string message)
        {
            Levels.Add(level);
            Message += message;
        }
    }
}
