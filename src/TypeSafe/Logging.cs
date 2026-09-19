namespace TypeSafe;

public interface ITypeSafeLogger
{
    void Log(TypeSafeLogLevel level, string message);
}

internal sealed class ConsoleTypeSafeLogger : ITypeSafeLogger
{
    public void Log(TypeSafeLogLevel level, string message) =>
        Console.Error.WriteLine($"[TypeSafe] {level}: {message}");
}

public static class CredentialRedactor
{
    private static readonly HashSet<string> SecretHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "proxy-authorization", "x-api-key"
    };

    private static readonly HashSet<string> OpaqueHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "cookie", "set-cookie"
    };

    public static string RedactHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) =>
        string.Join(", ", headers.Select(header =>
        {
            var value = OpaqueHeaders.Contains(header.Key)
                ? "***"
                : SecretHeaders.Contains(header.Key)
                    ? string.Join(", ", header.Value.Select(value => RedactSecret(header.Key, value)))
                    : string.Join(", ", header.Value);
            return $"{header.Key}: {value}";
        }));

    private static string RedactSecret(string header, string value)
    {
        var scheme = "";
        var secret = value;
        if (header.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("proxy-authorization", StringComparison.OrdinalIgnoreCase))
        {
            var separator = value.IndexOf(' ');
            if (separator > 0)
            {
                scheme = value[..(separator + 1)];
                secret = value[(separator + 1)..];
            }
        }

        return secret.Length > 8 ? $"{scheme}***{secret[^4..]}" : $"{scheme}***";
    }
}
