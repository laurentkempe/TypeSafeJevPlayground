namespace TypeSafe;

public sealed class ApiResponse<T> : IDisposable
{
    public T Data { get; }
    public HttpResponseMessage Response { get; }
    public string? RequestId { get; }

    internal ApiResponse(T data, HttpResponseMessage response)
    {
        Data = data;
        Response = response;
        RequestId = response.Headers.TryGetValues("x-typesafe-request-id", out var values) ? values.FirstOrDefault() : null;
    }

    public void Dispose() => Response.Dispose();
}
