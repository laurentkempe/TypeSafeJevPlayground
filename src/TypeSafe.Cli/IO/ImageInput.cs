namespace TypeSafe.Cli.IO;

public static class ImageInput
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] JpegSignature = [0xff, 0xd8, 0xff];

    public static bool IsSupportedImage(ReadOnlySpan<byte> data) =>
        data.StartsWith(PngSignature) || data.StartsWith(JpegSignature);

    /// <summary>
    /// Parses <c>--image</c> values in <c>name=path</c> or <c>path</c> format into request assets keyed by
    /// their <c>&lt;name&gt;</c> reference, which can then be used in state, instructions, or criteria text.
    /// </summary>
    public static async Task<Dictionary<string, Image>> ReadAssetsAsync(
        IEnumerable<string> imageArguments,
        CancellationToken cancellationToken = default)
    {
        var assets = new Dictionary<string, Image>(StringComparer.Ordinal);
        foreach (var entry in imageArguments)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var separatorIndex = entry.IndexOf('=');
            string name;
            string path;
            if (separatorIndex < 0)
            {
                path = entry.Trim().TrimStart('@');
                name = Path.GetFileNameWithoutExtension(path);
            }
            else
            {
                name = entry[..separatorIndex].Trim();
                path = entry[(separatorIndex + 1)..].Trim().TrimStart('@');
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                throw new CliValidationException($"Invalid image '{entry}'. Expected format 'name=path' or 'path'.");

            var reference = name.StartsWith('<') && name.EndsWith('>') ? name : $"<{name}>";
            if (assets.ContainsKey(reference))
                throw new CliValidationException($"Duplicate image name '{reference}'.");

            assets[reference] = await ReadImageAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return assets;
    }

    public static async Task<Image> ReadImageAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Image file not found: {path}", path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
            throw new CliValidationException($"Image file is empty: {path}");

        return new Image(bytes);
    }
}
