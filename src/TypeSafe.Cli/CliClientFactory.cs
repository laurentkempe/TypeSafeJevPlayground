namespace TypeSafe.Cli;

public static class CliClientFactory
{
    public static TypeSafeClient CreateClient(CliGlobalOptions options, HttpClient? httpClient = null)
    {
        var apiKey = !string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.ApiKey
            : Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");

        var baseUrl = options.BaseUrl;
        if (baseUrl is null && Environment.GetEnvironmentVariable("TYPESAFE_BASE_URL") is { } envBaseUrl && !string.IsNullOrWhiteSpace(envBaseUrl))
        {
            baseUrl = new Uri(envBaseUrl);
        }

        var defaultModel = !string.IsNullOrWhiteSpace(options.Model)
            ? options.Model
            : Environment.GetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL");

        var clientOptions = new TypeSafeClientOptions
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            BaseUrl = baseUrl,
            DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? null : defaultModel
        };

        return new TypeSafeClient(clientOptions, httpClient);
    }
}
