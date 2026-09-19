# TypeSafe .NET SDK

An idiomatic .NET port of `typesafe-ai/typesafe-sdk-js` v0.6.0. The library targets
`net11.0`, uses C# 15 and `System.Text.Json`, and has no runtime package dependencies.

> [!IMPORTANT]
> This is an unofficial, vibe coded community port and is not supported by TypeSafe. Use it at your own risk.

> [!Note]
Code for my blog post "[TypeSafe's Jev AI Model in .NET: A Community SDK for Structured AI Output in C#](https://laurentkempe.com/2026/09/19/typesafe-jev-dotnet-sdk-introduction/)"

## Quickstart

The upstream JavaScript call is `client.systemOne({ state, questions })`. Its .NET
equivalent uses `SystemOneRequest`:

```csharp
using TypeSafe;

using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    ApiKey = "your-api-key"
});

var result = await client.SystemOneAsync(new SystemOneRequest
{
    State = new { text = "Evaluate this statement." },
    Questions = new Dictionary<string, Question>
    {
        ["safe"] = Questions.Noul(
            instructions: "Assess safety.",
            criteria: new NoulCriteria
            {
                True = "The statement is safe.",
                False = "The statement is unsafe."
            }),
        ["topic"] = Questions.Choice(
            instructions: new { task = "Choose a topic." },
            criteria: new Dictionary<string, object?>
            {
                ["science"] = "Scientific content",
                ["other"] = null
            }),
        ["quality"] = Questions.Score(
            instructions: "Rate quality.",
            criteria: ["poor", new { label = "good" }, "excellent"])
    }
});

var safeProbability = ((NoulAnswer)result.Answers["safe"]).Noul;
IReadOnlyList<ModelCard> models = await client.ModelsAsync();
```

`State`, instructions, and criterion descriptions accept a JSON string, object,
array, or null. Noul criteria are optional and use JSON keys `true` and `false`.
Question dictionaries cannot be empty, choice criteria cannot be empty, and score
criteria require at least two ordered entries.

`SystemOneRequest.AdditionalProperties` forwards extra top-level JSON fields.
Pass a `CancellationToken` as the final method argument to cancel a call:

```csharp
var models = await client.ModelsAsync(cancellationToken: cancellationToken);
```

## Configuration and retries

Options use explicit value, then a nonblank environment variable, then the default:

| Option | Environment | Default |
|---|---|---|
| API key | `TYPESAFE_API_KEY` | required |
| Base URL | `TYPESAFE_BASE_URL` | `https://api.typesafe.ai` |
| Model | `TYPESAFE_DEFAULT_MODEL` | `jev-latest` |
| Log level | `TYPESAFE_LOG_LEVEL` (`debug`, `info`, `warn`, `error`, or `off`) | `Warning` |
| Timeout | — | 10 seconds |

The resolved retry defaults are two retries, 500 ms initial exponential backoff,
a 5 second delay cap, 0.25 subtractive jitter, statuses 408/429/500–599,
`Retry-After` support with a 60 second cap, and retries for connection failures
and timeouts. A syntactically valid, nonnegative `retry-after-ms` takes
precedence over `Retry-After`; an invalid value falls through to `Retry-After`,
while an over-cap valid value falls back to backoff.

`TypeSafeClientOptions.Retry` partially overrides defaults.
`RequestOptions.Retry` then partially overrides the client policy for one call,
including every retry setting. Retry requests carry a 1-based
`X-TypeSafe-Retry-Count`.

Resolved settings are available from `TypeSafeClient` through `BaseUrl` (also
`BaseUri`), `DefaultModel`, `LogLevel`, `Timeout`, `Retry`, and
`DefaultHeaders`. Returned headers and retry status sets are read-only copies.

## HTTP ownership, responses, and logging

Authentication, `Accept`, content, User-Agent, `X-TypeSafe-SDK`, runtime, and
retry headers are protected from option overrides. User-Agent and
`X-TypeSafe-SDK` are `typesafe-sdk/0.6.0`.

An injected `HttpClient` is never disposed by `TypeSafeClient`; an internally
created client is disposed with it. Ordinary methods dispose HTTP responses.
`SystemOneWithResponseAsync` and `ModelsWithResponseAsync` return disposable
`ApiResponse<T>` values that retain readable buffered response content and the
`x-typesafe-request-id`.

Provide `ITypeSafeLogger` for minimal logging integration. Without one, the SDK
uses a console logger filtered by `LogLevel`. Credential logging
keeps an authentication scheme and, only for secrets longer than eight
characters, the final four characters. Cookie and Set-Cookie values are fully
redacted.

## JavaScript-to-.NET mapping

| JavaScript SDK | .NET SDK |
|---|---|
| `TypeSafe` | `TypeSafeClient` |
| `client.systemOne({ state, questions })` | `client.SystemOneAsync(new SystemOneRequest { State = ..., Questions = ... })` |
| `client.models.list()` | `client.ModelsAsync()` |
| `APIPromise.withResponse()` | `*WithResponseAsync()` and `ApiResponse<T>` |
| `APIError` | `ApiError` |
| `BadRequestError` | `BadRequestError` |
| `AuthenticationError` | `AuthenticationError` |
| `PermissionDeniedError` | `PermissionDeniedError` |
| `NotFoundError` | `NotFoundError` |
| `UnprocessableEntityError` | `UnprocessableEntityError` |
| `RateLimitError` | `RateLimitError` |
| `InternalServerError` | `InternalServerError` |
| `APIConnectionError` | `ApiConnectionError` |
| `APITimeoutError` | `ApiTimeoutError` |
| `APIUserAbortError` | `ApiUserAbortError` |

API errors preserve parsed JSON, raw non-JSON text, or a null body. Validation
locations omit the leading `body` segment. Unknown polymorphic answer types and
malformed response shapes fail with `TypeSafeError`.

## Sample console application

`samples/TypeSafe.Sample` is a runnable end-to-end example. It lists the
available models, evaluates a support ticket with one `noul`, one `choice`, and
one `score` question, prints the typed answers, request id, and token usage, and
maps every SDK error to a distinct exit code.

```powershell
$env:TYPESAFE_API_KEY = "sk-..."
dotnet run --project samples/TypeSafe.Sample
```

```bash
export TYPESAFE_API_KEY="sk-..."
dotnet run --project samples/TypeSafe.Sample
```

Point the sample at a different host with `TYPESAFE_BASE_URL`, or change the
model with `TYPESAFE_DEFAULT_MODEL`. Press Ctrl+C to cancel an in-flight
request; the sample surfaces this as `ApiUserAbortError`.

## Building and testing

```bash
dotnet build TypeSafe.slnx -c Release
dotnet test TypeSafe.slnx -c Release
dotnet pack src/TypeSafe/TypeSafe.csproj -c Release
```

