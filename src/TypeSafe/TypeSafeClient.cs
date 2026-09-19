using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafe;

public sealed class TypeSafeClient : IDisposable
{
    private const string SdkValue = "typesafe-sdk/0.6.0";
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>());
    private static readonly HashSet<string> ProtectedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "authorization", "content-type", "user-agent", "x-typesafe-sdk",
        "x-typesafe-runtime", "x-typesafe-retry-count"
    };

    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.TypeInfoResolverChain.Add(TypeSafeJsonSerializerContext.Default);
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            options.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        }
        return options;
    }

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ResolvedOptions _options;
    private readonly Random _random = new();

    public TypeSafeClient(TypeSafeClientOptions? options = null, HttpClient? httpClient = null)
    {
        _options = (options ?? new TypeSafeClientOptions()).Resolve();
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        if (_ownsHttpClient)
            _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }

    public Uri BaseUrl => _options.BaseUrl;
    public Uri BaseUri => _options.BaseUrl;
    public string DefaultModel => _options.DefaultModel;
    public TypeSafeLogLevel LogLevel => _options.LogLevel;
    public TimeSpan Timeout => _options.Timeout;
    public RetryPolicy Retry => _options.Retry;
    public IReadOnlyDictionary<string, string> DefaultHeaders => _options.DefaultHeaders ?? EmptyHeaders;

    public async Task<SystemOneResult> SystemOneAsync(
        SystemOneRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await SystemOneWithResponseAsync(request, requestOptions, cancellationToken)
            .ConfigureAwait(false);
        return response.Data;
    }

    public Task<ApiResponse<SystemOneResult>> SystemOneWithResponseAsync(
        SystemOneRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var payload = request.Model is null ? request with { Model = _options.DefaultModel } : request;
        return SendAsync(
            "/v1/systemone",
            HttpMethod.Post,
            JsonSerializer.Serialize(payload, JsonOptions),
            ParseSystemOne,
            requestOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ModelCard>> ModelsAsync(
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await ModelsWithResponseAsync(requestOptions, cancellationToken).ConfigureAwait(false);
        return response.Data;
    }

    public Task<ApiResponse<IReadOnlyList<ModelCard>>> ModelsWithResponseAsync(
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        SendAsync("/v1/models", HttpMethod.Get, null, ParseModels, requestOptions, cancellationToken);

    private static void ValidateRequest(SystemOneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EntryType.Validate(request.State, nameof(request.State));
        if (request.Questions is null || request.Questions.Count == 0)
            throw new TypeSafeError("Questions cannot be empty.");

        foreach (var (name, question) in request.Questions)
        {
            if (string.IsNullOrEmpty(name))
                throw new TypeSafeError("Question names cannot be empty.");
            if (question is null)
                throw new TypeSafeError($"Question '{name}' cannot be null.");

            switch (question)
            {
                case NoulQuestion noul:
                    EntryType.Validate(noul.Instructions, $"{name}.instructions");
                    if (noul.Criteria is not null)
                    {
                        EntryType.Validate(noul.Criteria.True, $"{name}.criteria.true");
                        EntryType.Validate(noul.Criteria.False, $"{name}.criteria.false");
                    }
                    break;
                case ChoiceQuestion choice:
                    Questions.Choice(choice.Instructions, choice.Criteria);
                    break;
                case ScoreQuestion score:
                    Questions.Score(score.Instructions, score.Criteria);
                    break;
                default:
                    throw new TypeSafeError($"Question '{name}' has an unknown type.");
            }
        }
    }

    private async Task<ApiResponse<T>> SendAsync<T>(
        string path,
        HttpMethod method,
        string? content,
        Func<string, T> parser,
        RequestOptions? requestOptions,
        CancellationToken callerToken)
    {
        requestOptions ??= new RequestOptions();
        var retry = _options.Retry.Layer(requestOptions.Retry);
        var timeout = requestOptions.Timeout ?? _options.Timeout;
        if (timeout <= TimeSpan.Zero)
            throw new TypeSafeError("Timeout must be positive.");

        for (var attempt = 0; ; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutCts.Token);
            using var request = CreateRequest(path, method, content, requestOptions.Headers, attempt);
            LogRequest(request);
            HttpResponseMessage? response = null;

            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token).ConfigureAwait(false);
                Log(TypeSafeLogLevel.Information, $"{method} {path} returned {(int)response.StatusCode}.");

                if (retry.StatusCodes.Contains((int)response.StatusCode) && attempt < retry.MaxRetries)
                {
                    var delay = GetRetryDelay(response, attempt, retry);
                    Log(TypeSafeLogLevel.Information,
                        $"Retrying {method} {path} after status {(int)response.StatusCode} in {delay.TotalMilliseconds:0} ms.");
                    response.Dispose();
                    response = null;
                    await DelayForRetryAsync(delay, callerToken).ConfigureAwait(false);
                    continue;
                }

                var body = await BufferResponseBodyAsync(response, linkedCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var error = CreateApiError(response, body, retry);
                    response.Dispose();
                    response = null;
                    throw error;
                }

                try
                {
                    var result = parser(body);
                    var wrapper = new ApiResponse<T>(result, response);
                    response = null;
                    return wrapper;
                }
                catch
                {
                    response!.Dispose();
                    response = null;
                    throw;
                }
            }
            catch (OperationCanceledException exception) when (callerToken.IsCancellationRequested)
            {
                response?.Dispose();
                Log(TypeSafeLogLevel.Information, $"{method} {path} was aborted by the caller.");
                throw new ApiUserAbortError("Request was cancelled by the caller.", exception);
            }
            catch (OperationCanceledException exception)
            {
                response?.Dispose();
                if (retry.RetryTimeouts && attempt < retry.MaxRetries)
                {
                    var delay = GetBackoff(attempt, retry);
                    Log(TypeSafeLogLevel.Information,
                        $"Retrying {method} {path} after a timeout in {delay.TotalMilliseconds:0} ms.");
                    await DelayForRetryAsync(delay, callerToken).ConfigureAwait(false);
                    continue;
                }

                Log(TypeSafeLogLevel.Information, $"{method} {path} timed out.");
                throw new ApiTimeoutError($"Request timed out after {timeout}.", exception, timeout);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException &&
                retry.RetryConnectionErrors &&
                attempt < retry.MaxRetries)
            {
                response?.Dispose();
                var delay = GetBackoff(attempt, retry);
                Log(TypeSafeLogLevel.Information,
                    $"Retrying {method} {path} after a connection failure in {delay.TotalMilliseconds:0} ms.");
                await DelayForRetryAsync(delay, callerToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                response?.Dispose();
                Log(TypeSafeLogLevel.Information, $"{method} {path} failed to connect.");
                throw new ApiConnectionError("Connection to TypeSafe failed.", exception);
            }
        }
    }

    private async Task DelayForRetryAsync(TimeSpan delay, CancellationToken callerToken)
    {
        try
        {
            await Task.Delay(delay, callerToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (callerToken.IsCancellationRequested)
        {
            Log(TypeSafeLogLevel.Information, "Request was aborted by the caller while waiting to retry.");
            throw new ApiUserAbortError("Request was cancelled by the caller.", exception);
        }
    }

    private static async Task<string> BufferResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var originalContent = response.Content;
        var bytes = await originalContent.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var bufferedContent = new ByteArrayContent(bytes);
        foreach (var header in originalContent.Headers)
            bufferedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content = bufferedContent;
        originalContent.Dispose();
        return Encoding.UTF8.GetString(bytes);
    }

    private HttpRequestMessage CreateRequest(
        string path,
        HttpMethod method,
        string? content,
        IReadOnlyDictionary<string, string>? callHeaders,
        int attempt)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(_options.BaseUrl.AbsoluteUri.TrimEnd('/') + path));
        if (content is not null)
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

        AddUnprotected(request, _options.DefaultHeaders);
        AddUnprotected(request, callHeaders);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.UserAgent.ParseAdd(SdkValue);
        request.Headers.TryAddWithoutValidation("X-TypeSafe-SDK", SdkValue);
        request.Headers.TryAddWithoutValidation(
            "X-TypeSafe-Runtime",
            $".NET/{Environment.Version} ({RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture})");
        if (attempt > 0)
            request.Headers.TryAddWithoutValidation("X-TypeSafe-Retry-Count", attempt.ToString(CultureInfo.InvariantCulture));
        return request;
    }

    private static void AddUnprotected(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
            return;

        foreach (var (name, value) in headers)
        {
            if (ProtectedHeaders.Contains(name))
                continue;
            request.Headers.Remove(name);
            request.Content?.Headers.Remove(name);
            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private void LogRequest(HttpRequestMessage request)
    {
        var headers = request.Headers
            .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value));
        Log(
            TypeSafeLogLevel.Debug,
            $"{request.Method} {request.RequestUri} {CredentialRedactor.RedactHeaders(headers)}");
    }

    private void Log(TypeSafeLogLevel level, string message)
    {
        if (_options.LogLevel == TypeSafeLogLevel.None || level < _options.LogLevel)
            return;
        _options.Logger?.Log(level, message);
    }

    private TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt,
        RetryPolicy retry)
    {
        if (retry.RespectRetryAfter && TryGetServerRetryDelay(response, retry.MaxRetryAfter, out var delay))
            return delay;
        return GetBackoff(attempt, retry);
    }

    private static bool TryGetServerRetryDelay(
        HttpResponseMessage response,
        TimeSpan maximum,
        out TimeSpan delay)
    {
        if (response.Headers.TryGetValues("retry-after-ms", out var milliseconds))
        {
            var validMilliseconds = double.TryParse(
                milliseconds.FirstOrDefault(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var ms) &&
                double.IsFinite(ms) &&
                ms >= 0;
            if (validMilliseconds)
            {
                if (ms <= maximum.TotalMilliseconds)
                {
                    delay = TimeSpan.FromMilliseconds(ms);
                    return true;
                }

                delay = default;
                return false;
            }
        }

        if (response.Headers.RetryAfter is { } retryAfter)
        {
            TimeSpan? parsed = retryAfter.Delta;
            if (parsed is null && retryAfter.Date is { } date)
                parsed = date - DateTimeOffset.UtcNow;
            if (parsed is { } value && value >= TimeSpan.Zero && value <= maximum)
            {
                delay = value;
                return true;
            }
        }

        delay = default;
        return false;
    }

    private TimeSpan GetBackoff(int attempt, RetryPolicy retry)
    {
        var exponential = Math.Min(
            retry.MaxDelay.TotalMilliseconds,
            retry.InitialDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var factor = 1 - (_random.NextDouble() * retry.SubtractiveJitter);
        return TimeSpan.FromMilliseconds(exponential * factor);
    }

    private static ApiError CreateApiError(
        HttpResponseMessage response,
        string text,
        RetryPolicy retry)
    {
        object? body = null;
        JsonElement? json = null;
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                json = JsonSerializer.Deserialize<JsonElement>(text);
                body = json.Value.Clone();
            }
            catch (JsonException)
            {
                body = text;
            }
        }

        var headers = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        var requestId = response.Headers.TryGetValues("x-typesafe-request-id", out var values)
            ? values.FirstOrDefault()
            : null;
        var message = FormatErrorMessage(response.StatusCode, json, text);

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest =>
                new BadRequestError(message, response.StatusCode, headers, body, requestId),
            HttpStatusCode.Unauthorized =>
                new AuthenticationError(message, response.StatusCode, headers, body, requestId),
            HttpStatusCode.Forbidden =>
                new PermissionDeniedError(message, response.StatusCode, headers, body, requestId),
            HttpStatusCode.NotFound =>
                new NotFoundError(message, response.StatusCode, headers, body, requestId),
            (HttpStatusCode)422 =>
                new UnprocessableEntityError(message, response.StatusCode, headers, body, requestId),
            HttpStatusCode.TooManyRequests =>
                new RateLimitError(
                    message,
                    response.StatusCode,
                    headers,
                    body,
                    requestId,
                    TryGetServerRetryDelay(response, retry.MaxRetryAfter, out var delay) ? delay : null),
            >= HttpStatusCode.InternalServerError =>
                new InternalServerError(message, response.StatusCode, headers, body, requestId),
            _ => new ApiError(message, response.StatusCode, headers, body, requestId)
        };
    }

    private static string FormatErrorMessage(HttpStatusCode status, JsonElement? body, string text)
    {
        string? detail = null;
        if (body is { } root)
        {
            detail = root.ValueKind switch
            {
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Object => ExtractObjectMessage(root),
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(text))
            return $"{(int)status} status code (no body)";

        if (!string.IsNullOrWhiteSpace(detail))
            return $"{(int)status} {detail}";

        var truncated = text.Length > 200 ? text[..200] + "…" : text;
        return $"{(int)status} {truncated}";
    }

    private static string? ExtractObjectMessage(JsonElement root)
    {
        if (TryString(root, "error", out var error))
            return error;
        if (TryNestedMessage(root, "error", out error))
            return error;
        if (TryString(root, "message", out var message))
            return message;
        if (TryString(root, "detail", out var detail))
            return detail;
        if (TryNestedMessage(root, "detail", out detail))
            return detail;
        if (root.TryGetProperty("detail", out var validation) && validation.ValueKind == JsonValueKind.Array)
            return FormatValidationDetails(validation);
        return null;
    }

    private static bool TryString(JsonElement root, string propertyName, out string? value)
    {
        if (root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryNestedMessage(JsonElement root, string propertyName, out string? value)
    {
        if (root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Object &&
            TryString(property, "message", out value))
            return true;

        value = null;
        return false;
    }

    private static string FormatValidationDetails(JsonElement detail) =>
        string.Join("; ", detail.EnumerateArray().Select(item =>
        {
            var path = item.TryGetProperty("loc", out var location) &&
                       location.ValueKind == JsonValueKind.Array
                ? string.Join(
                    ".",
                    location.EnumerateArray()
                        .Select(segment => segment.ToString())
                        .SkipWhile(segment => segment.Equals("body", StringComparison.OrdinalIgnoreCase)))
                : null;
            var message = item.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String
                ? msg.GetString()
                : item.ToString();
            return string.IsNullOrEmpty(path) ? message : $"{path}: {message}";
        }));

    private static IReadOnlyList<ModelCard> ParseModels(string json)
    {
        using var document = ParseJson(json, "models response");
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
            throw new TypeSafeError("Malformed models response: expected an object with a models array.");

        try
        {
            var result = models.Deserialize<List<ModelCard>>(JsonOptions)
                ?? throw new JsonException("models was null");
            if (result.Any(model => model.Name is null || model.Description is null || model.ReleaseDate is null))
                throw new JsonException("a model entry is missing required fields");
            return result;
        }
        catch (JsonException exception)
        {
            throw new TypeSafeError("Malformed models response.", exception);
        }
    }

    private static SystemOneResult ParseSystemOne(string json)
    {
        using var document = ParseJson(json, "System One response");
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("input_tokens", out var input) || !input.TryGetInt32(out var inputTokens) ||
            !usage.TryGetProperty("output_tokens", out var output) || !output.TryGetInt32(out var outputTokens))
            throw new TypeSafeError("Malformed System One response.");

        var parsedAnswers = new Dictionary<string, Answer>();
        foreach (var property in answers.EnumerateObject())
        {
            if (!property.Value.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
                throw new TypeSafeError($"Malformed answer '{property.Name}': missing type.");

            try
            {
                Answer answer = typeElement.GetString() switch
                {
                    "noul" => property.Value.Deserialize<NoulAnswer>(JsonOptions)
                        ?? throw new JsonException("noul answer was null"),
                    "choice" => property.Value.Deserialize<ChoiceAnswer>(JsonOptions)
                        ?? throw new JsonException("choice answer was null"),
                    "score" => property.Value.Deserialize<ScoreAnswer>(JsonOptions)
                        ?? throw new JsonException("score answer was null"),
                    var type => throw new TypeSafeError(
                        $"Malformed answer '{property.Name}': unknown type '{type}'.")
                };
                if (answer is ChoiceAnswer choice &&
                    (choice.Choice is null || choice.Probabilities is null))
                    throw new JsonException("choice answer is missing required fields");
                if (answer is ScoreAnswer score &&
                    (score.Legend is null || score.Probabilities is null))
                    throw new JsonException("score answer is missing required fields");
                parsedAnswers[property.Name] = answer;
            }
            catch (JsonException exception)
            {
                throw new TypeSafeError($"Malformed answer '{property.Name}'.", exception);
            }
        }

        return new SystemOneResult
        {
            Model = model.GetString()!,
            Answers = parsedAnswers,
            Usage = new Usage { InputTokens = inputTokens, OutputTokens = outputTokens }
        };
    }

    private static JsonDocument ParseJson(string json, string description)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new TypeSafeError($"Malformed {description}: invalid JSON.", exception);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
