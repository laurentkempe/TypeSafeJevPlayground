namespace TypeSafe.Mcp;

public sealed class TypeSafeMcpOptions
{
    public const string SectionName = "TypeSafe";

    public string? ApiKey { get; set; }
    public Uri? BaseUrl { get; set; }
    public string? Model { get; set; }
}
