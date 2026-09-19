using System.Net;

namespace TypeSafe;

public class TypeSafeError : Exception
{
    public TypeSafeError(string message, Exception? innerException = null) : base(message, innerException) { }
}

public class ApiError : TypeSafeError
{
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyDictionary<string, string[]> Headers { get; }
    public object? Body { get; }
    public string? RequestId { get; }

    public ApiError(
        string message,
        HttpStatusCode statusCode,
        IReadOnlyDictionary<string, string[]> headers,
        object? body,
        string? requestId) : base(message)
    {
        StatusCode = statusCode;
        Headers = headers;
        Body = body;
        RequestId = requestId;
    }
}

public sealed class BadRequestError(string m, HttpStatusCode s, IReadOnlyDictionary<string, string[]> h, object? b, string? r) : ApiError(m, s, h, b, r);
public sealed class AuthenticationError(string m, HttpStatusCode s, IReadOnlyDictionary<string, string[]> h, object? b, string? r) : ApiError(m, s, h, b, r);
public sealed class PermissionDeniedError(string m, HttpStatusCode s, IReadOnlyDictionary<string, string[]> h, object? b, string? r) : ApiError(m, s, h, b, r);
public sealed class NotFoundError(string m, HttpStatusCode s, IReadOnlyDictionary<string, string[]> h, object? b, string? r) : ApiError(m, s, h, b, r);
public sealed class UnprocessableEntityError(string m, HttpStatusCode s, IReadOnlyDictionary<string, string[]> h, object? b, string? r) : ApiError(m, s, h, b, r);

public sealed class RateLimitError(
    string m,
    HttpStatusCode s,
    IReadOnlyDictionary<string, string[]> h,
    object? b,
    string? r,
    TimeSpan? retryDelay) : ApiError(m, s, h, b, r)
{
    public TimeSpan? RetryDelay { get; } = retryDelay;
}

public sealed class InternalServerError(string m, HttpStatusCode s, IReadOnlyDictionary<string, string[]> h, object? b, string? r) : ApiError(m, s, h, b, r);

public class ApiConnectionError(string message, Exception inner) : TypeSafeError(message, inner);

public sealed class ApiTimeoutError(string message, Exception inner, TimeSpan timeout)
    : ApiConnectionError(message, inner)
{
    public TimeSpan Timeout { get; } = timeout;
}

public sealed class ApiUserAbortError(string message, Exception inner) : TypeSafeError(message, inner);
