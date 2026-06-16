# Repository Guidelines

## Project Structure

- `CodexRoslyn.slnx` is the solution entry point. The repo targets `net10.0` through `Directory.Build.props` and uses central package management in `Directory.Packages.props`.
- `src/Codex.Roslyn.Abstractions` contains DTOs and MCP/tool response contracts. Keep these stable and compact because they define the public tool payload shape.
- `src/Codex.Roslyn.Index` owns cold repo scanning, hashing, SQLite schema/storage, FTS search, cache paths, and syntax declaration indexing.
- `src/Codex.Roslyn.Workspaces` owns `MSBuildWorkspace` registration/loading and warm workspace lifetime.
- `src/Codex.Roslyn.Core` orchestrates services, solution discovery/selection, symbol search, semantic queries, and DI registration.
- `src/Codex.Roslyn.Mcp` exposes MCP server instructions and tools. Keep tool methods thin; business behavior belongs in Core/Index/Workspaces.
- `src/Codex.Roslyn.Cli` is the `dotnet-roslyn-mcp` tool entry point.
- `plugin/` contains Codex plugin wiring only: `.mcp.json` and skills. Do not put the semantic engine there.
- `tests/Codex.Roslyn.Tests` contains xUnit unit and integration tests using temporary repositories.

## Build, Test, and Run Commands

- Restore: `dotnet restore CodexRoslyn.slnx`
- Build: `dotnet build CodexRoslyn.slnx`
- Test all: `dotnet test CodexRoslyn.slnx`
- Test narrow: `dotnet test tests/Codex.Roslyn.Tests/Codex.Roslyn.Tests.csproj --filter FullyQualifiedName~<TestClassOrMethod>`
- Run CLI from source: `dotnet run --project src/Codex.Roslyn.Cli -- <command>`
- Useful CLI commands:
  - `dotnet run --project src/Codex.Roslyn.Cli -- index --repo .`
  - `dotnet run --project src/Codex.Roslyn.Cli -- status --repo .`
  - `dotnet run --project src/Codex.Roslyn.Cli -- doctor --repo .`
  - `dotnet run --project src/Codex.Roslyn.Cli -- serve --stdio`
  - `dotnet run --project src/Codex.Roslyn.Cli -- session-context`

## Coding Conventions

- Use file-scoped namespaces, nullable-aware code, implicit usings, and sealed classes/records where that matches the surrounding code.
- Keep package versions in `Directory.Packages.props`; project files should use versionless `PackageReference` entries.
- Keep service registration in `ServiceCollectionExtensions.AddCodexRoslynPhaseZero()` when adding phase-zero services.
- MCP tools should return `ToolResponse<T>` and preserve compact, structured outputs: `ResultKind`, `Summary`, `Items`, cache state, token policy, and warnings.
- Normalize repo-relative paths to forward slashes in indexes, DTOs, and tool responses.
- Do not add broad source dumps to tool responses. Prefer compact summaries and explicit `maxItems` limits.

## Architecture Rules

- Cold-path tools must not load `MSBuildWorkspace`. `cs_repo_overview`, `cs_solution_list`, `cs_index_status`, `cs_symbol_search`, and `cs_document_outline` should answer from scanner/index state.
- Warm semantic tools load Roslyn only after solution selection or unambiguous scope. Preserve `cs_solution_select` and ambiguity responses for multi-solution repos.
- `WorkspaceManager` currently keeps a small warm LRU and loads Debug configuration. Dispose stale workspaces when the cold index becomes stale.
- `IndexDatabase` owns schema changes. Update tests around status, search, outline, and semantic persistence when changing schema or query behavior.
- `SPECIFICATION.md` and `RESEARCH.md` describe intended direction, including future HTTP daemon/refactor phases. Do not present future-mode behavior as implemented.

## Testing Guidelines

- Add or update focused xUnit tests in `tests/Codex.Roslyn.Tests` for changed behavior.
- For cold index changes, cover scanner/index/search/outline behavior without relying on real workspace load.
- For semantic changes, use temporary sample repos and restore/build only as narrowly as needed; see `SemanticWorkspaceIntegrationTests`.
- Keep generated-file and excluded-directory behavior covered when changing scanning logic.
- Run at least `dotnet build CodexRoslyn.slnx` plus the relevant test filter before claiming code changes are complete.

## Generated Code, Indexes, and Caches

- The scanner excludes `bin`, `obj`, `.git`, IDE folders, `node_modules`, `packages`, `TestResults`, and `coverage`.
- Generated C# files ending in `.g.cs`, `.generated.cs`, or `.designer.cs` are excluded by default unless `includeGenerated` is explicitly supported for the path.
- Default index storage is outside the repo under the OS cache root, for example `%LOCALAPPDATA%\CodexRoslyn\indexes\{repo_id}\index.db` on Windows. Tests should override `IndexPathProvider` to a temp path.
- Do not commit local cache files, test results, build output, or `.codex/environments/`.

## Plugin and Distribution Notes

- `plugin/.mcp.json` expects an installed `dotnet-roslyn-mcp` command running `serve --stdio`.
- The current CLI supports both `serve --stdio` and loopback `serve --http`; bundled plugin wiring still uses stdio by default.
- Keep plugin skills concise and workflow-oriented; the server instructions in `Codex.Roslyn.Mcp/Instructions.cs` should remain aligned with exposed tools.

## Security and Configuration

- The tool is local-first: no source upload, no remote telemetry by default, and no generic arbitrary-file MCP read tool.
- Restore/build/doctor suggestions may be returned, but code should not auto-run restore/build as part of MCP tool handling.
- If HTTP serving is added later, bind to loopback by default and validate local access before exposing tools.
