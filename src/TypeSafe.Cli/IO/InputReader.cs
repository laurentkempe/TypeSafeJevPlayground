namespace TypeSafe.Cli.IO;

public static class InputReader
{
    /// <summary>
    /// Reads the state like <see cref="ReadStateAsync"/>, but returns an <see cref="Image"/> when the
    /// state is an <c>@filepath</c> pointing to a PNG or JPEG file, and parsed JSON/text otherwise.
    /// </summary>
    public static async Task<object> ReadStateValueAsync(
        string? stateArgument,
        TextReader? stdin = null,
        bool? isInputRedirected = null,
        CancellationToken cancellationToken = default)
    {
        if (stateArgument is { Length: > 1 } && stateArgument.StartsWith('@'))
        {
            var filePath = stateArgument[1..];
            if (File.Exists(filePath))
            {
                await using var stream = File.OpenRead(filePath);
                var header = new byte[8];
                var headerLength = await stream.ReadAtLeastAsync(
                    header.AsMemory(), header.Length, throwOnEndOfStream: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (ImageInput.IsSupportedImage(header.AsSpan(0, headerLength)))
                {
                    var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                    return new Image(bytes);
                }
            }
        }

        var text = await ReadStateAsync(stateArgument, stdin, isInputRedirected, cancellationToken).ConfigureAwait(false);
        return StateParser.Parse(text);
    }

    public static async Task<string> ReadStateAsync(
        string? stateArgument,
        TextReader? stdin = null,
        bool? isInputRedirected = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(stateArgument))
        {
            if (stateArgument == "-")
            {
                var reader = stdin ?? Console.In;
                var piped = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(piped))
                {
                    throw new InvalidOperationException("Standard input was empty.");
                }

                return piped.Trim();
            }

            if (stateArgument.StartsWith('@'))
            {
                var filePath = stateArgument[1..];
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    throw new ArgumentException("File path after '@' cannot be empty.");
                }

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"State file not found: {filePath}", filePath);
                }

                var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                return content;
            }

            return stateArgument;
        }

        var isRedirected = isInputRedirected ?? (stdin != null ? true : Console.IsInputRedirected);
        if (isRedirected)
        {
            var reader = stdin ?? Console.In;
            var piped = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(piped))
            {
                throw new InvalidOperationException("Standard input was empty.");
            }

            return piped.Trim();
        }

        throw new ArgumentException("State argument is required or must be provided via standard input.");
    }

    public static async Task<string> ReadFileOrTextAsync(
        string fileOrText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileOrText);

        if (fileOrText.StartsWith('@'))
        {
            var filePath = fileOrText[1..];
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path after '@' cannot be empty.");
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }

            return await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(fileOrText))
        {
            return await File.ReadAllTextAsync(fileOrText, cancellationToken).ConfigureAwait(false);
        }

        return fileOrText;
    }
}
