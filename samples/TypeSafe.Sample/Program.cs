using System.Text.Json;
using TypeSafe;

// Sample console application for the TypeSafe .NET SDK.
//
// Set your API key before running:
//   $env:TYPESAFE_API_KEY = "sk-..."   (PowerShell)
//   export TYPESAFE_API_KEY="sk-..."   (bash)
//
// Optional environment overrides: TYPESAFE_BASE_URL, TYPESAFE_DEFAULT_MODEL, TYPESAFE_LOG_LEVEL.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
    Console.WriteLine();
    Console.WriteLine("Cancellation requested...");
};

try
{
    using var client = new TypeSafeClient(new TypeSafeClientOptions
    {
        // ApiKey is read from TYPESAFE_API_KEY when omitted.
        Timeout = TimeSpan.FromSeconds(30),
        Retry = new RetryOptions { MaxRetries = 3 },
        Logger = new ConsoleLogger(),
        LogLevel = TypeSafeLogLevel.Information,
    });

    Console.WriteLine($"Base URL      : {client.BaseUrl}");
    Console.WriteLine($"Default model : {client.DefaultModel}");
    Console.WriteLine();

    await ListModelsAsync(client, cancellation.Token);
    await EvaluateSupportTicketAsync(client, cancellation.Token);

    return 0;
}
catch (ApiUserAbortError)
{
    Console.Error.WriteLine("Request aborted by the user.");
    return 130;
}
catch (ApiTimeoutError error)
{
    Console.Error.WriteLine($"Request timed out after {error.Timeout}. Consider raising Timeout.");
    return 1;
}
catch (RateLimitError error)
{
    var retryIn = error.RetryDelay is { } delay ? $" Retry in {delay}." : string.Empty;
    Console.Error.WriteLine($"Rate limited (request {error.RequestId}).{retryIn}");
    return 1;
}
catch (AuthenticationError error)
{
    Console.Error.WriteLine($"Authentication failed: {error.Message}. Check TYPESAFE_API_KEY.");
    return 1;
}
catch (ApiError error)
{
    Console.Error.WriteLine($"API error {(int)error.StatusCode}: {error.Message} (request {error.RequestId})");
    if (error.Body is not null)
        Console.Error.WriteLine($"Body: {error.Body}");
    return 1;
}
catch (ApiConnectionError error)
{
    Console.Error.WriteLine($"Could not reach the TypeSafe API: {error.Message}");
    return 1;
}
catch (TypeSafeError error)
{
    // Covers configuration problems such as a missing API key, and validation errors.
    Console.Error.WriteLine($"TypeSafe SDK error: {error.Message}");
    return 1;
}

static async Task ListModelsAsync(TypeSafeClient client, CancellationToken cancellationToken)
{
    Console.WriteLine("Available models");
    Console.WriteLine("----------------");

    var models = await client.ModelsAsync(cancellationToken: cancellationToken);
    foreach (var model in models)
        Console.WriteLine($"  {model.Name,-16} {model.ReleaseDate,-12} {model.Description}");

    Console.WriteLine();
}

static async Task EvaluateSupportTicketAsync(TypeSafeClient client, CancellationToken cancellationToken)
{
    // State can be any JSON-compatible value: a string, object, or array.
    var state = new
    {
        ticket = new
        {
            subject = "The Checkout process on the website fails with a 500 error",
            body = "Every attempt to pay with a saved card returns an error page. This started this morning.",
            customer_tier = "enterprise",
        },
    };

    var request = new SystemOneRequest
    {
        State = state,
        Questions = new Dictionary<string, Question>
        {
            ["is_bug"] = Questions.Noul(
                "Does the ticket describe a defect in the product?",
                new NoulCriteria
                {
                    True = "The customer reports broken or incorrect behaviour.",
                    False = "The ticket is a question, feature request, or billing issue.",
                }),

            ["category"] = Questions.Choice(
                "Which team should own this ticket?",
                new Dictionary<string, object?>
                {
                    ["billing"] = "Payments, invoices, and refunds.",
                    ["platform"] = "Availability, errors, and performance of the website.",
                    ["account"] = "Login, permissions, and provisioning.",
                }),

            ["urgency"] = Questions.Score(
                "How urgent is this ticket?",
                ["not urgent", "low", "medium", "high", "drop everything"]),
        },
    };

    // Use the *WithResponseAsync overloads when you also need the request id or raw response.
    using var response = await client.SystemOneWithResponseAsync(
        request,
        new RequestOptions { Timeout = TimeSpan.FromSeconds(20) },
        cancellationToken);

    var result = response.Data;

    Console.WriteLine("Evaluation");
    Console.WriteLine("----------");
    Console.WriteLine($"  model      : {result.Model}");
    Console.WriteLine($"  request id : {response.RequestId ?? "(none)"}");

    foreach (var (name, answer) in result.Answers)
        Console.WriteLine($"  {name,-10} : {Describe(answer)}");

    Console.WriteLine($"  usage      : {result.Usage.InputTokens} in / {result.Usage.OutputTokens} out");
    Console.WriteLine();
}

static string Describe(Answer answer) => answer switch
{
    NoulAnswer noul => $"noul {noul.Noul:P1}",
    ChoiceAnswer choice =>
        $"{choice.Choice} (confidence {choice.Confidence:P1}) " +
        $"[{string.Join(", ", choice.Probabilities.Select(p => $"{p.Key} {p.Value:P0}"))}]",
    ScoreAnswer score =>
        $"{score.Score:0.##} (confidence {score.Confidence:P1}) " +
        $"legend: {JsonSerializer.Serialize(score.Legend)}"
};

file sealed class ConsoleLogger : ITypeSafeLogger
{
    public void Log(TypeSafeLogLevel level, string message) =>
        Console.Error.WriteLine($"[typesafe:{level.ToString().ToLowerInvariant()}] {message}");
}
