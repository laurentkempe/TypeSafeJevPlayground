using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace TypeSafe;

public enum TypeSafeLogLevel { Trace, Debug, Information, Warning, Error, None }

public sealed class RetryOptions
{
    public int? MaxRetries { get; set; }
    public TimeSpan? InitialDelay { get; set; }
    public TimeSpan? MaxDelay { get; set; }
    public double? SubtractiveJitter { get; set; }
    public IReadOnlySet<int>? StatusCodes { get; set; }
    public bool? RespectRetryAfter { get; set; }
    public TimeSpan? MaxRetryAfter { get; set; }
    public bool? RetryConnectionErrors { get; set; }
    public bool? RetryTimeouts { get; set; }
}

public sealed class TypeSafeClientOptions
{
    public bool Compression { get; set; } = true;
    public string? ApiKey { get; set; }
    public Uri? BaseUrl { get; set; }
    public string? DefaultModel { get; set; }
    public TimeSpan? Timeout { get; set; }
    public TypeSafeLogLevel? LogLevel { get; set; }
    public RetryOptions? Retry { get; set; }
    public IReadOnlyDictionary<string, string>? DefaultHeaders { get; set; }
    public ITypeSafeLogger? Logger { get; set; }

    internal ResolvedOptions Resolve()
    {
        static string? Env(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var apiKey = ApiKey is null ? Env("TYPESAFE_API_KEY") : ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new TypeSafeError("An API key is required. Set ApiKey or TYPESAFE_API_KEY.");
        apiKey = apiKey.Trim();

        var baseText = Env("TYPESAFE_BASE_URL");
        var configuredBaseUrl = BaseUrl ?? (baseText is null
            ? new Uri("https://api.typesafe.ai")
            : CreateUri(baseText, "TYPESAFE_BASE_URL"));
        var baseUrl = CreateUri(configuredBaseUrl.AbsoluteUri.TrimEnd('/'), nameof(BaseUrl));
        var model = DefaultModel ?? (Env("TYPESAFE_DEFAULT_MODEL") ?? "jev-latest");
        if (string.IsNullOrWhiteSpace(model))
            throw new TypeSafeError("DefaultModel cannot be blank.");
        model = model.Trim();
        var log = ResolveLogLevel(LogLevel, Env("TYPESAFE_LOG_LEVEL"));

        if (Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new TypeSafeError("Timeout must be positive.");

        var headers = DefaultHeaders is null
            ? null
            : new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(DefaultHeaders, StringComparer.OrdinalIgnoreCase));

        return new ResolvedOptions(
            apiKey,
            baseUrl,
            model,
            Timeout ?? TimeSpan.FromSeconds(10),
            log,
            RetryPolicy.Default.Layer(Retry),
            headers,
            Compression,
            Logger ?? new ConsoleTypeSafeLogger());
    }

    private static Uri CreateUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new TypeSafeError($"{name} must be an absolute URI.");
        return uri;
    }

    private static TypeSafeLogLevel ResolveLogLevel(TypeSafeLogLevel? explicitLevel, string? environmentLevel)
    {
        if (explicitLevel is { } level)
        {
            if (!Enum.IsDefined(level))
                throw new TypeSafeError($"Invalid log level '{level}'.");
            return level;
        }

        if (environmentLevel is null)
            return TypeSafeLogLevel.Warning;

        return environmentLevel.ToLowerInvariant() switch
        {
            "debug" => TypeSafeLogLevel.Debug,
            "info" => TypeSafeLogLevel.Information,
            "warn" => TypeSafeLogLevel.Warning,
            "error" => TypeSafeLogLevel.Error,
            "off" => TypeSafeLogLevel.None,
            _ => throw new TypeSafeError(
                $"Invalid log level '{environmentLevel}' in TYPESAFE_LOG_LEVEL.")
        };
    }
}

internal sealed record ResolvedOptions(
    string ApiKey,
    Uri BaseUrl,
    string DefaultModel,
    TimeSpan Timeout,
    TypeSafeLogLevel LogLevel,
    RetryPolicy Retry,
    IReadOnlyDictionary<string, string>? DefaultHeaders,
    bool Compression,
    ITypeSafeLogger? Logger);

public sealed class RetryPolicy
{
    private static readonly IReadOnlySet<int> DefaultStatuses =
        new HashSet<int>([408, 429, .. Enumerable.Range(500, 100)]).ToFrozenSet();

    internal static RetryPolicy Default { get; } = new(
        2, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5), 0.25,
        DefaultStatuses, true, TimeSpan.FromSeconds(60), true, true);

    internal RetryPolicy(
        int maxRetries,
        TimeSpan initialDelay,
        TimeSpan maxDelay,
        double subtractiveJitter,
        IEnumerable<int> statusCodes,
        bool respectRetryAfter,
        TimeSpan maxRetryAfter,
        bool retryConnectionErrors,
        bool retryTimeouts)
    {
        MaxRetries = maxRetries;
        InitialDelay = initialDelay;
        MaxDelay = maxDelay;
        SubtractiveJitter = subtractiveJitter;
        StatusCodes = statusCodes.ToFrozenSet();
        RespectRetryAfter = respectRetryAfter;
        MaxRetryAfter = maxRetryAfter;
        RetryConnectionErrors = retryConnectionErrors;
        RetryTimeouts = retryTimeouts;
        Validate();
    }

    public int MaxRetries { get; }
    public TimeSpan InitialDelay { get; }
    public TimeSpan MaxDelay { get; }
    public double SubtractiveJitter { get; }
    public IReadOnlySet<int> StatusCodes { get; }
    public bool RespectRetryAfter { get; }
    public TimeSpan MaxRetryAfter { get; }
    public bool RetryConnectionErrors { get; }
    public bool RetryTimeouts { get; }

    internal RetryPolicy Layer(RetryOptions? options) =>
        options is null
            ? this
            : new RetryPolicy(
                options.MaxRetries ?? MaxRetries,
                options.InitialDelay ?? InitialDelay,
                options.MaxDelay ?? MaxDelay,
                options.SubtractiveJitter ?? SubtractiveJitter,
                options.StatusCodes ?? StatusCodes,
                options.RespectRetryAfter ?? RespectRetryAfter,
                options.MaxRetryAfter ?? MaxRetryAfter,
                options.RetryConnectionErrors ?? RetryConnectionErrors,
                options.RetryTimeouts ?? RetryTimeouts);

    private void Validate()
    {
        if (MaxRetries < 0)
            throw new TypeSafeError("MaxRetries cannot be negative.");
        if (InitialDelay < TimeSpan.Zero)
            throw new TypeSafeError("InitialDelay cannot be negative.");
        if (MaxDelay < TimeSpan.Zero)
            throw new TypeSafeError("MaxDelay cannot be negative.");
        if (MaxRetryAfter < TimeSpan.Zero)
            throw new TypeSafeError("MaxRetryAfter cannot be negative.");
        if (SubtractiveJitter is < 0 or > 1)
            throw new TypeSafeError("SubtractiveJitter must be between 0 and 1.");
        if (StatusCodes.Any(status => status is < 100 or > 999))
            throw new TypeSafeError("Retry status codes must be between 100 and 999.");
    }
}

public sealed class RequestOptions
{
    public TimeSpan? Timeout { get; set; }
    public RetryOptions? Retry { get; set; }
    public IReadOnlyDictionary<string, string>? Headers { get; set; }
}
