# Specification: Codex-integrated .NET/C# semantic tooling

## 1. Product decision

Build this as a **Codex plugin that bundles a local Roslyn MCP server**, with direct CLI/IDE support through the same MCP server.

The plugin is the app-facing distribution unit. The MCP server is the durable technical core. Skills and hooks are used to steer Codex toward the semantic tools instead of raw file reads or shell-based grep. Codex supports MCP servers over STDIO and Streamable HTTP, supports MCP server instructions, and lets MCP servers be configured through `config.toml`; Codex plugins can bundle skills, hooks, apps, and `.mcp.json` server definitions. ([OpenAI Developers][1])

```text
Codex app / Codex CLI / Codex IDE extension
        |
        | MCP tool calls
        v
dotnet-roslyn-mcp
        |
        | local service calls
        v
Roslyn semantic engine
        |
        | lazy workspaces + persistent index
        v
Repo root with one or more .sln files
```

The default transport should be **STDIO** because it is simple, local, and fits Codex’s local-server model. Add **localhost Streamable HTTP daemon mode** after MVP for warm, persistent Roslyn caches across sessions. MCP itself defines both STDIO and Streamable HTTP transports; STDIO uses newline-delimited JSON-RPC over stdin/stdout, while Streamable HTTP runs as an independent server endpoint and requires proper local security controls such as localhost binding and origin validation. ([Model Context Protocol][2])

---

## 2. Technology stack

### Runtime and language

Use:

```text
Language:          C# 14 where available; otherwise latest supported by .NET SDK
Primary runtime:   .NET 10 LTS
Fallback target:   net8.0 only if enterprise compatibility requires it
Build system:      SDK-style .csproj
Packaging:         dotnet tool + Codex plugin bundle
```

.NET 10 is the current LTS line and is active through November 2028; that makes it the right default target for a new tool. ([Microsoft][3])

### Core packages

Use these packages as the initial dependency set:

```xml
<ItemGroup>
  <PackageReference Include="ModelContextProtocol" Version="1.4.0" />
  <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.4.0" />

  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.3.0" />
  <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="5.3.0" />
  <PackageReference Include="Microsoft.Build.Locator" Version="1.11.2" />

  <PackageReference Include="Microsoft.Data.Sqlite" Version="*" />
  <PackageReference Include="System.CommandLine" Version="*" />

  <PackageReference Include="Microsoft.Extensions.Hosting" Version="*" />
  <PackageReference Include="Microsoft.Extensions.Logging" Version="*" />
  <PackageReference Include="OpenTelemetry" Version="*" />
  <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="*" />

  <PackageReference Include="BenchmarkDotNet" Version="*" PrivateAssets="all" />
</ItemGroup>
```

The Roslyn package versions above reflect the current Microsoft-published Roslyn packages found during research: `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, and `Microsoft.CodeAnalysis.Workspaces.MSBuild` at version `5.3.0`. ([nuget.org][4])

Use the official C# MCP SDK packages rather than hand-rolling JSON-RPC protocol code. The MCP C# SDK exposes core MCP server/client functionality and an ASP.NET Core package for HTTP hosting. ([nuget.org][5])

### Roslyn APIs to use

Use Roslyn Workspaces as the semantic source of truth:

```text
MSBuildWorkspace
Solution
Project
Document
Compilation
SemanticModel
SymbolFinder
Renamer
Formatter
Simplifier
DocumentEditor
SyntaxEditor
```

Microsoft’s Workspace API is explicitly designed for whole-solution analysis and gives access to source text, syntax trees, semantic models, and compilations without manually managing parse options, project references, or dependencies. `SemanticModel` is the correct API for type, symbol, binding, data-flow, control-flow, and diagnostic queries, but it caches semantic data and can retain memory, so the implementation must use strict lifetime and LRU disposal rules. ([Microsoft Learn][6])

For cross-file relationship queries, use `SymbolFinder` for references, implementations, derived types, overrides, declarations, and callers. ([Microsoft Learn][7])

### Storage and indexing

Use:

```text
Persistent store:       SQLite
SQLite provider:        Microsoft.Data.Sqlite
Journal mode:           WAL
Full-text search:       SQLite FTS5
In-process cache:       MemoryCache + custom LRU
File watching:          FileSystemWatcher + periodic verification scan
Work queue:             System.Threading.Channels
Serialization:          System.Text.Json source generation
```

SQLite WAL mode is appropriate because it improves concurrency for readers and writers and is generally faster in common cases; FTS5 is appropriate for fast full-text lookup over symbols, paths, namespaces, and compact outlines. ([sqlite.org][8])

`FileSystemWatcher` should be treated as an optimization, not as the only correctness mechanism. It can monitor directories recursively, but buffer overflow can cause missed events; the indexer must debounce events and fall back to a verification scan when overflow or uncertainty occurs. ([Microsoft Learn][9])

---

## 3. Repository layout

Use this project structure:

```text
codex-roslyn/
  src/
    Codex.Roslyn.Abstractions/
      ToolContracts/
      FeatureFlags/
      StableIds/
      Dtos/

    Codex.Roslyn.Core/
      WorkspaceManager.cs
      SolutionSelector.cs
      SemanticQueryService.cs
      SymbolIdentityService.cs
      RefactoringService.cs
      DiagnosticsService.cs
      ImpactAnalysisService.cs
      TestImpactService.cs

    Codex.Roslyn.Index/
      IndexDatabase.cs
      IndexSchema.sql
      RepoScanner.cs
      FileHasher.cs
      SyntaxIndexer.cs
      SemanticEdgeIndexer.cs
      ChangeJournal.cs
      FtsSearchService.cs

    Codex.Roslyn.Mcp/
      Program.cs
      McpServerFactory.cs
      Tools/
        RepoTools.cs
        SymbolTools.cs
        RelationshipTools.cs
        DiagnosticTools.cs
        ImpactTools.cs
        RefactorTools.cs
      Instructions.cs

    Codex.Roslyn.Http/
      Program.cs
      McpHttpEndpoint.cs
      DaemonLifetime.cs

    Codex.Roslyn.Cli/
      Program.cs
      Commands/
        serve.cs
        index.cs
        status.cs
        doctor.cs
        session-context.cs

    Codex.Roslyn.Tests/
    Codex.Roslyn.IntegrationTests/
    Codex.Roslyn.Benchmarks/

  plugin/
    .codex-plugin/
      plugin.json
    .mcp.json
    skills/
      csharp-semantic-navigation/
        SKILL.md
      csharp-safe-refactor/
        SKILL.md
      dotnet-test-impact/
        SKILL.md
    hooks/
      hooks.json
    config/
      defaults.json

  samples/
    multi-sln-repo/
      TralaliProject/
        project.sln
      SolutionProject/
        project.sln

  docs/
    architecture.md
    tool-contracts.md
    indexing.md
    codex-integration.md
```

The plugin directory should remain thin. It should not contain the semantic engine. It should declare the plugin metadata, skills, hooks, and MCP server wiring. The executable should be distributed as a `dotnet tool`, native package, or bundled binary.

---

## 4. Multi-solution support

The repository model must assume multiple independent `.sln` files under one repo root.

Example:

```text
repo-root/
  TralaliProject/
    project.sln
    src/
      Tralali.Api/
      Tralali.Core/

  SolutionProject/
    project.sln
    src/
      SolutionProject.Web/
      SolutionProject.Domain/

  shared/
    SharedKernel/
      SharedKernel.csproj
```

### Core concepts

Use these identities:

```text
repo_id
solution_id
project_id
target_framework_id
file_id
document_id
symbol_id
```

Definitions:

```text
repo_id
  Stable hash of normalized repo root path + git remote URL when available.

solution_id
  Stable hash of repo_id + relative .sln path.

project_id
  Stable hash of solution_id + project relative path + project GUID/name.

target_framework_id
  project_id + TFM + configuration + runtime identifier where relevant.

file_id
  Stable hash of repo_id + normalized relative file path.

document_id
  solution_id + project_id + target_framework_id + file_id.

symbol_id
  Stable semantic identity produced by SymbolIdentityService.
```

The same physical file can appear in multiple solutions, projects, or target frameworks. Therefore:

```text
file_id != document_id
```

A single `shared/Foo.cs` file may have:

```text
file_id:       file:shared/Foo.cs
document_id 1: TralaliProject/project.sln + SharedKernel.csproj + net10.0
document_id 2: SolutionProject/project.sln + SharedKernel.csproj + net10.0
```

### Solution selection algorithm

Every semantic tool accepts optional scope:

```json
{
  "scope": {
    "repoRoot": "/repo",
    "solutionId": "optional",
    "projectId": "optional",
    "targetFramework": "optional",
    "file": "optional"
  }
}
```

Selection order:

```text
1. Explicit solutionId wins.
2. Explicit projectId narrows within the selected solution.
3. If a file path is supplied, choose the solution with the longest common directory prefix.
4. If multiple solutions include the same file, return an ambiguity response.
5. If no scope is supplied, use the last active solution for this Codex session.
6. If no active solution exists, select the nearest .sln to the changed/open file.
7. If still ambiguous, call cs_solution_list and ask Codex to pick one before semantic loading.
```

Ambiguity response:

```json
{
  "resultKind": "ambiguous_solution",
  "message": "File is included by multiple solutions.",
  "solutions": [
    {
      "solutionId": "sln_tralali",
      "path": "TralaliProject/project.sln",
      "reason": "Longest prefix match"
    },
    {
      "solutionId": "sln_solutionproject",
      "path": "SolutionProject/project.sln",
      "reason": "Shared project reference"
    }
  ],
  "recommendedNextTool": "cs_solution_select"
}
```

### Required multi-solution tools

```text
cs_solution_list
cs_solution_select
cs_solution_status
cs_repo_overview
```

`cs_repo_overview` must not load all solutions. It reads the persistent index, reports discovered solutions, reports index freshness, and recommends the likely active solution.

---

## 5. Startup and indexing model

The central performance rule:

```text
Never load MSBuildWorkspace on server startup.
Never load every solution unless explicitly requested.
Use the persistent syntax index first.
Load Roslyn semantic workspace lazily.
Cache semantic results incrementally.
```

### Two-tier index

Use two index tiers.

```text
Tier 1: cold syntax index
  Always available quickly.
  Built from files and C# syntax trees.
  Does not require MSBuildWorkspace.
  Supports repo overview, file search, symbol-name search, document outlines, namespaces, class/member declarations.

Tier 2: warm semantic index
  Built only after selecting a solution/project/file.
  Uses MSBuildWorkspace, Compilation, SemanticModel, and SymbolFinder.
  Supports true symbol resolution, references, implementations, diagnostics, type hierarchy, callers/callees, refactoring previews.
```

### Startup sequence

On MCP server start:

```text
1. Resolve repo root from cwd or explicit --repo.
2. Open SQLite index for repo_id.
3. Check index manifest.
4. Return server-ready immediately.
5. Schedule lightweight validation:
   - scan for .sln files
   - scan changed .cs/.csproj/props/targets/global.json/NuGet.Config files
   - update syntax index for changed files
6. Do not call MSBuildWorkspace.OpenSolutionAsync yet.
```

Target behavior:

```text
Server ready with valid index:       p90 <= 700 ms
cs_repo_overview from warm SQLite:   p90 <= 100 ms
cs_symbol_search from FTS:           p90 <= 75 ms
First semantic workspace load:       async/lazy, never blocks server startup
```

These are product SLOs, not guaranteed constants. Real first semantic load time depends on repo size, NuGet restore state, SDK availability, project count, analyzers, and target frameworks.

### Index invalidation inputs

Invalidate or partially rebuild when any of these change:

```text
*.cs
*.csproj
*.fsproj only for project graph awareness, not semantic C# analysis
*.vbproj only for project graph awareness, not semantic C# analysis
*.sln
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
global.json
NuGet.Config
packages.lock.json
obj/project.assets.json
.editorconfig
.ruleset
*.editorconfig
```

Do not index these by default:

```text
**/bin/**
**/obj/**
**/.git/**
**/.vs/**
**/.idea/**
**/.vscode/**
**/node_modules/**
**/packages/**
**/TestResults/**
**/coverage/**
**/*.g.cs
**/*.generated.cs
**/*.Designer.cs
```

Generated code can be enabled by feature flag.

### Persistent cache location

Use OS-appropriate cache paths:

```text
Windows:
  %LOCALAPPDATA%\CodexRoslyn\indexes\{repo_id}\index.db

macOS:
  ~/Library/Caches/CodexRoslyn/indexes/{repo_id}/index.db

Linux:
  ${XDG_CACHE_HOME:-~/.cache}/codex-roslyn/indexes/{repo_id}/index.db
```

Also write:

```text
manifest.json
lock
logs/
traces/
```

### File change pipeline

Use this pipeline:

```text
FileSystemWatcher
  -> debounce queue
  -> change coalescer
  -> hash verifier
  -> syntax reindex
  -> semantic invalidation marker
  -> optional semantic refresh for active solution
```

Implementation rules:

```text
Debounce window:        250-500 ms
Batch max size:         512 file events
Overflow behavior:      mark repo dirty, run verification scan
Hash strategy:          mtime + size fast path; SHA-256 or xxHash fallback
Correctness scan:       periodic or on query when dirty
```

---

## 6. SQLite schema

Use this as the initial schema. Keep semantic graph tables sparse and incremental.

```sql
create table repo (
  repo_id text primary key,
  root_path text not null,
  git_head text null,
  git_remote_hash text null,
  created_utc text not null,
  updated_utc text not null,
  schema_version integer not null
);

create table solution (
  solution_id text primary key,
  repo_id text not null,
  relative_path text not null,
  display_name text not null,
  file_hash text null,
  last_seen_utc text not null,
  is_default integer not null default 0
);

create table project (
  project_id text primary key,
  solution_id text not null,
  relative_path text not null,
  name text not null,
  assembly_name text null,
  language text not null,
  output_kind text null,
  last_seen_utc text not null
);

create table project_target (
  target_id text primary key,
  project_id text not null,
  target_framework text not null,
  configuration text not null default 'Debug',
  runtime_identifier text null
);

create table file (
  file_id text primary key,
  repo_id text not null,
  relative_path text not null,
  extension text not null,
  size_bytes integer not null,
  mtime_utc text not null,
  content_hash text not null,
  is_generated integer not null default 0,
  last_indexed_utc text not null
);

create table document (
  document_id text primary key,
  solution_id text not null,
  project_id text not null,
  target_id text null,
  file_id text not null,
  logical_path text null
);

create table declaration (
  declaration_id text primary key,
  file_id text not null,
  document_id text null,
  solution_id text null,
  project_id text null,
  declared_symbol_id text null,
  kind text not null,
  name text not null,
  namespace text null,
  containing_type text null,
  accessibility text null,
  modifiers text null,
  arity integer null,
  signature_short text null,
  signature_hash text null,
  start_line integer not null,
  start_col integer not null,
  end_line integer not null,
  end_col integer not null,
  syntax_hash text not null,
  semantic_state text not null default 'syntax_only'
);

create table symbol (
  symbol_id text primary key,
  solution_id text null,
  project_id text null,
  target_id text null,
  kind text not null,
  name text not null,
  full_name text not null,
  metadata_name text null,
  doc_comment_id text null,
  assembly_name text null,
  namespace text null,
  containing_type text null,
  accessibility text null,
  signature_short text null,
  signature_hash text null,
  source_file_id text null,
  source_start_line integer null,
  source_start_col integer null,
  is_external integer not null default 0,
  confidence text not null default 'semantic'
);

create table reference_edge (
  edge_id text primary key,
  solution_id text not null,
  project_id text null,
  from_file_id text not null,
  from_symbol_id text null,
  to_symbol_id text not null,
  reference_kind text not null,
  start_line integer not null,
  start_col integer not null,
  end_line integer not null,
  end_col integer not null,
  confidence text not null,
  computed_utc text not null
);

create table call_edge (
  edge_id text primary key,
  solution_id text not null,
  caller_symbol_id text not null,
  callee_symbol_id text not null,
  call_kind text not null,
  file_id text not null,
  start_line integer not null,
  start_col integer not null,
  confidence text not null,
  computed_utc text not null
);

create table inheritance_edge (
  edge_id text primary key,
  solution_id text not null,
  derived_symbol_id text not null,
  base_symbol_id text not null,
  relation_kind text not null,
  confidence text not null,
  computed_utc text not null
);

create table diagnostic (
  diagnostic_id text primary key,
  solution_id text not null,
  project_id text null,
  file_id text null,
  roslyn_id text not null,
  severity text not null,
  message text not null,
  start_line integer null,
  start_col integer null,
  end_line integer null,
  end_col integer null,
  computed_utc text not null
);

create virtual table symbol_fts using fts5(
  name,
  full_name,
  namespace,
  containing_type,
  signature_short,
  content='symbol',
  content_rowid='rowid'
);

create virtual table file_fts using fts5(
  relative_path,
  outline,
  namespaces,
  type_names
);
```

### Index correctness states

Every indexed item must carry a confidence/state:

```text
syntax_only
  Produced from C# syntax parsing without Roslyn compilation.

semantic
  Produced from Roslyn workspace, compilation, semantic model, or SymbolFinder.

stale
  File/project changed since the item was computed.

partial
  Result is valid but truncated by maxItems/maxDepth/maxTokens.

ambiguous
  More than one solution/project/TFM could apply.
```

---

## 7. Symbol identity strategy

Roslyn symbols are not enough as external API identifiers. The MCP server must expose stable IDs that survive process restarts and can be resolved back to Roslyn symbols when a workspace is loaded.

### Symbol ID format

Use:

```text
csid:v1:{repo_id}:{solution_scope}:{assembly}:{kind}:{metadata_name}:{signature_hash}
```

Examples:

```text
csid:v1:r_abc:sln_tralali:Tralali.Core:type:Tralali.Core.CustomerService:9f21a6
csid:v1:r_abc:sln_tralali:Tralali.Core:method:Tralali.Core.CustomerService.GetAsync:1c3e7b
```

### Symbol identity inputs

For top-level and member symbols, use:

```text
Roslyn symbol kind
assembly name
namespace
metadata name
containing type
arity
parameter type list
return type where relevant
nullable annotation where relevant
generic constraints hash
DocumentationCommentId where available
source file + span fallback
```

For locals, lambdas, anonymous types, and generated compiler artifacts:

```text
csid:v1:{repo}:{solution}:{file_id}:local:{span_hash}:{syntax_hash}
```

### Resolution algorithm

```text
1. Try exact symbol_id lookup in semantic symbol table.
2. If workspace is warm, resolve by doc_comment_id + assembly + metadata name.
3. If not found, resolve by source file + span + signature hash.
4. If still not found, return stale_symbol with suggested cs_symbol_search query.
```

---

## 8. Workspace lifecycle

### Workspace loading

Use `MSBuildLocator` before any MSBuildWorkspace usage. Then load only the selected solution:

```csharp
MSBuildLocator.RegisterDefaults();

using var workspace = MSBuildWorkspace.Create(properties);
var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken);
```

Store a `WorkspaceHandle` per loaded solution/configuration/TFM set:

```text
WorkspaceHandle
  solution_id
  solution_path
  configuration
  target_framework_filter
  loaded_utc
  last_access_utc
  project_count
  document_count
  memory_estimate
  state: loading | ready | faulted | disposed
```

### Cache policy

Default:

```text
Max warm workspaces:          2
Max idle age:                 20 minutes
Max semantic model age:       request-scoped by default
Max compilation cache age:    workspace-scoped
Eviction policy:              LRU + memory pressure
```

Do not retain arbitrary `SemanticModel` objects beyond a request unless a specific benchmark proves the benefit. Microsoft documents that semantic models cache information and can keep significant memory alive. ([Microsoft Learn][10])

### Loading modes

```text
cold
  SQLite + syntax index only.

warm_solution
  MSBuildWorkspace loaded for one selected solution.

warm_project
  Specific project compilation prepared.

hot_document
  Specific document syntax root and semantic model available for a single request.
```

### Restore/build assumptions

The tool must not run `dotnet restore` automatically by default. It should detect restore state and return actionable status:

```json
{
  "state": "workspace_load_failed",
  "reason": "missing_assets_file",
  "project": "src/App/App.csproj",
  "suggestedCommand": "dotnet restore TralaliProject/project.sln",
  "safeToRunAutomatically": false
}
```

A later opt-in flag can allow restore:

```json
{
  "features": {
    "workspace.autoRestore": false
  }
}
```

---

## 9. MCP tool contract

MCP servers expose tools with names, descriptions, input schemas, output schemas, and annotations, and tools are model-invokable. Write-capable tools must preserve human approval and preview semantics. ([Model Context Protocol][11])

### Universal request fields

Every tool should accept:

```json
{
  "scope": {
    "repoRoot": "optional absolute path",
    "solutionId": "optional",
    "projectId": "optional",
    "targetFramework": "optional",
    "file": "optional relative path"
  },
  "detailLevel": "tiny | normal | full",
  "maxItems": 50,
  "maxDepth": 2,
  "includeSource": false,
  "includeGenerated": false,
  "cursor": "optional"
}
```

### Universal response fields

Every tool should return:

```json
{
  "resultKind": "ok | partial | ambiguous_solution | stale_index | error",
  "summary": "One compact sentence.",
  "items": [],
  "nextCursor": null,
  "cacheStatus": {
    "index": "hit | miss | stale",
    "workspace": "cold | loading | warm | faulted"
  },
  "tokenPolicy": {
    "detailLevel": "normal",
    "estimatedTokens": 780,
    "truncated": false
  },
  "warnings": []
}
```

### Default enabled MCP tools

These are the default tools because they cover the highest-frequency 90th percentile of useful C# semantic tasks without excessive latency or token cost.

| Tool                      | Default | Purpose                                              | Backing API                             |
| ------------------------- | ------: | ---------------------------------------------------- | --------------------------------------- |
| `cs_repo_overview`        |     Yes | Summarize repo, solutions, projects, index freshness | SQLite index                            |
| `cs_solution_list`        |     Yes | List `.sln` files and selection status               | SQLite index                            |
| `cs_solution_select`      |     Yes | Set active solution for session                      | Tool state                              |
| `cs_index_status`         |     Yes | Report cold/warm index state                         | SQLite + workspace manager              |
| `cs_symbol_search`        |     Yes | Fast symbol lookup by name/signature                 | SQLite FTS5, then Roslyn if warm        |
| `cs_document_outline`     |     Yes | Compact file outline                                 | Syntax index, Roslyn optional           |
| `cs_symbol_at`            |     Yes | Go-to-definition / identify symbol at position       | `SemanticModel`                         |
| `cs_find_references`      |     Yes | True semantic references                             | `SymbolFinder.FindReferencesAsync`      |
| `cs_find_implementations` |     Yes | Interface/abstract implementation lookup             | `SymbolFinder.FindImplementationsAsync` |
| `cs_type_hierarchy`       |     Yes | Base/derived/interface hierarchy                     | `SymbolFinder` + semantic model         |
| `cs_callers`              |     Yes | One-hop caller lookup                                | `SymbolFinder.FindCallersAsync`         |
| `cs_diagnostics`          |     Yes | Compiler diagnostics for selected scope              | `Compilation`, `SemanticModel`          |
| `cs_change_impact`        |     Yes | Changed symbols/files and impacted areas             | Semantic graph + git diff               |
| `cs_test_impact`          |     Yes | Likely affected tests                                | Naming heuristics + reference graph     |
| `cs_refactor_preview`     |     Yes | Rename/move/extract preview, no write                | Roslyn refactoring APIs                 |

### Disabled by default

These should exist but be opt-in:

| Tool                       | Default | Reason                                             |
| -------------------------- | ------: | -------------------------------------------------- |
| `cs_apply_workspace_edit`  |      No | Mutates files; require explicit approval           |
| `cs_full_call_graph`       |      No | Expensive and can produce huge output              |
| `cs_data_flow`             |      No | Useful but niche; high semantic cost               |
| `cs_control_flow`          |      No | Useful but niche; high semantic cost               |
| `cs_operation_tree`        |      No | Useful for analyzers; noisy for normal Codex tasks |
| `cs_run_analyzers`         |      No | Potentially slow; analyzer behavior varies by repo |
| `cs_code_fix_preview`      |      No | Useful but broad; enable after MVP                 |
| `cs_public_api_diff`       |      No | Valuable for libraries, not universal              |
| `cs_dead_code_candidates`  |      No | False positives without whole-program assumptions  |
| `cs_generated_code_search` |      No | High noise                                         |
| `cs_load_all_solutions`    |      No | Violates startup requirement                       |

### Tool examples

#### `cs_symbol_search`

```json
{
  "query": "CustomerService GetAsync",
  "kind": "method",
  "scope": {
    "solutionId": "sln_tralali"
  },
  "maxItems": 10,
  "detailLevel": "tiny"
}
```

Response:

```json
{
  "resultKind": "ok",
  "summary": "Found 2 candidate methods.",
  "items": [
    {
      "symbolId": "csid:v1:r_abc:sln_tralali:Tralali.Core:method:Tralali.Core.CustomerService.GetAsync:1c3e7b",
      "kind": "method",
      "displayName": "CustomerService.GetAsync(int customerId)",
      "file": "TralaliProject/src/Tralali.Core/CustomerService.cs",
      "line": 42,
      "confidence": "syntax_only"
    }
  ],
  "cacheStatus": {
    "index": "hit",
    "workspace": "cold"
  }
}
```

#### `cs_symbol_at`

```json
{
  "file": "TralaliProject/src/Tralali.Core/CustomerService.cs",
  "line": 42,
  "column": 23,
  "scope": {
    "solutionId": "sln_tralali"
  },
  "detailLevel": "normal"
}
```

Response:

```json
{
  "resultKind": "ok",
  "summary": "Symbol is method CustomerService.GetAsync(int).",
  "item": {
    "symbolId": "csid:v1:r_abc:sln_tralali:Tralali.Core:method:Tralali.Core.CustomerService.GetAsync:1c3e7b",
    "kind": "method",
    "displayName": "Tralali.Core.CustomerService.GetAsync(int customerId)",
    "declaredAccessibility": "public",
    "returnType": "Task<Customer>",
    "definition": {
      "file": "TralaliProject/src/Tralali.Core/CustomerService.cs",
      "startLine": 42,
      "startColumn": 17
    }
  },
  "cacheStatus": {
    "index": "hit",
    "workspace": "warm"
  }
}
```

#### `cs_refactor_preview`

```json
{
  "operation": "rename",
  "symbolId": "csid:v1:r_abc:sln_tralali:Tralali.Core:method:Tralali.Core.CustomerService.GetAsync:1c3e7b",
  "newName": "GetCustomerAsync",
  "scope": {
    "solutionId": "sln_tralali"
  },
  "detailLevel": "normal"
}
```

Response:

```json
{
  "resultKind": "ok",
  "summary": "Rename would update 18 references across 7 files.",
  "editId": "edit_20260616_001",
  "changes": [
    {
      "file": "TralaliProject/src/Tralali.Core/CustomerService.cs",
      "edits": 1
    },
    {
      "file": "TralaliProject/tests/CustomerServiceTests.cs",
      "edits": 4
    }
  ],
  "diffPreview": "@@ compact unified diff here @@",
  "requiresApproval": true
}
```

Write flow:

```text
cs_refactor_preview -> Codex reviews diff -> user approval -> cs_apply_workspace_edit
```

`cs_apply_workspace_edit` must never be enabled silently.

---

## 10. Feature flags

Create a single config model:

```json
{
  "version": 1,
  "index": {
    "enabled": true,
    "syntaxDeclarations": true,
    "semanticEdges": "onDemand",
    "watchFiles": true,
    "includeGenerated": false,
    "loadAllSolutionsOnStartup": false,
    "excludeGlobs": [
      "**/bin/**",
      "**/obj/**",
      "**/.git/**",
      "**/.vs/**",
      "**/node_modules/**",
      "**/*.g.cs",
      "**/*.generated.cs",
      "**/*.Designer.cs"
    ]
  },
  "workspace": {
    "lazyLoad": true,
    "maxWarmSolutions": 2,
    "autoRestore": false,
    "configuration": "Debug",
    "targetFramework": null
  },
  "features": {
    "repoOverview": true,
    "symbolSearch": true,
    "documentOutline": true,
    "symbolAt": true,
    "findReferences": true,
    "findImplementations": true,
    "typeHierarchy": true,
    "callersOneHop": true,
    "diagnostics": true,
    "changeImpact": true,
    "testImpact": true,
    "refactorPreview": true,

    "applyWorkspaceEdit": false,
    "fullCallGraph": false,
    "dataFlow": false,
    "controlFlow": false,
    "operationTree": false,
    "runAnalyzers": false,
    "codeFixPreview": false,
    "publicApiDiff": false,
    "deadCodeCandidates": false,
    "generatedCode": false
  },
  "limits": {
    "defaultMaxItems": 50,
    "defaultMaxDepth": 2,
    "defaultDetailLevel": "normal",
    "maxReturnedSourceLines": 80,
    "maxToolResponseTokens": 4000,
    "maxFullResponseTokens": 12000
  }
}
```

### Default profile: `p90`

The default profile is the high-value, low-latency profile:

```text
Profile name: p90
Enabled:
  repo overview
  solution list/select
  index status
  symbol search
  document outline
  symbol at position
  find references
  find implementations
  type hierarchy
  one-hop callers
  diagnostics
  change impact
  test impact
  refactor preview

Disabled:
  direct mutation
  full call graph
  full analyzer execution
  data/control flow
  operation tree
  generated code
  all-solution preloading
```

Add optional profiles:

```text
profile: minimal
  Only repo overview, solution list, symbol search, document outline.

profile: refactor
  p90 + applyWorkspaceEdit prompted + codeFixPreview.

profile: analyzer
  p90 + runAnalyzers + operationTree + dataFlow + controlFlow.

profile: monorepo
  p90 + stricter result limits + HTTP daemon recommended.

profile: full
  Everything enabled, but write tools still require approval.
```

---

## 11. Codex plugin specification

### Plugin manifest

```json
{
  "name": "dotnet-semantic-tools",
  "version": "0.1.0",
  "description": "Roslyn semantic navigation, diagnostics, impact analysis, and safe refactor previews for C#/.NET repositories.",
  "skills": "./skills/",
  "mcpServers": "./.mcp.json",
  "hooks": "./hooks/hooks.json",
  "interface": {
    "defaultPrompt": [
      "Use Roslyn semantic tools for C#/.NET navigation, references, diagnostics, impact analysis, and refactor planning before raw text search.",
      "Prefer compact semantic summaries first. Request source only when an edit requires it."
    ]
  }
}
```

Codex plugins require `.codex-plugin/plugin.json` and can point to `skills`, `hooks`, apps, and `.mcp.json` MCP server definitions. ([OpenAI Developers][12])

### `.mcp.json`

```json
{
  "mcp_servers": {
    "roslyn": {
      "command": "dotnet-roslyn-mcp",
      "args": ["serve", "--stdio"]
    }
  }
}
```

### Direct Codex config

For direct CLI/IDE use:

```toml
[mcp_servers.roslyn]
command = "dotnet-roslyn-mcp"
args = ["serve", "--stdio"]
startup_timeout_sec = 3
tool_timeout_sec = 20
enabled_tools = [
  "cs_repo_overview",
  "cs_solution_list",
  "cs_solution_select",
  "cs_index_status",
  "cs_symbol_search",
  "cs_document_outline",
  "cs_symbol_at",
  "cs_find_references",
  "cs_find_implementations",
  "cs_type_hierarchy",
  "cs_callers",
  "cs_diagnostics",
  "cs_change_impact",
  "cs_test_impact",
  "cs_refactor_preview"
]
```

For daemon mode:

```toml
[mcp_servers.roslyn]
url = "http://127.0.0.1:38777/mcp"
startup_timeout_sec = 1
tool_timeout_sec = 20
enabled_tools = [
  "cs_repo_overview",
  "cs_symbol_search",
  "cs_symbol_at",
  "cs_find_references",
  "cs_diagnostics",
  "cs_refactor_preview"
]
```

Codex MCP configuration supports startup timeout, tool timeout, enabling/disabling servers, and allow/deny lists for individual tools. ([OpenAI Developers][1])

### Plugin-scoped MCP policy

```toml
[plugins."dotnet-semantic-tools".mcp_servers.roslyn]
enabled = true
default_tools_approval_mode = "approve"
enabled_tools = [
  "cs_repo_overview",
  "cs_solution_list",
  "cs_solution_select",
  "cs_index_status",
  "cs_symbol_search",
  "cs_document_outline",
  "cs_symbol_at",
  "cs_find_references",
  "cs_find_implementations",
  "cs_type_hierarchy",
  "cs_callers",
  "cs_diagnostics",
  "cs_change_impact",
  "cs_test_impact",
  "cs_refactor_preview"
]

[plugins."dotnet-semantic-tools".mcp_servers.roslyn.tools.cs_apply_workspace_edit]
approval_mode = "prompt"
```

---

## 12. MCP server instructions

The MCP server must emit concise instructions. Codex uses MCP server instructions to decide how the server should be used, and OpenAI’s docs recommend keeping the first 512 characters self-contained. ([OpenAI Developers][1])

Use this instruction text:

```text
Use this server first for C#/.NET repositories. Prefer cs_repo_overview, cs_symbol_search, cs_symbol_at, cs_find_references, cs_find_implementations, cs_type_hierarchy, cs_callers, and cs_diagnostics before grep or broad file reads. Return compact results first. Do not load every solution unless asked. For edits, call cs_refactor_preview before changing files.

This server provides Roslyn-backed semantic information for navigation, diagnostics, impact analysis, and safe refactor planning. It supports repos with multiple .sln files. Use solutionId when known. If a file belongs to multiple solutions, resolve ambiguity with cs_solution_list or cs_solution_select.
```

---

## 13. Skills to make Codex actually use the tools

Skills are important because Codex initially sees a skill’s name, description, and path, then loads the full `SKILL.md` only when selected. That progressive-disclosure behavior means the skill descriptions must be explicit and trigger-rich. ([OpenAI Developers][13])

### Skill: `csharp-semantic-navigation`

`plugin/skills/csharp-semantic-navigation/SKILL.md`

```md
---
name: csharp-semantic-navigation
description: Use for C#/.NET code navigation, symbol lookup, go-to-definition, find references, implementations, type hierarchy, callers, diagnostics, and impact analysis. Prefer Roslyn MCP tools before grep or raw file reads.
---

# C# semantic navigation

When working in a C# or .NET repository:

1. Call `cs_repo_overview` first unless the active solution is already known.
2. Use `cs_solution_list` or `cs_solution_select` when multiple `.sln` files exist.
3. Use `cs_symbol_search` for names, types, methods, properties, interfaces, records, and namespaces.
4. Use `cs_symbol_at` for file/line/column questions.
5. Use `cs_find_references`, `cs_find_implementations`, `cs_type_hierarchy`, or `cs_callers` instead of text search.
6. Request `detailLevel: "tiny"` or `"normal"` first.
7. Do not request source text unless an edit requires it.
```

### Skill: `csharp-safe-refactor`

```md
---
name: csharp-safe-refactor
description: Use for safe C#/.NET rename, move type, extract interface, change signature planning, and refactor impact review. Always call Roslyn refactor preview before editing files.
---

# C# safe refactoring

For semantic refactors:

1. Identify the target symbol with `cs_symbol_search` or `cs_symbol_at`.
2. Call `cs_find_references` to estimate blast radius.
3. Call `cs_refactor_preview`.
4. Review changed files and diagnostics.
5. Only apply edits after approval.
6. Prefer Codex patch editing for small mechanical changes; use `cs_apply_workspace_edit` only when explicitly enabled.
```

### Skill: `dotnet-test-impact`

```md
---
name: dotnet-test-impact
description: Use for selecting impacted .NET/C# tests from changed files, changed symbols, reference graph, call graph, project graph, and naming conventions.
---

# .NET test impact

1. Call `cs_change_impact` for changed files or symbols.
2. Call `cs_test_impact` to rank tests.
3. Prefer targeted tests before full solution tests.
4. Report confidence and why each test is selected.
```

---

## 14. Hooks

Use a `SessionStart` hook to add lightweight repo guidance to Codex context. Codex hooks can add context at session start, and `PreToolUse` hooks can intercept Bash, patch, and MCP tool calls, though they should be treated as guardrails rather than a complete security boundary. ([OpenAI Developers][14])

`plugin/hooks/hooks.json`:

```json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup|resume",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet-roslyn-mcp session-context",
            "timeout": 3,
            "statusMessage": "Loading C# semantic tooling context"
          }
        ]
      }
    ],
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet-roslyn-mcp guard-bash",
            "timeout": 2,
            "statusMessage": "Checking whether Roslyn semantic tools should be used"
          }
        ]
      }
    ]
  }
}
```

The `session-context` command should output compact developer context:

```text
C#/.NET semantic tooling is available. For C# navigation, references, implementations, type hierarchy, diagnostics, and refactor planning, use the Roslyn MCP tools before broad grep or raw file reads. Start with cs_repo_overview. In multi-solution repos, select a solution with cs_solution_list/cs_solution_select. Use compact detail first.
```

The `guard-bash` command should not block normal shell use aggressively. It should only warn or deny pathological commands such as broad source dumping:

```text
deny:
  find . -name '*.cs' -print -exec cat {} \;
  cat $(find . -name '*.cs')
  rg . --glob '*.cs' with huge unrestricted output

warn:
  rg "SomeSymbol" without using cs_symbol_search first
```

---

## 15. Token optimization design

The tool must reduce tokens by making semantic retrieval compact and targeted.

### Rules

```text
1. Never return full source by default.
2. Return symbol IDs, file paths, spans, signatures, and summaries first.
3. Use cursors for large result sets.
4. Use maxItems and maxDepth everywhere.
5. Use detailLevel everywhere.
6. Prefer outlines over source.
7. Prefer semantic edges over copied code.
8. Return source snippets only for edit-critical ranges.
9. Include generated code only when requested.
10. Use stable tool names, schemas, and skill descriptions.
```

OpenAI’s prompt-caching guidance favors stable repeated prompt prefixes and placing variable content later; tool lists, tool schemas, and structured outputs can be part of cached inputs in API contexts. For Codex app/plugin usage, the practical design implication is to keep MCP instructions, tool schemas, and skill descriptions stable and compact rather than constantly generating large dynamic instructions. ([OpenAI Platform][15])

### Context pack format

Add a tool:

```text
cs_context_pack
```

Disabled by default for MVP, enabled after the main tools are stable.

Request:

```json
{
  "task": "rename CustomerService.GetAsync to GetCustomerAsync",
  "symbols": [
    "csid:v1:r_abc:sln_tralali:Tralali.Core:method:Tralali.Core.CustomerService.GetAsync:1c3e7b"
  ],
  "maxTokens": 2500
}
```

Response:

```json
{
  "resultKind": "ok",
  "summary": "Compact semantic context for rename.",
  "context": {
    "activeSolution": "TralaliProject/project.sln",
    "primarySymbol": {
      "id": "csid:v1:r_abc:sln_tralali:Tralali.Core:method:Tralali.Core.CustomerService.GetAsync:1c3e7b",
      "signature": "Task<Customer> CustomerService.GetAsync(int customerId)",
      "file": "TralaliProject/src/Tralali.Core/CustomerService.cs",
      "line": 42
    },
    "references": {
      "count": 18,
      "files": [
        "TralaliProject/src/Tralali.Api/CustomerController.cs",
        "TralaliProject/tests/CustomerServiceTests.cs"
      ]
    },
    "diagnostics": [],
    "testHints": [
      "CustomerServiceTests.GetAsync_ReturnsCustomer"
    ]
  }
}
```

This gives Codex the facts it needs without loading the conversation with source files.

---

## 16. Refactoring specification

### Supported MVP refactors

Enable preview by default:

```text
rename symbol
move type to namespace
move type to file
extract interface preview
change namespace preview
organize usings preview
```

Apply is disabled by default.

### Preview object

```json
{
  "editId": "edit_x",
  "operation": "rename",
  "symbolId": "csid:v1:...",
  "solutionId": "sln_tralali",
  "changedFiles": 7,
  "changedSpans": 18,
  "diagnosticsBefore": 2,
  "diagnosticsAfter": 2,
  "newDiagnostics": [],
  "risk": "low | medium | high",
  "riskReasons": [
    "Public API change",
    "Symbol used across test project"
  ],
  "diffPreview": "compact diff"
}
```

### Risk model

Mark risk as high when:

```text
public or protected API changed
symbol crosses project boundary
symbol appears in serialized contract DTO
symbol appears in controller/action route pattern
symbol appears in reflection string candidate
symbol belongs to generated or partial type
symbol is in shared project used by multiple solutions
new diagnostics appear after preview
```

---

## 17. Diagnostics specification

Default diagnostics should be compiler-level and project-scoped. Full analyzer execution should be opt-in.

Tools:

```text
cs_diagnostics
cs_diagnostics_summary
```

Modes:

```text
file
  Fastest. Use document semantic model.

project
  Default for active work. Use project compilation.

solution
  Allowed but paged and capped.

analyzers
  Disabled by default. Runs configured analyzers.
```

Response shape:

```json
{
  "resultKind": "ok",
  "summary": "3 errors and 12 warnings in selected project.",
  "diagnostics": [
    {
      "id": "CS8602",
      "severity": "warning",
      "message": "Dereference of a possibly null reference.",
      "file": "src/App/Foo.cs",
      "line": 42,
      "column": 17,
      "symbolId": "optional",
      "help": "optional"
    }
  ],
  "truncated": false
}
```

---

## 18. Impact and test analysis

### `cs_change_impact`

Inputs:

```json
{
  "changedFiles": [
    "TralaliProject/src/Tralali.Core/CustomerService.cs"
  ],
  "scope": {
    "solutionId": "sln_tralali"
  },
  "maxDepth": 2
}
```

Algorithm:

```text
1. Map changed files to declarations.
2. Resolve changed declarations to semantic symbols when workspace is warm.
3. Find direct references.
4. Find callers for changed methods.
5. Add derived/implementing types for interface/base changes.
6. Add dependent projects through project references.
7. Rank impacted files/projects/tests.
```

### `cs_test_impact`

Ranking signals:

```text
direct reference from test to changed symbol
test project references changed project
test class name matches changed type
test method name matches changed method
namespace proximity
fixture/setup reference to changed type
recently failing test cache, if available
```

Response:

```json
{
  "resultKind": "ok",
  "summary": "Recommended 6 targeted tests before full solution test.",
  "tests": [
    {
      "displayName": "CustomerServiceTests.GetAsync_ReturnsCustomer",
      "file": "TralaliProject/tests/CustomerServiceTests.cs",
      "line": 31,
      "confidence": 0.92,
      "reasons": [
        "Direct reference to changed method",
        "Name match with changed type"
      ],
      "command": "dotnet test TralaliProject/tests/Tralali.Tests.csproj --filter FullyQualifiedName~CustomerServiceTests"
    }
  ]
}
```

---

## 19. Performance strategy

### Cold path

Cold path must answer without Roslyn workspace load:

```text
cs_repo_overview
cs_solution_list
cs_index_status
cs_symbol_search
cs_document_outline
```

These tools use SQLite and syntax index only.

### Warm path

Warm path requires selected solution:

```text
cs_symbol_at
cs_find_references
cs_find_implementations
cs_type_hierarchy
cs_callers
cs_diagnostics
cs_refactor_preview
```

Warm loading is lazy and scoped.

### Caching targets

```text
Existing index open:
  p90 <= 700 ms

Repo overview:
  p90 <= 100 ms

Symbol search:
  p90 <= 75 ms

Document outline:
  p90 <= 100 ms

Warm symbol-at:
  p90 <= 300 ms

Warm references for common symbol:
  p90 <= 2 s, paged

Refactor preview:
  p90 <= 5 s for ordinary rename, paged preview

Memory:
  default <= 2 warm solutions
  dispose inactive workspaces
```

### Optimization rules

```text
1. Avoid full solution semantic graph construction on startup.
2. Parse changed syntax files incrementally.
3. Store semantic edges only when computed.
4. Mark semantic edges stale on file/project changes.
5. Recompute stale edges on demand.
6. Cap all graph traversals.
7. Page all high-cardinality results.
8. Prefer SQLite FTS for initial lookup.
9. Use Roslyn only after narrowing scope.
10. Use daemon mode for large repos.
```

---

## 20. HTTP daemon mode

Add after MVP:

```bash
dotnet-roslyn-mcp serve --http --port 38777
```

Codex config:

```toml
[mcp_servers.roslyn]
url = "http://127.0.0.1:38777/mcp"
startup_timeout_sec = 1
tool_timeout_sec = 20
```

Security rules:

```text
bind only to 127.0.0.1 by default
reject non-localhost Host headers
validate Origin header
optional bearer token
no remote telemetry by default
separate cache by repo_id
never expose arbitrary file read tools
write tools require approval
```

The MCP Streamable HTTP specification explicitly warns local servers to validate origin headers, bind to localhost for local-only servers, and implement authentication where needed. ([Model Context Protocol][2])

---

## 21. CLI commands

Ship one executable:

```bash
dotnet-roslyn-mcp serve --stdio
dotnet-roslyn-mcp serve --http --port 38777
dotnet-roslyn-mcp index --repo .
dotnet-roslyn-mcp status --repo .
dotnet-roslyn-mcp doctor --repo .
dotnet-roslyn-mcp session-context
dotnet-roslyn-mcp clear-cache --repo .
```

### `doctor`

Checks:

```text
.NET SDK installed
MSBuild registration works
repo root detected
solutions discovered
SQLite cache readable/writable
MCP server can list tools
workspace can load selected solution
project restore state
target framework availability
```

### `status`

Output:

```json
{
  "repoRoot": "/repo",
  "solutions": [
    {
      "solutionId": "sln_tralali",
      "path": "TralaliProject/project.sln",
      "projects": 18,
      "workspace": "cold",
      "index": "fresh"
    },
    {
      "solutionId": "sln_solutionproject",
      "path": "SolutionProject/project.sln",
      "projects": 12,
      "workspace": "cold",
      "index": "fresh"
    }
  ],
  "filesIndexed": 1842,
  "symbolsIndexed": 23155,
  "semanticEdgesCached": 8450
}
```

---

## 22. Development milestones

### Phase 0: skeleton

Deliver:

```text
dotnet solution structure
MCP STDIO server
plugin manifest
.mcp.json
one skill
cs_repo_overview
cs_solution_list
cs_index_status
```

Definition of done:

```text
Codex can load the plugin.
Codex can list MCP tools.
cs_repo_overview returns discovered solutions without loading MSBuildWorkspace.
```

### Phase 1: cold index MVP

Deliver:

```text
SQLite schema
repo scanner
syntax declaration parser
FTS symbol search
file watcher
incremental reindex
multi-sln membership model
cs_symbol_search
cs_document_outline
```

Definition of done:

```text
Existing index opens under 700 ms p90.
Symbol search works before any solution is loaded.
Two sibling .sln files are both discovered and selectable.
```

### Phase 2: semantic workspace MVP

Deliver:

```text
MSBuildLocator integration
lazy MSBuildWorkspace load
workspace LRU
semantic symbol identity
cs_symbol_at
cs_find_references
cs_find_implementations
cs_type_hierarchy
cs_callers
cs_diagnostics
```

Definition of done:

```text
Only selected solution loads.
Shared files across solutions return ambiguity when necessary.
Semantic results persist to SQLite and are invalidated when files change.
```

### Phase 3: Codex steering

Deliver:

```text
final skills
SessionStart hook
optional PreToolUse guard
stable MCP instructions
plugin-scoped config sample
AGENTS.md template
```

Definition of done:

```text
For C# symbol/reference questions, Codex reliably calls cs_symbol_search/cs_find_references before broad shell search in test prompts.
Tool outputs stay compact.
No default tool emits full source.
```

Codex can also be guided by repository instructions such as `AGENTS.md`, which are intended to tell Codex how to navigate a codebase, run commands, and follow project conventions. ([OpenAI][16])

### Phase 4: refactor preview and impact

Deliver:

```text
cs_refactor_preview
rename preview
move type preview
organize usings preview
cs_change_impact
cs_test_impact
diagnostics before/after preview
```

Definition of done:

```text
Rename preview produces compact diff and changed-file summary.
No mutation occurs unless apply tool is explicitly enabled.
Impact/test recommendations include confidence and reasons.
```

### Phase 5: daemon and advanced packs

Deliver:

```text
Streamable HTTP daemon
persistent warm workspace cache
optional data-flow/control-flow tools
optional analyzer runner
optional code-fix preview
optional full call graph
optional public API diff
```

Definition of done:

```text
Large repo sessions reuse loaded workspace through daemon mode.
Advanced tools remain disabled by default.
```

---

## 23. Test strategy

Use these test layers:

```text
Unit tests
  Symbol ID generation
  solution selection
  file hashing
  syntax declaration extraction
  SQLite persistence
  feature flag evaluation
  output truncation/token limits

Integration tests
  sample repo with two .sln files
  shared project included in both solutions
  multi-targeted project
  stale index recovery
  file watcher overflow simulation
  workspace load failure
  missing restore assets
  ambiguous file membership

Roslyn semantic tests
  symbol_at
  references
  implementations
  type hierarchy
  callers
  diagnostics
  rename preview

MCP protocol tests
  tools/list
  tools/call
  schema validation
  cancellation
  timeout
  malformed requests

Codex behavior tests
  skill selection prompts
  “find references” prompt should use MCP
  “rename symbol” prompt should call preview
  broad grep should be avoided when semantic tools apply

Benchmarks
  cold startup
  existing index open
  syntax reindex changed file
  FTS symbol search
  first solution load
  warm symbol_at
  references query
```

---

## 24. Observability

Emit local logs only by default.

```text
Log categories:
  startup
  index
  workspace
  mcp
  query
  refactor
  diagnostics
  cache
  security

Metrics:
  server_start_ms
  index_open_ms
  syntax_index_file_ms
  workspace_load_ms
  tool_duration_ms
  tool_result_items
  tool_estimated_tokens
  sqlite_query_ms
  cache_hit_rate
  workspace_memory_estimate
```

Add OpenTelemetry exporters only behind explicit config:

```json
{
  "telemetry": {
    "enabled": false,
    "otlpEndpoint": null
  }
}
```

---

## 25. Security model

Default posture:

```text
local only
no remote service dependency
no source upload
no telemetry unless enabled
STDIO preferred
HTTP binds to localhost only
write tools disabled by default
restore/build commands not run automatically
generated/secrets/env files excluded
all mutation requires approval
```

Do not expose a generic “read arbitrary file” MCP tool. Codex already has file access in its environment; this server should expose semantic operations only.

---

## 26. Concrete implementation order

Build in this order:

```text
1. Codex.Roslyn.Abstractions
2. Codex.Roslyn.Index SQLite schema
3. repo scanner and multi-sln discovery
4. syntax declaration indexer
5. MCP STDIO server with repo/index/symbol search tools
6. plugin manifest + .mcp.json + semantic-navigation skill
7. MSBuildLocator + lazy MSBuildWorkspace
8. symbol identity service
9. symbol_at
10. references / implementations / type hierarchy / callers
11. diagnostics
12. refactor preview
13. change impact / test impact
14. hooks and Codex steering
15. HTTP daemon
16. advanced opt-in Roslyn tools
```

The key engineering constraint is to keep **startup, search, and repo overview independent of Roslyn workspace loading**. Roslyn should be invoked only after Codex has narrowed the query to a solution, file, symbol, or project. That is what gives you fast startup, lower token usage, and better model behavior in multi-solution repositories.

[1]: https://developers.openai.com/codex/mcp "Model Context Protocol – Codex | OpenAI Developers"
[2]: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports "Transports - Model Context Protocol"
[3]: https://dotnet.microsoft.com/en-us/platform/support/policy?utm_source=chatgpt.com "The official .NET support policy | .NET"
[4]: https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Workspaces/?utm_source=chatgpt.com "NuGet Gallery | Microsoft.CodeAnalysis.CSharp.Workspaces 5.3.0"
[5]: https://www.nuget.org/packages/ModelContextProtocol?utm_source=chatgpt.com "NuGet Gallery | ModelContextProtocol 1.4.0"
[6]: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-workspace "Work with the .NET Compiler Platform SDK workspace model - C# | Microsoft Learn"
[7]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder?view=roslyn-dotnet-4.14.0 "SymbolFinder Class (Microsoft.CodeAnalysis.FindSymbols) | Microsoft Learn"
[8]: https://www.sqlite.org/wal.html "Write-Ahead Logging"
[9]: https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0 "FileSystemWatcher Class (System.IO) | Microsoft Learn"
[10]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.semanticmodel?view=roslyn-dotnet-4.14.0 "SemanticModel Class (Microsoft.CodeAnalysis) | Microsoft Learn"
[11]: https://modelcontextprotocol.io/specification/2025-06-18/server/tools "Tools - Model Context Protocol"
[12]: https://developers.openai.com/codex/plugins/build "Build plugins – Codex | OpenAI Developers"
[13]: https://developers.openai.com/codex/skills "Agent Skills – Codex | OpenAI Developers"
[14]: https://developers.openai.com/codex/hooks "Hooks – Codex | OpenAI Developers"
[15]: https://platform.openai.com/docs/guides/prompt-caching "Prompt caching | OpenAI API"
[16]: https://openai.com/index/introducing-codex/?utm_source=chatgpt.com "Introducing Codex | OpenAI"
