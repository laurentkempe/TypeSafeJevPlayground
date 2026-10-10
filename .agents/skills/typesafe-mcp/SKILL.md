---
name: typesafe-mcp
license: MIT
description: >
  Get fast, typed AI judgments directly from the TypeSafe MCP server
  (typesafe-mcp): yes/no probabilities (typesafe_noul), classification
  (typesafe_choice), ordered rubric scores (typesafe_score), several questions
  over the same state in one call (typesafe_evaluate), and available models
  (typesafe_list_models). Use when an agent has to judge text or JSON (triage,
  routing, spam/toxicity checks, sentiment, urgency, relevance, claim
  verification, or ranking candidates) and a calibrated probability is more
  useful than a free-text opinion. Also use to prototype TypeSafe questions
  interactively before coding them against the TypeSafe .NET SDK, and to
  install or configure the server in an MCP client.
---

# Use the TypeSafe MCP server

`typesafe-mcp` is an unofficial stdio [MCP](https://modelcontextprotocol.io) server. It puts
[TypeSafe AI](https://docs.typesafe.ai/introduction) **System One** models (by default **Jev**,
`jev-latest`) behind MCP tools. A System One model does not write text. It reads a **state** and
one or more **questions**, then returns **typed answers with probabilities**. Treat each tool call
as a programmable piece of common sense: you (the agent) own the workflow, and the model returns
a focused judgment.

For the programming model, primitive semantics and prompting guidance, the live docs are the
source of truth. Start at the [documentation index](https://docs.typesafe.ai/llms.txt); pages are
served as Markdown when you append `.md` (for example
[primitives](https://docs.typesafe.ai/primitives.md) or
[confidence](https://docs.typesafe.ai/confidence.md)). This skill covers what is specific to this
server: which tool to call, its argument shapes, result shapes, errors and setup.

## Pick the tool

| Need | Tool | Returns (under `answers.result`) |
| --- | --- | --- |
| Whether one condition holds | `typesafe_noul` | `noul`: probability (0–1) that the statement is true |
| One label from an unordered set | `typesafe_choice` | `choice`, `confidence`, `probabilities` per label |
| Position on an ordered scale | `typesafe_score` | `score`, `confidence`, `legend`, `probabilities` per level |
| Several independent judgments of the same state | `typesafe_evaluate` | one answer per question name you chose |
| See which models the key can use | `typesafe_list_models` | list of `name`, `description`, `releaseDate` |

All question tools are read-only and idempotent, but each call is a paid network request to the
TypeSafe API.

- If several labels can apply at once, use one noul per label (in a single `typesafe_evaluate`),
  not a choice.
- If nothing may fit, give the choice an explicit no-match label such as `none`.
- To rank items, give each item a comparable noul or score and sort in code. Do not ask the model
  to produce an order.

## Arguments

Shared by `typesafe_noul`, `typesafe_choice`, `typesafe_score` and `typesafe_evaluate`:

- `state` (required, non-empty string). Everything the judgment depends on. If the trimmed string
  starts with `{` or `[` and parses as JSON, it is sent as **structured JSON**. Otherwise it is
  sent as plain text. When the context has several parts (message, customer, policy, candidates),
  prefer a JSON object with named fields over concatenated prose.
- `instructions` (required, non-empty). The judgment to make. Refer to state fields with
  backticked paths, such as `` `ticket.messages[0].text` ``. Question names are not sent to the
  model, so put the full meaning into the instructions.
- `model` (optional). A name from `typesafe_list_models`. If omitted, the server's configured
  default is used (`jev-latest` unless overridden).

Tool-specific:

- `typesafe_noul`: phrase `instructions` as a statement or a yes/no question. The optional
  `trueCriteria` / `falseCriteria` describe what makes it true or false.
- `typesafe_choice`: `choices` is an object mapping **label → description of when it applies**.
  It needs at least one entry. Labels are unique and non-empty, and the answer's `choice` is one of
  these labels.
- `typesafe_score`: `levels` is an array of at least two descriptions, ordered **lowest →
  highest**. Each level should describe a concrete situation and make sense without the others.
- `typesafe_evaluate`: `questions` is an object mapping a unique name you choose (for example
  `is_spam`, `topic`, `urgency`) to
  `{ "type": "noul" | "choice" | "score", "instructions": ..., <type fields> }`. Only fill the
  fields for that type: `trueCriteria` / `falseCriteria` for noul, `choices` for choice, `levels`
  for score.

## Examples

Single yes/no check:

```json
{
  "tool": "typesafe_noul",
  "arguments": {
    "state": "Congratulations! You won a $500 gift card. Click here to claim.",
    "instructions": "This message is unsolicited spam or a phishing attempt.",
    "trueCriteria": "Unsolicited promotion, prize bait, or requests to click unknown links.",
    "falseCriteria": "A legitimate personal or transactional message."
  }
}
```

Several judgments of one support ticket in a single request (they run in parallel, cost less and
return faster than separate calls):

```json
{
  "tool": "typesafe_evaluate",
  "arguments": {
    "state": "{\"ticket\":{\"subject\":\"Charged twice\",\"body\":\"I was billed twice this month and need a refund today.\"},\"plan\":\"pro\"}",
    "questions": {
      "topic": {
        "type": "choice",
        "instructions": "Which team should handle `ticket`?",
        "choices": {
          "billing": "Payments, invoices, refunds, charges.",
          "technical": "Bugs, errors, outages, integrations.",
          "account": "Login, profile, plan changes.",
          "none": "None of the above."
        }
      },
      "urgency": {
        "type": "score",
        "instructions": "How urgent is `ticket` for the customer?",
        "levels": [
          "No time pressure; informational.",
          "Should be handled within a few days.",
          "Blocks the customer or involves money; handle today."
        ]
      },
      "wants_refund": {
        "type": "noul",
        "instructions": "The customer explicitly asks for a refund in `ticket.body`."
      }
    }
  }
}
```

Result shape (structured content):

```json
{
  "model": "jev-...",
  "answers": {
    "topic": { "type": "choice", "choice": "billing", "confidence": 0.97, "probabilities": { "billing": 0.98, "technical": 0.01, "account": 0.01, "none": 0.0 } },
    "urgency": { "type": "score", "score": 0.9, "confidence": 0.8, "legend": { "...": "..." }, "probabilities": { "...": 0.0 } },
    "wants_refund": { "type": "noul", "noul": 0.96 }
  },
  "usage": { "inputTokens": 312, "outputTokens": 9 }
}
```

The single-question tools use the same shape, with the one answer under the key `result`.

## Read the answers

- **Noul** is the probability that the statement is true. A value near 0.5 means the model is
  undecided; it does not mean a "medium" amount of the property. There is no separate confidence.
- **Choice/Score `confidence`** measures how concentrated the distribution is. It does not tell you
  whether the overall action is correct or permitted. Several acceptable labels can spread the
  probability, so a low confidence on a harmless preference is fine.
- **Score** is a probability-weighted position on the rubric. Use `legend` to map it back to the
  levels, and `probabilities` to see the full spread.
- Pick thresholds that fit the consequences of acting. For uncertain or high-stakes answers, ask
  the user, gather more evidence and call again, or escalate. Do not present a typed answer as
  established fact: typed output guarantees the format, not the truth.
- Keep policy on your side. For example, "escalate if any serious violation" should be a separate
  noul per condition combined in your logic, not a single blended score.
- Put independent questions about the same state into one `typesafe_evaluate` call. The questions
  cannot see each other's answers. Only make a follow-up call when an earlier answer decides what
  evidence, state or options come next.

## Errors

Errors come back as MCP tool errors with a readable message:

- `state cannot be empty.` / `At least one question is required.`: supply the missing input.
- `Invalid question: ...`: the question failed validation before any network call. Common causes:
  empty instructions, empty `choices`, fewer than two `levels`, an unknown `type` in
  `typesafe_evaluate`, or an empty question name. For `typesafe_evaluate`, the message is prefixed
  with the question name, for example `Question 'urgency': ...`.
- `TypeSafe error: ... --api-key ... TYPESAFE_API_KEY`: the server has no API key configured (see
  setup below). Tell the user; do not retry.
- `TypeSafe API error: ... (request id: ...)`: the service rejected the request or failed. Check
  the model name and the input size. Quote the request id to the user if the error persists.

## Install and configure

The server requires the .NET 11 runtime or newer. Alternatively, publish it as a Native AOT
executable that needs no runtime.

```bash
dotnet tool install -g TypeSafe.Mcp      # command: typesafe-mcp
```

| Setting | Command line | Environment | Default |
| --- | --- | --- | --- |
| API key | `--api-key` | `TypeSafe__ApiKey`, then `TYPESAFE_API_KEY` | required |
| Base URL | `--base-url` | `TypeSafe__BaseUrl`, then `TYPESAFE_BASE_URL` | `https://api.typesafe.ai` |
| Model | `--model` | `TypeSafe__Model`, then `TYPESAFE_DEFAULT_MODEL` | `jev-latest` |

Pass the key through an environment variable rather than command-line arguments, and never write
real keys into files you commit. The server starts without a key, but every tool call then fails
with the missing-key error.

VS Code / Rider (`mcp.json`):

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

Claude Desktop (`claude_desktop_config.json`): use `"mcpServers"` with the same `command` and
`env`. To run from source instead, use
`"command": "dotnet", "args": ["run", "--project", "path/to/src/TypeSafe.Mcp"]`. For a Native AOT
build, run `dotnet publish src/TypeSafe.Mcp/TypeSafe.Mcp.csproj -c Release -r win-x64 -o ./publish`
and point `command` at the published `TypeSafe.Mcp` executable.

## From prototype to code

Questions that behave well through the tools translate one-to-one to the TypeSafe .NET SDK in
`src/TypeSafe`. Build `Questions.Noul`, `Questions.Choice` and `Questions.Score` with the same
instructions and criteria, send them in a `SystemOneRequest` with the same state through
`TypeSafeClient.SystemOneAsync`, and read the typed `NoulAnswer`, `ChoiceAnswer` and `ScoreAnswer`.
Before shipping thresholds, test representative cases from the user's domain. When something
fails, inspect the exact state, questions, answers and your own composition logic.
