# Installation Guide

CodexRoslyn has two installable pieces:

1. The `dotnet-roslyn-mcp` .NET tool, which runs the local MCP server.
2. The Codex plugin bundle under `plugin/`, which provides skills, hooks, and MCP startup configuration.

The plugin starts MCP through `plugin/scripts/roslyn-mcp.ps1`. On Windows, that launcher installs the global `dotnet-roslyn-mcp` .NET tool automatically if it cannot find it, then starts `dotnet-roslyn-mcp serve --stdio`. Installing the tool manually is still useful for development and troubleshooting.

## Prerequisites

- .NET SDK compatible with `global.json`.
- Codex app or Codex CLI with plugin support.
- On Windows, `%USERPROFILE%\.dotnet\tools` must be available to the Codex process PATH when using the global tool.

Check the SDK:

```powershell
dotnet --version
```

## Install the MCP Server Tool

If the package is available from your configured NuGet sources, install it directly:

```powershell
dotnet tool install -g Codex.Roslyn.Mcp.Tool
```

If you already have it installed:

```powershell
dotnet tool update -g Codex.Roslyn.Mcp.Tool
```

For local development from this repository, pack and install from a temporary local package source:

```powershell
$packageSource = Join-Path $env:TEMP "CodexRoslynPackages"
New-Item -ItemType Directory -Force $packageSource | Out-Null
dotnet pack src/Codex.Roslyn.Cli/Codex.Roslyn.Cli.csproj -c Release -o $packageSource
dotnet tool install -g Codex.Roslyn.Mcp.Tool --add-source $packageSource
```

If the tool is already installed from an older local package:

```powershell
dotnet tool update -g Codex.Roslyn.Mcp.Tool --add-source $packageSource
```

Verify the command:

```powershell
dotnet-roslyn-mcp --help
dotnet-roslyn-mcp serve --stdio
```

Stop `serve --stdio` with `Ctrl+C` after confirming it starts.

## Install the Codex Plugin

The plugin manifest is:

```text
plugin/.codex-plugin/plugin.json
```

The plugin MCP config is:

```text
plugin/.mcp.json
```

That config starts the MCP server with:

```text
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File ./scripts/roslyn-mcp.ps1 serve --stdio
```

Install or enable the plugin from the Codex plugin directory or your local marketplace entry that points at this repository's `plugin/` folder. After installing or updating the plugin:

1. Restart Codex.
2. Start a new thread.
3. Ask Codex to use the `dotnet-semantic-tools` plugin or one of its bundled skills.

## Verify MCP Availability

In a C# repository, ask Codex to inspect the repository with Roslyn semantic tools. The first tool should be:

```text
cs_repo_overview
```

If the tools are available, Codex should also be able to call:

```text
cs_symbol_search
cs_solution_select
cs_symbol_at
cs_find_references
cs_diagnostics
cs_diagnostics_summary
cs_context_pack
cs_refactor_preview
```

The server also exposes advanced tools such as `cs_full_call_graph`, `cs_data_flow`, `cs_code_fix_preview`, and `cs_public_api_diff`, plus the mutating `cs_apply_workspace_edit`. These are disabled by the default plugin config. Use `plugin/config/roslyn.advanced-opt-in.config.toml` only when prompt-approved advanced/apply tools are intended.

`cs_apply_workspace_edit` also has a server-side safety gate. Exposing the tool is not enough: start `dotnet-roslyn-mcp serve` with `--enable-apply` or set `CODEX_ROSLYN_ENABLE_APPLY=1` for the server process. Without that explicit opt-in, the tool returns `disabled` and does not mutate files.

You can also verify the CLI outside Codex:

```powershell
dotnet-roslyn-mcp doctor --repo .
dotnet-roslyn-mcp index --repo .
dotnet-roslyn-mcp status --repo .
```

## Troubleshooting

### Plugin installed, but MCP tools are missing

The plugin likely installed correctly, but Codex could not start the MCP server. Check:

- PowerShell can run the launcher:

  ```powershell
  powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\plugin\scripts\roslyn-mcp.ps1 session-context
  ```

- `dotnet-roslyn-mcp` is installed or can be installed by the launcher:

  ```powershell
  dotnet tool list -g
  Get-Command dotnet-roslyn-mcp
  ```

- `%USERPROFILE%\.dotnet\tools` is on PATH for the Codex app process.
- Codex was restarted after plugin installation.
- The current thread was created after the plugin was installed and enabled.
- The plugin is enabled in Codex.

### Command works in PowerShell, but not in Codex

Codex may have been started before PATH was updated. Restart Codex. If it still fails, configure the plugin or Codex MCP settings to use the full command path, for example:

```text
C:\Users\<you>\.dotnet\tools\dotnet-roslyn-mcp.exe
```

The plugin launcher also probes `%USERPROFILE%\.dotnet\tools`, so it can usually find the tool even when Codex was started before PATH changed.

### Local package install fails

Clean the temporary package source and rebuild:

```powershell
$packageSource = Join-Path $env:TEMP "CodexRoslynPackages"
Remove-Item $packageSource -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $packageSource | Out-Null
dotnet pack src/Codex.Roslyn.Cli/Codex.Roslyn.Cli.csproj -c Release -o $packageSource
dotnet tool update -g Codex.Roslyn.Mcp.Tool --add-source $packageSource
```

If the package was not installed before, use `dotnet tool install` instead of `dotnet tool update`.

## Uninstall

Uninstall the .NET tool:

```powershell
dotnet tool uninstall -g Codex.Roslyn.Mcp.Tool
```

Then uninstall or disable the plugin from Codex.
