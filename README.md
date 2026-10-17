# Unofficial TypeSafe.ai .NET SDK

An idiomatic .NET port of `typesafe-ai/typesafe-sdk-js` v0.7.0. The library targets
`net11.0`, uses C# 15 and `System.Text.Json`, and has no runtime package dependencies.

> [!IMPORTANT]
> This is an unofficial, vibe coded community port and is not supported by TypeSafe. Use it at your own risk.

> [!Note]
> Code for my blog posts
> 1. [TypeSafe's Jev AI Model in .NET: A Community SDK for Structured AI Output in C#](https://laurentkempe.com/2026/09/19/typesafe-jev-dotnet-sdk-introduction/)
> 2. [Building a TypeSafe's Jev MCP server with .NET](https://laurentkempe.com/2026/10/10/building-a-typesafe-jev-mcp-server-dotnet/)
> 3. [TypeSafe Jev .NET SDK 0.7.0: Experimental Images and Bounding Boxes](https://laurentkempe.com/2026/10/17/typesafe-dotnet-sdk-v0-7-0/)

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
array, or image. Instructions and criterion descriptions can also be null; state
cannot. Noul criteria are optional and use JSON keys `true` and `false`; a noul
question must have instructions or at least one non-null outcome description.
Question dictionaries cannot be empty, choice criteria cannot be empty, and score
criteria require at least two ordered entries.

`SystemOneRequest.AdditionalProperties` forwards extra top-level JSON fields.
Reserved fields (`state`, `questions`, `model`, `assets`) cannot be overridden
through extension data. Pass a `CancellationToken` as the final method argument to cancel a call:

```csharp
var models = await client.ModelsAsync(cancellationToken: cancellationToken);
```

## Images and bounding boxes (experimental)

v0.7.0 adds image assets, asset-aware text, and `box` questions. Enable images
explicitly with `TypeSafe-Danger-Zone-Images: true`; box questions require
`jev-preview`. The API accepts PNG and JPEG and rejects unsupported formats.

```csharp
using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    ApiKey = "your-api-key",
    DefaultHeaders = new Dictionary<string, string>
    {
        ["TypeSafe-Danger-Zone-Images"] = "true"
    }
});
var photo = new Image(await File.ReadAllBytesAsync("photo.jpg"));
var result = await client.SystemOneAsync(new SystemOneRequest
{
    State = photo,
    Model = "jev-preview",
    Questions = new Dictionary<string, Question>
    {
        ["dog"] = Questions.Box(TextWithAssets.Create($"Locate the dog in {photo}"))
    }
});
var box = ((BoxAnswer)result.Answers["dog"]).Box;
Console.WriteLine($"{box.Top}, {box.Bottom}, {box.Left}, {box.Right}");
```

Coordinates are fractions of image dimensions, from zero to one. Box confidence
may be null. `Usage.InputTokens` and `OutputTokens` are now nullable too: null means
the API did not report a count, not zero.

Images can appear in nested state, instructions, or criteria. `Image.FromBase64`
decodes plain base64 (not a data URL); image bytes are copied at construction.
Use `TextWithAssets.Create($"Compare {photo} with ...")` instead of ordinary string
interpolation to attach interpolated images. `TextWithAssets.Join(images, ", ")`
joins asset-bearing values. Alternatively set `SystemOneRequest.Assets` to a
dictionary such as `{ ["<photo>"] = photo }` and write `<photo>` in the request
content. Missing image references and unused asset names fail before HTTP.
Asset requests use multipart form data; ordinary requests remain JSON.

## Configuration and retries

Options use explicit value, then a nonblank environment variable, then the default:

| Option | Environment | Default |
|---|---|---|
| API key | `TYPESAFE_API_KEY` | required |
| Base URL | `TYPESAFE_BASE_URL` | `https://api.typesafe.ai` |
| Model | `TYPESAFE_DEFAULT_MODEL` | `jev-latest` |
| Log level | `TYPESAFE_LOG_LEVEL` (`debug`, `info`, `warn`, `error`, or `off`) | `Warning` |
| Timeout | — | 10 seconds |
| Compression | — | `true` (zstd for request bodies of at least 1,024 UTF-8 bytes) |

Compression uses .NET 11's built-in `ZstandardStream`, with no package dependency.
Set `TypeSafeClientOptions.Compression = false` to disable it. JSON and multipart
bodies are encoded/compressed once and replayed unchanged across retries.

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
`X-TypeSafe-SDK` are `typesafe-sdk/0.7.0`. Content-Encoding and Content-Length
are also protected so overrides cannot corrupt compressed or multipart bodies.

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
| `image(bytes)` / `Image.fromBase64(text)` | `new Image(bytes)` / `Image.FromBase64(text)` |
| `withAssets` / `withAssets.join(...)` | `TextWithAssets.Create($"...")` / `TextWithAssets.Join(...)` |
| `box(instructions)` | `Questions.Box(instructions)` / `BoxAnswer` |
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

## Keeping the port current

[`upstream.json`](upstream.json) pins v0.7.0 to upstream commit
`b844cd1aca12855a5d24a2df52a1401c86e7d37a` and maps upstream modules to .NET
surfaces. The [v0.6.0 → v0.7.0 comparison](https://github.com/typesafe-ai/typesafe-sdk-js/compare/v0.6.0...v0.7.0)
introduced images/multipart, boxes, zstd, nullable usage and stricter validation
(null state and undescribed noul questions now fail). Invalid requests now fault
the `SystemOneWithResponseAsync` task rather than throwing before returning it.
Runtime-built score lists were already supported. Nested nonfinite numbers are
sent as strings, matching upstream.

Run the comparison with an authenticated GitHub CLI and PowerShell 7:

```powershell
.\scripts\Compare-Upstream.ps1 -OutputDirectory "$env:TEMP\typesafe-upstream" -Check
# Include unreleased upstream commits:
.\scripts\Compare-Upstream.ps1 -TargetRef main -OutputDirectory "$env:TEMP\typesafe-upstream"
```

The script emits Markdown and JSON (including patches and .NET file mappings).
`-Check` exits nonzero on drift; network/authentication failures also fail explicitly.
The **Upstream SDK drift** workflow runs weekly and on manual dispatch, publishing
the report in the run summary and an artifact. It only has read permissions:
it does not silently change the compatibility pin or port code. Scheduled checks
activate once the workflow is on the default branch.

Use the [`update-typesafe-sdk` skill](.agents/skills/update-typesafe-sdk/SKILL.md)
for the repeatable review, port, regression-test, and pin-update procedure.
New upstream files are marked unmapped rather than ignored.

Intentional platform differences: .NET keeps its explicit request types, native
Task/cancellation and HttpClient ownership, and uses immutable in-memory image
bytes instead of JavaScript Blob objects. The JavaScript Node 22 requirement and
removed exported TypeScript payload types do not apply. The MCP tools remain
text/JSON-oriented (no image upload or box tool); use the SDK for experimental
vision calls. MCP results preserve nullable token counts.

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

## MCP server (`TypeSafe.Mcp`)

`src/TypeSafe.Mcp` is a stdio [Model Context Protocol](https://modelcontextprotocol.io) server built
on the official C# MCP SDK. It lets any MCP client (Claude Desktop, VS Code, Rider, Cursor…) ask
System One questions. It ships as a .NET global tool and requires the .NET 11 runtime.

```bash
dotnet tool install -g TypeSafe.Mcp
```

The API key comes from `--api-key`, then `TypeSafe__ApiKey`, then `TYPESAFE_API_KEY`. `--base-url`
and `--model` follow the same pattern (falling back to `TYPESAFE_BASE_URL` and
`TYPESAFE_DEFAULT_MODEL`). The server starts without a key and reports a tool error until one is set.

| Tool | What it does |
|---|---|
| `typesafe_noul` | Probability (0–1) that a yes/no statement about the state is true. |
| `typesafe_choice` | Picks one label from an unordered set, with a probability per label. |
| `typesafe_score` | Places the state on an ordered rubric (≥ 2 levels) and returns a weighted score. |
| `typesafe_evaluate` | Any mix of named noul/choice/score questions about the same state in one request. |
| `typesafe_list_models` | Lists the models available to your key. |

VS Code / Rider `mcp.json`:

```json
{
  "servers": {
    "typesafe": {
      "type": "stdio",
      "command": "typesafe-mcp",
      "env": { "TYPESAFE_API_KEY": "sk-..." }
    }
  }
}
```

Claude Desktop `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "typesafe": {
      "command": "typesafe-mcp",
      "env": { "TYPESAFE_API_KEY": "sk-..." }
    }
  }
}
```

To try a local build, pack and install from the output folder:

```bash
dotnet pack src/TypeSafe.Mcp/TypeSafe.Mcp.csproj -c Release -o ./nupkg
dotnet tool install -g TypeSafe.Mcp --add-source ./nupkg
```

`dotnet publish` produces a Native AOT, self-contained executable that needs no .NET runtime
(the C++ build tools for your platform are required, see
[Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites)):

```bash
dotnet publish src/TypeSafe.Mcp/TypeSafe.Mcp.csproj -c Release -r win-x64 -o ./publish
```

Use the matching runtime identifier (`linux-x64`, `osx-arm64`, …) and point the MCP client's `command`
at `./publish/TypeSafe.Mcp(.exe)`. The `TypeSafe` SDK and the server use source-generated JSON only, so the
build is free of trimming and AOT warnings. `dotnet pack` still produces the framework-dependent global tool.

## Building and testing

```bash
dotnet build TypeSafe.slnx -c Release
dotnet test TypeSafe.slnx -c Release
dotnet pack src/TypeSafe/TypeSafe.csproj -c Release
dotnet pack src/TypeSafe.Mcp/TypeSafe.Mcp.csproj -c Release
```
