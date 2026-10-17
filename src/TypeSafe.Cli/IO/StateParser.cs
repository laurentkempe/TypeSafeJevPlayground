using System.Text.Json;

namespace TypeSafe.Cli.IO;

public static class StateParser
{
    public static object Parse(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var trimmed = state.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fallback to raw string
            }
        }

        return state;
    }
}
