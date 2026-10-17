using TypeSafe.Cli.Formatting;

namespace TypeSafe.Cli.Commands;

public static class CommandExecutor
{
    public static async Task<int> ExecuteAsync(
        CliContext context,
        CliGlobalOptions globalOptions,
        Func<TypeSafeClient, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CliClientFactory.CreateClient(globalOptions, context.HttpClient);
            await action(client, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (CliValidationException exception)
        {
            OutputFormatter.WriteError(exception.Message, globalOptions.Json, context.Error);
            return 2;
        }
        catch (ArgumentException exception)
        {
            OutputFormatter.WriteError(exception.Message, globalOptions.Json, context.Error);
            return 2;
        }
        catch (InvalidOperationException exception)
        {
            OutputFormatter.WriteError(exception.Message, globalOptions.Json, context.Error);
            return 2;
        }
        catch (FileNotFoundException exception)
        {
            OutputFormatter.WriteError(exception.Message, globalOptions.Json, context.Error);
            return 2;
        }
        catch (AuthenticationError exception)
        {
            OutputFormatter.WriteError($"Authentication error: {exception.Message}", globalOptions.Json, context.Error);
            return 1;
        }
        catch (ApiError exception)
        {
            var requestId = exception.RequestId is null ? "" : $" (request id: {exception.RequestId})";
            OutputFormatter.WriteError($"TypeSafe API error: {exception.Message}{requestId}", globalOptions.Json, context.Error);
            return 1;
        }
        catch (TypeSafeError exception)
        {
            OutputFormatter.WriteError($"TypeSafe error: {exception.Message}", globalOptions.Json, context.Error);
            return 1;
        }
        catch (HttpRequestException exception)
        {
            OutputFormatter.WriteError($"Network error: {exception.Message}", globalOptions.Json, context.Error);
            return 1;
        }
        catch (Exception exception)
        {
            OutputFormatter.WriteError($"Unexpected error: {exception.Message}", globalOptions.Json, context.Error);
            return 1;
        }
    }
}
