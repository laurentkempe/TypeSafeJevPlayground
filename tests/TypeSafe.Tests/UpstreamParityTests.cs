using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using TypeSafe;
using Xunit;

namespace TypeSafe.Tests;

public sealed class UpstreamParityTests
{
    private const string Result = """{"model":"jev-preview","answers":{"target":{"type":"box","box":{"top":0.1,"bottom":0.8,"left":0.2,"right":0.9},"confidence":null}},"usage":{"input_tokens":null,"output_tokens":null}}""";
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 42];

    [Fact]
    public async Task ParsesBoxAndNullableUsageAndSerializesQuestionWithoutReflection()
    {
        using var client = Client((_, _) => Task.FromResult(Response()));
        var result = await client.SystemOneAsync(Request("state"));
        var answer = Assert.IsType<BoxAnswer>(result.Answers["target"]);
        Assert.Null(answer.Confidence);
        Assert.Equal(0.1, answer.Box.Top);
        Assert.Equal(0.8, answer.Box.Bottom);
        Assert.Equal(0.2, answer.Box.Left);
        Assert.Equal(0.9, answer.Box.Right);
        Assert.Null(result.Usage.InputTokens);
        Assert.Null(result.Usage.OutputTokens);

        var json = JsonSerializer.Serialize<Question>(Questions.Box(), TypeSafeJsonSerializerContext.Default.Question);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("box", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("instructions").ValueKind);
        var usage = JsonSerializer.Serialize(result.Usage, TypeSafeJsonSerializerContext.Default.Usage);
        Assert.Contains("\"input_tokens\":null", usage);
    }

    [Theory]
    [InlineData("""{"type":"box","box":null,"confidence":null}""")]
    [InlineData("""{"type":"box","box":{"top":0.1},"confidence":0.5}""")]
    [InlineData("""{"type":"box","box":{"top":0,"bottom":1,"left":0,"right":1}}""")]
    [InlineData("""{"type":"box","box":{"top":"bad","bottom":1,"left":0,"right":1},"confidence":null}""")]
    [InlineData("null")]
    public async Task RejectsMalformedBoxAnswers(string answer)
    {
        var result = """{"model":"m","answers":{"x":""" + answer + """},"usage":{"input_tokens":null,"output_tokens":null}}""";
        using var client = Client((_, _) => Task.FromResult(Response(result)));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(Request("state")));
    }

    [Theory]
    [InlineData("""{"input_tokens":"bad","output_tokens":null}""")]
    [InlineData("""{"input_tokens":null}""")]
    [InlineData("""{"input_tokens":{},"output_tokens":null}""")]
    public async Task RejectsMalformedUsageInsteadOfReplacingItWithZero(string usage)
    {
        using var client = Client((_, _) => Task.FromResult(Response(
            """{"model":"m","answers":{},"usage":""" + usage + "}")));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(Request("state")));
    }

    [Fact]
    public async Task ValidationReturnsFaultedTaskAndNeverSendsInvalidRequests()
    {
        using var client = Client((_, _) => throw new InvalidOperationException("Must not send."));
        foreach (var request in new[]
        {
            Request(null),
            Request(JsonDocument.Parse("null").RootElement.Clone()),
            Request("state") with { Questions = new Dictionary<string, Question> { ["x"] = Questions.Noul() } },
            Request("state") with { Questions = new Dictionary<string, Question> { ["x"] = Questions.Noul(criteria: new NoulCriteria()) } },
            Request("state") with { AdditionalProperties = new Dictionary<string, object?> { ["state"] = "override" } }
        })
        {
            Task? pending = null;
            Assert.Null(Record.Exception(() => { pending = client.SystemOneWithResponseAsync(request); }));
            await Assert.ThrowsAsync<TypeSafeError>(async () => await pending!);
        }
    }

    [Fact]
    public async Task AcceptsCriteriaOnlyNoulAndRuntimeScoreLists()
    {
        using var client = Client((_, _) => Task.FromResult(Response()));
        await client.SystemOneAsync(Request("state") with
        {
            Questions = new Dictionary<string, Question>
            {
                ["yes"] = Questions.Noul(criteria: new NoulCriteria { True = "Valid" }),
                ["rating"] = Questions.Score(null, Enumerable.Range(0, 3).Select(i => (object?)$"level {i}").ToArray())
            }
        });
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(1025, true)]
    public async Task CompressionUsesExactUtf8ByteThreshold(int size, bool compressed)
    {
        var overhead = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(Request("") with { Model = "jev-preview" },
            TypeSafeJsonSerializerContext.Default.SystemOneRequest));
        var state = new string('x', size - overhead);
        using var client = Client(async (message, _) =>
        {
            Assert.Equal(compressed, message.Content!.Headers.ContentEncoding.Contains("zstd"));
            Assert.DoesNotContain("evil", message.Content.Headers.ContentEncoding);
            var bytes = await ReadBody(message);
            Assert.Equal(size, bytes.Length);
            using var json = JsonDocument.Parse(bytes);
            Assert.Equal(state, json.RootElement.GetProperty("state").GetString());
            return Response();
        });
        await client.SystemOneAsync(Request(state), new RequestOptions
        {
            Headers = new Dictionary<string, string> { ["Content-Encoding"] = "evil", ["Content-Length"] = "1" }
        });
    }

    [Fact]
    public async Task CompressionCountsUtf8BytesNotCharacters()
    {
        using var client = Client(async (message, _) =>
        {
            Assert.Contains("zstd", message.Content!.Headers.ContentEncoding);
            using var json = JsonDocument.Parse(await ReadBody(message));
            Assert.Equal(new string('\u00e9', 600), json.RootElement.GetProperty("state").GetString());
            return Response();
        });
        await client.SystemOneAsync(Request(new string('\u00e9', 600)));
    }

    [Fact]
    public async Task CompressionCanBeDisabled()
    {
        using var client = Client(async (message, _) =>
        {
            Assert.Empty(message.Content!.Headers.ContentEncoding);
            Assert.True((await message.Content.ReadAsByteArrayAsync()).Length > 1024);
            return Response();
        }, compression: false);
        Assert.False(client.Compression);
        await client.SystemOneAsync(Request(new string('x', 2000)));
    }

    [Fact]
    public async Task RetriesReuseIdenticalCompressedMultipartBytes()
    {
        var attempts = 0;
        byte[]? first = null;
        string? contentType = null;
        using var client = Client(async (message, _) =>
        {
            Assert.Contains("zstd", message.Content!.Headers.ContentEncoding);
            var bytes = await message.Content.ReadAsByteArrayAsync();
            if (attempts++ == 0)
            {
                first = bytes;
                contentType = message.Content.Headers.ContentType!.ToString();
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }
            Assert.Equal(first, bytes);
            Assert.Equal(contentType, message.Content.Headers.ContentType!.ToString());
            Assert.Equal("1", message.Headers.GetValues("X-TypeSafe-Retry-Count").Single());
            return Response();
        });
        await client.SystemOneAsync(Request(new Image(new byte[2000])));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ImagesInNestedContentAndTextAreSentAsReferencedMultipartParts()
    {
        var bytes = Png.ToArray();
        var photo = new Image(bytes);
        bytes[0] = 0; // The SDK owns a copy.
        var jpeg = Image.FromBase64(Convert.ToBase64String([0xff, 0xd8, 0xff, 42]));
        using var client = Client(async (message, _) =>
        {
            Assert.Equal("true", message.Headers.GetValues("TypeSafe-Danger-Zone-Images").Single());
            Assert.Equal("multipart/form-data", message.Content!.Headers.ContentType!.MediaType);
            var parts = await MultipartParts(message);
            Assert.Equal(3, parts.Length);
            using var payload = JsonDocument.Parse(parts[0].Body);
            var root = payload.RootElement;
            Assert.Equal(photo.ToString(), root.GetProperty("state").GetProperty("photo").GetString());
            var instructions = root.GetProperty("questions").GetProperty("target").GetProperty("instructions").GetString();
            Assert.Contains(photo.ToString(), instructions);
            Assert.Contains(jpeg.ToString(), instructions);
            var assets = root.GetProperty("assets");
            Assert.Equal(2, assets.EnumerateObject().Count());
            Assert.Equal("asset-0", assets.GetProperty(photo.ToString()).GetProperty("part").GetString());
            Assert.Equal("image", assets.GetProperty(jpeg.ToString()).GetProperty("type").GetString());
            Assert.Contains("image/png", parts[1].Headers);
            Assert.Contains("image/jpeg", parts[2].Headers);
            Assert.Equal(Png, Encoding.Latin1.GetBytes(parts[1].Body));
            return Response();
        }, compression: false);
        var text = TextWithAssets.Create($"Compare {photo} with {TextWithAssets.Join([jpeg, photo], " and ")}");
        await client.SystemOneAsync(Request(new Dictionary<string, object?> { ["photo"] = photo }) with
        {
            Questions = new Dictionary<string, Question> { ["target"] = Questions.Box(text) }
        }, new RequestOptions { Headers = new Dictionary<string, string> { ["TypeSafe-Danger-Zone-Images"] = "true" } });
    }

    [Fact]
    public async Task NamedAssetsReplaceDirectImagesAndAssetText()
    {
        var image = new Image(Png);
        using var client = Client(async (message, _) =>
        {
            var parts = await MultipartParts(message);
            using var payload = JsonDocument.Parse(parts[0].Body);
            var root = payload.RootElement;
            Assert.Equal("<photo>", root.GetProperty("state").GetString());
            Assert.Equal("Locate in <photo>", root.GetProperty("questions").GetProperty("target").GetProperty("instructions").GetString());
            Assert.True(root.GetProperty("assets").TryGetProperty("<photo>", out var asset));
            Assert.Equal("image", asset.GetProperty("type").GetString());
            return Response();
        }, compression: false);
        await client.SystemOneAsync(Request(image) with
        {
            Assets = new Dictionary<string, Image> { ["<photo>"] = image },
            Questions = new Dictionary<string, Question> { ["target"] = Questions.Box(TextWithAssets.Create($"Locate in {image}")) }
        });
    }

    [Fact]
    public async Task RejectsDanglingReferencesAndUnreferencedNamedAssets()
    {
        var image = new Image(Png);
        using var client = Client((_, _) => throw new InvalidOperationException("Must not send."));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(Request($"Look at {image}")));
        await Assert.ThrowsAsync<TypeSafeError>(() => client.SystemOneAsync(Request("unrelated") with
        {
            Assets = new Dictionary<string, Image> { ["<photo>"] = image }
        }));
    }

    [Fact]
    public void RejectsEmptyImagesAndInvalidBase64AndSerializesReferencesWithGeneratedMetadata()
    {
        Assert.Throws<TypeSafeError>(() => new Image([]));
        Assert.Throws<TypeSafeError>(() => Image.FromBase64(""));
        Assert.Throws<TypeSafeError>(() => Image.FromBase64("data:image/png;base64,AAAA"));
        var image = new Image(Png);
        var json = JsonSerializer.Serialize(image, TypeSafeJsonSerializerContext.Default.Image);
        Assert.Equal(image.ToString(), JsonSerializer.Deserialize(json, TypeSafeJsonSerializerContext.Default.String));
    }

    [Fact]
    public async Task NonfiniteNestedNumbersAreSentAsStrings()
    {
        using var client = Client(async (message, _) =>
        {
            using var json = JsonDocument.Parse(await ReadBody(message));
            Assert.Equal("NaN", json.RootElement.GetProperty("state").GetProperty("number").GetString());
            return Response();
        });
        await client.SystemOneAsync(Request(new Dictionary<string, object?> { ["number"] = double.NaN }));
    }

    [Fact]
    public async Task AssetsInAllQuestionContentUseSourceGeneratedMetadata()
    {
        var image = new Image(Png);
        using var client = Client(async (message, _) =>
        {
            var parts = await MultipartParts(message);
            Assert.Equal(2, parts.Length);
            using var document = JsonDocument.Parse(parts[0].Body);
            var questions = document.RootElement.GetProperty("questions");
            Assert.Equal(image.ToString(), questions.GetProperty("noul").GetProperty("criteria").GetProperty("true").GetString());
            Assert.Equal(image.ToString(), questions.GetProperty("choice").GetProperty("criteria").GetProperty("yes").GetString());
            Assert.Equal(image.ToString(), questions.GetProperty("score").GetProperty("criteria")[1].GetString());
            return Response();
        }, compression: false);
        await client.SystemOneAsync(Request("photo") with
        {
            Questions = new Dictionary<string, Question>
            {
                ["noul"] = Questions.Noul(criteria: new NoulCriteria { True = image }),
                ["choice"] = Questions.Choice(image, new Dictionary<string, object?> { ["yes"] = image }),
                ["score"] = Questions.Score(null, ["none", image])
            }
        });
    }

    [Fact]
    public async Task CallerCancellationBeforeEncodingNeverSendsARequest()
    {
        using var client = Client((_, _) => throw new InvalidOperationException("Must not send."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<ApiUserAbortError>(() => client.SystemOneAsync(
            Request(new Image(new byte[2000])), cancellationToken: cancellation.Token));
    }

    private static SystemOneRequest Request(object? state) => new()
    {
        State = state,
        Questions = new Dictionary<string, Question> { ["target"] = Questions.Box("The object") }
    };

    private static TypeSafeClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        bool compression = true) =>
        new(new TypeSafeClientOptions
        {
            ApiKey = "test",
            DefaultModel = "jev-preview",
            Compression = compression,
            Retry = new RetryOptions { InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero }
        }, new HttpClient(new Handler(send)));

    private static HttpResponseMessage Response(string json = Result) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static async Task<byte[]> ReadBody(HttpRequestMessage request)
    {
        var bytes = await request.Content!.ReadAsByteArrayAsync();
        if (!request.Content.Headers.ContentEncoding.Contains("zstd"))
            return bytes;
        using var input = new MemoryStream(bytes);
        using var zstd = new ZstandardStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await zstd.CopyToAsync(output);
        return output.ToArray();
    }

    private static async Task<(string Headers, string Body)[]> MultipartParts(HttpRequestMessage request)
    {
        var boundary = request.Content!.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        var body = Encoding.Latin1.GetString(await ReadBody(request));
        return body.Split($"--{boundary}", StringSplitOptions.None)
            .Skip(1).SkipLast(1)
            .Select(part =>
            {
                var split = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                return (part[..split], part[(split + 4)..^2]);
            }).ToArray();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
