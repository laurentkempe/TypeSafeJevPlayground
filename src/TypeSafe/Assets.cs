using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TypeSafe;

[JsonConverter(typeof(ImageJsonConverter))]
public sealed class Image
{
    private readonly byte[] _data;
    internal ReadOnlyMemory<byte> Data => _data;
    public string Type => "image";
    private readonly string _reference = $"<image:{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}>";

    public Image(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new TypeSafeError("Image data is empty.");
        _data = data.ToArray();
    }

    public static Image FromBase64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return new Image(Convert.FromBase64String(text));
        }
        catch (FormatException exception)
        {
            throw new TypeSafeError("Image data must contain valid base64 text.", exception);
        }
    }

    public override string ToString() => _reference;

    internal string ContentType =>
        _data.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })
            ? "image/png"
            : _data.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })
                ? "image/jpeg"
                : "application/octet-stream";
}

[JsonConverter(typeof(TextWithAssetsJsonConverter))]
public sealed class TextWithAssets
{
    public string Text { get; }
    internal IReadOnlyList<Image> Assets { get; }

    private TextWithAssets(string text, IReadOnlyList<Image> assets)
    {
        Text = text;
        Assets = assets;
    }

    public static TextWithAssets Create(FormattableString text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextWithAssets(text.ToString(CultureInfo.InvariantCulture), Collect(text.GetArguments()));
    }

    public static TextWithAssets Join(IEnumerable<object?> values, string separator = ",")
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = values.ToArray();
        return new TextWithAssets(string.Join(separator, items), Collect(items));
    }

    private static IReadOnlyList<Image> Collect(IEnumerable<object?> values) =>
        values.SelectMany(value => value switch
        {
            Image image => new[] { image },
            TextWithAssets text => text.Assets,
            _ => Array.Empty<Image>()
        }).Distinct().ToArray();

    public override string ToString() => Text;
}

public sealed class ImageJsonConverter : JsonConverter<Image>
{
    public override Image Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Images must be created from file bytes, not JSON references.");

    public override void Write(Utf8JsonWriter writer, Image value, JsonSerializerOptions options) =>
        writer.WriteStringValue(AssetSerialization.Current?.Reference(value) ?? value.ToString());
}

public sealed class TextWithAssetsJsonConverter : JsonConverter<TextWithAssets>
{
    public override TextWithAssets Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Asset text must be created with TextWithAssets.Create or Join.");

    public override void Write(Utf8JsonWriter writer, TextWithAssets value, JsonSerializerOptions options)
    {
        var text = value.Text;
        if (AssetSerialization.Current is { } context)
            foreach (var image in value.Assets)
                text = text.Replace(image.ToString(), context.Reference(image), StringComparison.Ordinal);
        writer.WriteStringValue(text);
    }
}

internal sealed partial class AssetSerialization
{
    private static readonly AsyncLocal<AssetSerialization?> Scope = new();
    internal static AssetSerialization? Current => Scope.Value;
    private readonly Dictionary<Image, string> _names = [];
    private readonly Dictionary<string, Image> _known = [];

    private AssetSerialization(IReadOnlyDictionary<string, Image>? assets)
    {
        if (assets is null)
            return;
        foreach (var (name, image) in assets)
        {
            if (image is null)
                throw new TypeSafeError($"Asset '{name}' cannot be null.");
            _names[image] = name;
            _known[image.ToString()] = image;
        }
    }

    internal string Reference(Image image)
    {
        _known[image.ToString()] = image;
        return _names.GetValueOrDefault(image) ?? image.ToString();
    }

    internal static HttpContent Serialize(SystemOneRequest request)
    {
        var context = new AssetSerialization(request.Assets);
        var previous = Scope.Value;
        string json;
        try
        {
            Scope.Value = context;
            json = JsonSerializer.Serialize(request, TypeSafeClient.TypeInfo<SystemOneRequest>());
        }
        finally
        {
            Scope.Value = previous;
        }

        var sent = request.Assets is null
            ? new Dictionary<string, Image>()
            : new Dictionary<string, Image>(request.Assets);
        foreach (var name in sent.Keys)
        {
            var escapedName = JsonSerializer.Serialize(name, TypeSafeClient.TypeInfo<string>())[1..^1];
            if (!json.Contains(escapedName, StringComparison.Ordinal))
                throw new TypeSafeError($"Asset '{name}' is not referenced in the request.");
        }
        using var document = JsonDocument.Parse(json);
        foreach (var text in Strings(document.RootElement))
        foreach (Match match in ImageReference().Matches(text))
        {
            if (sent.ContainsKey(match.Value))
                continue;
            if (!context._known.TryGetValue(match.Value, out var image))
                throw new TypeSafeError($"Image reference without an attached image: {match.Value}. Use TextWithAssets.Create for interpolated images.");
            sent[match.Value] = image;
        }

        if (sent.Count == 0)
            return new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var payload = JsonNode.Parse(json)!.AsObject();
        var assets = new JsonObject();
        var index = 0;
        foreach (var alias in sent.Keys)
        {
            var name = $"asset-{index++}";
            assets[alias] = new JsonObject { ["type"] = "image", ["part"] = name };
        }
        payload["assets"] = assets;
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), "request");
        index = 0;
        foreach (var image in sent.Values)
        {
            var name = $"asset-{index++}";
            var content = new ReadOnlyMemoryContent(image.Data);
            content.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
            multipart.Add(content, name, name);
        }
        return multipart;
    }

    private static IEnumerable<string> Strings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            yield return value.GetString()!;
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                yield return property.Name;
                foreach (var text in Strings(property.Value))
                    yield return text;
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                foreach (var text in Strings(item))
                    yield return text;
    }

    [GeneratedRegex("<image:[0-9a-f]{32}>")]
    private static partial Regex ImageReference();
}
