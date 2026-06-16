# CodexRoslyn

CodexRoslyn is a local-first Roslyn MCP server for C#/.NET repositories. It gives Codex compact semantic tools for repository overview, solution selection, symbol search, navigation, references, diagnostics, impact analysis, and read-only refactor previews.

The project is distributed as the `dotnet-roslyn-mcp` CLI and as a Codex plugin bundle under `plugin/`.

## Current Capabilities

- Cold repository indexing without loading `MSBuildWorkspace`.
- SQLite-backed solution discovery, index status, syntax declaration search, and document outlines.
- Warm Roslyn semantic queries after a solution is selected or unambiguous.
- Compact `ToolResponse<T>` payloads designed for agent use.
- Stdio MCP serving for plugin use.
- Loopback Streamable HTTP serving for daemon-style use.
- Codex plugin skills and hooks that steer C# work toward semantic tools before broad text search.

No exposed tool mutates source files by default. Refactoring support is preview-only through `cs_refactor_preview`.

## Repository Layout

| Path | Purpose |
| --- | --- |
| `CodexRoslyn.slnx` | Solution entry point. |
| `src/Codex.Roslyn.Abstractions` | Public DTOs and structured tool response contracts. |
| `src/Codex.Roslyn.Index` | Cold scanning, hashing, SQLite storage, FTS search, and syntax declaration indexing. |
| `src/Codex.Roslyn.Workspaces` | `MSBuildWorkspace` registration/loading and warm workspace lifetime. |
| `src/Codex.Roslyn.Core` | Service orchestration, solution discovery/selection, semantic queries, and DI registration. |
| `src/Codex.Roslyn.Mcp` | MCP server instructions, transports, security checks, hosted indexing, and tool methods. |
| `src/Codex.Roslyn.Cli` | `dotnet-roslyn-mcp` CLI entry point. |
| `plugin/` | Codex plugin manifest, MCP config, hooks, and skills. |
| `tests/Codex.Roslyn.Tests` | xUnit tests using temporary repositories. |

## Requirements

- .NET SDK compatible with `global.json`.
- The solution currently targets `net10.0`.
- Central package management is enabled through `Directory.Packages.props`.

## Build And Test

```powershell
dotnet restore CodexRoslyn.slnx
dotnet build CodexRoslyn.slnx
dotnet test CodexRoslyn.slnx
```

Run a narrower test slice with:

```powershell
dotnet test tests/Codex.Roslyn.Tests/Codex.Roslyn.Tests.csproj --filter FullyQualifiedName~<TestClassOrMethod>
```

## CLI Usage

Run from source:

```powershell
dotnet run --project src/Codex.Roslyn.Cli -- <command>
```

Available commands:

```text
serve --stdio
serve --http --port 38777
index --repo <path>
status --repo <path>
doctor --repo <path>
clear-cache --repo <path>
session-context
guard-bash
```

Common local checks:

```powershell
dotnet run --project src/Codex.Roslyn.Cli -- index --repo .
dotnet run --project src/Codex.Roslyn.Cli -- status --repo .
dotnet run --project src/Codex.Roslyn.Cli -- doctor --repo .
```

`doctor` exercises the cold path only. It reports repository root detection, solution discovery, index freshness, SQLite cache status, and intentionally skips warm workspace loading.

## MCP Tools

Cold-path tools answer from scanner/index state and should not load `MSBuildWorkspace`:

- `cs_repo_overview`
- `cs_solution_list`
- `cs_index_status`
- `cs_symbol_search`
- `cs_document_outline`

Warm semantic tools load Roslyn only after solution selection or an unambiguous scope:

- `cs_solution_select`
- `cs_symbol_at`
- `cs_find_references`
- `cs_find_implementations`
- `cs_type_hierarchy`
- `cs_callers`
- `cs_diagnostics`
- `cs_change_impact`
- `cs_test_impact`
- `cs_refactor_preview`

Tool methods live in `src/Codex.Roslyn.Mcp/Tools/RepoTools.cs`. Business logic belongs in `Core`, `Index`, or `Workspaces`, not in MCP method bodies.

## Plugin Usage

For the full setup flow, including installing the required `dotnet-roslyn-mcp` .NET tool, see [docs/INSTALLATION.md](docs/INSTALLATION.md).

The plugin manifest is `plugin/.codex-plugin/plugin.json`. The bundled MCP config in `plugin/.mcp.json` starts MCP through the plugin launcher:

```text
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File ./scripts/roslyn-mcp.ps1 serve --stdio
```

On Windows, the launcher installs the global `dotnet-roslyn-mcp` .NET tool if it is missing before it starts MCP.

The plugin also includes:

- `plugin/skills/csharp-semantic-navigation/SKILL.md`
- `plugin/skills/csharp-safe-refactor/SKILL.md`
- `plugin/skills/dotnet-test-impact/SKILL.md`
- `plugin/hooks/hooks.json`
- `plugin/scripts/roslyn-mcp.ps1`

For direct Codex config examples, see `plugin/config/roslyn.config.toml` for stdio/plugin policy and `plugin/config/roslyn.daemon.config.toml` for loopback HTTP daemon usage.

## HTTP Mode

HTTP serving binds to loopback and defaults to:

```powershell
dotnet run --project src/Codex.Roslyn.Cli -- serve --http --port 38777
```

The default endpoint path is `/mcp`. Configure a bearer token with `--token <value>` or `CODEX_ROSLYN_MCP_TOKEN`. When a token is configured, requests must use a `Bearer` authorization header. Host and Origin checks only allow loopback names.

## Cache And Indexes

Indexes are stored outside the repository under the OS-local cache root. On Windows this is typically:

```text
%LOCALAPPDATA%\CodexRoslyn\indexes\{repo_id}\index.db
```

Use `clear-cache --repo <path>` to remove the cache for a repository.

Generated files ending in `.g.cs`, `.generated.cs`, or `.designer.cs` are excluded by default. The scanner also skips build outputs, IDE folders, `node_modules`, package folders, test results, and coverage output.

## Development Notes

- Keep DTOs and tool responses stable and compact.
- Keep repo-relative paths normalized to forward slashes in indexes and responses.
- Do not auto-run restore or build from MCP tool handling; return actionable status instead.
- Update focused xUnit tests when changing scanner, index, semantic query, tool contract, or transport behavior.
- Treat `docs/SPECIFICATION.md` and `docs/RESEARCH.md` as design/reference documents. Verify current behavior against source before documenting it as implemented.
