namespace TypeSafe.Cli.IO;

public static class InputReader
{
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
