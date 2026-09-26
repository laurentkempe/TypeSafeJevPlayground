using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace TypeSafe.Mcp;

public sealed class TypeSafeClientProvider : IDisposable
{
    private readonly TypeSafeMcpOptions _options;
    private readonly HttpClient? _httpClient;
    private readonly Lock _lock = new();
    private TypeSafeClient? _client;

    public TypeSafeClientProvider(IOptions<TypeSafeMcpOptions> options)
        : this(options.Value, null)
    {
    }

    internal TypeSafeClientProvider(TypeSafeMcpOptions options, HttpClient? httpClient)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public TypeSafeClient GetClient()
    {
        if (_client is { } existing)
            return existing;

        lock (_lock)
        {
            if (_client is not null)
                return _client;

            try
            {
                _client = new TypeSafeClient(
                    new TypeSafeClientOptions
                    {
                        ApiKey = string.IsNullOrWhiteSpace(_options.ApiKey) ? null : _options.ApiKey,
                        BaseUrl = _options.BaseUrl,
                        DefaultModel = string.IsNullOrWhiteSpace(_options.Model) ? null : _options.Model
                    },
                    _httpClient);
            }
            catch (TypeSafeError exception) when (exception.Message.StartsWith("An API key is required", StringComparison.Ordinal))
            {
                throw new McpException("An API key is required. Set --api-key or TYPESAFE_API_KEY.", exception);
            }
            catch (TypeSafeError exception)
            {
                throw new McpException($"Invalid TypeSafe configuration: {exception.Message}", exception);
            }

            return _client;
        }
    }

    public void Dispose() => _client?.Dispose();
}
