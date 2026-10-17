# TypeSafe.Cli

A fast, scriptable command-line interface for [TypeSafe AI](https://docs.typesafe.ai/introduction) System One evaluations (`noul`, `choice`, `score`, `box`, `evaluate`) and model inspection (`models`). Designed for human developers, shell scripts, CI/CD automation, and command-line AI agents.

## Installation

Requires the .NET 11 runtime (or publish as a standalone Native AOT binary).

```bash
dotnet tool install -g TypeSafe.Cli
```

The tool command is `typesafe`.

## Configuration

| Setting | CLI Option | Environment Variable | Default |
|---|---|---|---|
| API Key | `-a`, `--api-key` | `TYPESAFE_API_KEY` | required |
| Base URL | `--base-url` | `TYPESAFE_BASE_URL` | `https://api.typesafe.ai` |
| Default Model | `-m`, `--model` | `TYPESAFE_DEFAULT_MODEL` | `jev-latest` |
| JSON Output | `--json` | — | `false` |
| Verbose Output | `-v`, `--verbose` | — | `false` |

## Commands & Usage

### 1. Noul (`typesafe noul`)
Evaluates a binary yes/no statement and returns a calibrated probability (0–1).

```bash
# Inline text
typesafe noul "The user requested a password reset" "Is this an authentication request?"

# With true/false criteria
typesafe noul "Error 500 on checkout" "Is this outage critical?" --true "Payment gateway is down" --false "Transient network hiccup"

# Reading from file using @filepath
typesafe noul @log.txt "Does this log show a critical security incident?"

# Piped from stdin
cat ticket.txt | typesafe noul - "Is this an urgent inquiry?"
```

### 2. Choice (`typesafe choice`)
Picks the most suitable label from an unordered set of options.

```bash
typesafe choice "Database connection timed out during checkout" "Classify the root cause" \
  -c auth="Authentication failure" \
  -c db="Database connectivity or pool exhaustion" \
  -c network="Network timeout or latency"
```

### 3. Score (`typesafe score`)
Places the state on an ordered rubric (at least 2 levels from lowest to highest).

```bash
typesafe score "Customer: The system went completely down during Black Friday sale" "Assess sentiment urgency" \
  -l "Low priority" \
  -l "Medium concern" \
  -l "High urgency" \
  -l "Critical emergency"
```

### 4. Box (`typesafe box`) — experimental
Locates something in an image and returns a bounding box whose coordinates are fractions (0–1) of the image
dimensions. Box questions require `jev-preview`, which is used automatically unless `--model` or
`TYPESAFE_DEFAULT_MODEL` is set. Box confidence may be `unknown` (`null` in JSON).

```bash
typesafe box @photo.jpg "Locate the dog"
typesafe box @photo.jpg "Locate the dog" --json | jq '.answers.result.box'
```

### Images (experimental)
Every evaluation command (`noul`, `choice`, `score`, `box`, `evaluate`) accepts images:

- **Image state:** an `@filepath` state pointing to a PNG or JPEG file (detected from its content) is sent as an image.
- **Named assets:** `-i`, `--image name=path` (repeatable; `path` alone uses the file name) attaches an image that is
  referenced as `<name>` in the state, instructions, or criteria. Every attached image must be referenced.

```bash
typesafe noul @photo.jpg "Is there a waterfall?"
typesafe choice "Compare <before> and <after>" "What changed?" \
  -i before=before.png -i after=after.png \
  -c none="Nothing changed" -c repaired="Damage was repaired"
```

Image requests are sent as multipart form data with the `TypeSafe-Danger-Zone-Images: true` opt-in header, which the
CLI adds automatically. The API accepts PNG and JPEG only.

### 5. Evaluate (`typesafe evaluate`)
Executes a multi-question evaluation in a single parallel request using a questions JSON definition.

```bash
# Using inline JSON
typesafe evaluate "User login failed 5 times in 1 minute" --questions '{
  "is_attack": { "type": "noul", "instructions": "Is this a brute-force attack?" },
  "action": { "type": "choice", "instructions": "Recommended action", "choices": { "block": "Block IP", "notify": "Notify user" } }
}'

# Using a questions file
typesafe evaluate @request.json --file questions.json

# Mixing a box question with other questions about an image
typesafe evaluate @photo.jpg --questions '{
  "dog": { "type": "box", "instructions": "Locate the dog" },
  "outdoor": { "type": "noul", "instructions": "Was this photo taken outdoors?" }
}'
```

Question types are `noul` (optional `true_criteria`/`false_criteria`), `choice` (`choices`), `score` (`levels`), and `box`.

### 6. Models (`typesafe models`)
Lists available TypeSafe AI models.

```bash
typesafe models
```

## Machine-Readable JSON Mode (`--json`)

When passing `--json`, `typesafe` outputs strictly parseable JSON to `stdout` and redirects diagnostic/progress logs to `stderr`, making it seamless to compose with `jq` or consume in agent tools.
`usage.input_tokens` and `usage.output_tokens` are `null` when the API does not report a count (shown as `n/a` in text mode).

```bash
# Extract the winning choice with jq
typesafe choice @ticket.json "Classify ticket" -c billing="Billing issue" -c tech="Tech support" --json | jq -r '.answers.result.choice'

# Gate CI/CD pipelines on probability threshold
PROB=$(typesafe noul @diff.patch "Does this diff introduce security risks?" --json | jq -r '.answers.result.noul')
```

## Native AOT Compilation

Compile into a self-contained, lightning-fast native binary with zero runtime dependencies:

```bash
dotnet publish src/TypeSafe.Cli/TypeSafe.Cli.csproj -c Release -r win-x64 -o ./publish
```
