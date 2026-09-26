# TypeSafe.Mcp

An unofficial [Model Context Protocol](https://modelcontextprotocol.io) server (stdio) that lets any MCP
client (Claude Desktop, VS Code, Rider, Cursor…) ask [TypeSafe AI](https://docs.typesafe.ai/introduction)
System One questions. It is built on the official C# MCP SDK and the community `TypeSafe` .NET SDK.

## Install

Requires the .NET 11 runtime (newer runtimes work thanks to `RollForward=Major`).

```bash
dotnet tool install -g TypeSafe.Mcp
```

The tool command is `typesafe-mcp`.

## Configuration

| Setting | Command line | Environment | Default |
|---|---|---|---|
| API key | `--api-key` | `TypeSafe__ApiKey`, then `TYPESAFE_API_KEY` | required |
| Base URL | `--base-url` | `TypeSafe__BaseUrl`, then `TYPESAFE_BASE_URL` | `https://api.typesafe.ai` |
| Model | `--model` | `TypeSafe__Model`, then `TYPESAFE_DEFAULT_MODEL` | `jev-latest` |

The server starts without a key; tool calls then return an error asking for `--api-key` or `TYPESAFE_API_KEY`.
Prefer the environment variable so the key is not stored in plain client configuration arguments.

## Tools

| Tool | What it does |
|---|---|
| `typesafe_noul` | Probability (0–1) that a yes/no statement about the state is true. |
| `typesafe_choice` | Picks one label from an unordered set (label → description), with a probability per label. |
| `typesafe_score` | Places the state on an ordered rubric (≥ 2 levels, lowest → highest) and returns a weighted score. |
| `typesafe_evaluate` | Any mix of named noul/choice/score questions about the same state in one request. |
| `typesafe_list_models` | Lists the models available to your key. |

`state` is plain text, or a JSON object/array that is sent as structured JSON. Every question tool accepts an
optional `model`. Results are structured JSON with `model`, `answers` (keyed `result` for the single-question
tools) and `usage`.

## Client configuration

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

Claude Desktop (`claude_desktop_config.json`):

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

Without installing the tool, you can run it from source with
`"command": "dotnet", "args": ["run", "--project", "path/to/src/TypeSafe.Mcp"]`.

## Native AOT

Publishing from source produces a Native AOT, self-contained executable that starts fast and needs no .NET runtime:

```bash
dotnet publish src/TypeSafe.Mcp/TypeSafe.Mcp.csproj -c Release -r win-x64 -o ./publish
```

Then set the client's `command` to the published `TypeSafe.Mcp` executable.
