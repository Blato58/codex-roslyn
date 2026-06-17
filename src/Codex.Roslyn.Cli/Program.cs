using System.Text.Json;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Core;
using Codex.Roslyn.Mcp;
using Microsoft.Extensions.DependencyInjection;

return await CliProgram.RunAsync(args);

internal static class CliProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage();
            return 0;
        }

        return args[0] switch
        {
            "serve" => await RunServeAsync(args[1..]),
            "index" => RunIndex(args[1..]),
            "status" => RunStatus(args[1..]),
            "doctor" => RunDoctor(args[1..]),
            "clear-cache" => RunClearCache(args[1..]),
            "session-context" => RunSessionContext(),
            "guard-bash" => RunGuardBash(),
            _ => UnknownCommand(args[0])
        };
    }

    private static int RunIndex(string[] args)
    {
        var repoRoot = GetOptionValue(args, "--repo");
        var services = CreateServices();
        var resolvedRoot = services.GetRequiredService<RepoRootResolver>().Resolve(repoRoot);
        var result = services.GetRequiredService<Codex.Roslyn.Index.ColdIndexService>().Build(resolvedRoot);

        WriteJson(new
        {
            resultKind = "ok",
            summary = $"Indexed {result.FilesIndexed} files and {result.DeclarationsIndexed} declarations.",
            item = result
        });
        return 0;
    }

    private static async Task<int> RunServeAsync(string[] args)
    {
        if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase))
        {
            await McpServerRunner.RunStdioAsync(args);
            return 0;
        }

        if (args.Contains("--http", StringComparer.OrdinalIgnoreCase))
        {
            await McpServerRunner.RunHttpAsync(args);
            return 0;
        }

        Console.Error.WriteLine("Serve requires either '--stdio' or '--http'.");
        return 2;
    }

    private static int RunStatus(string[] args)
    {
        var repoRoot = GetOptionValue(args, "--repo");
        var services = CreateServices();
        var overview = services.GetRequiredService<RepoOverviewService>()
            .GetOverview(new ToolScope { RepoRoot = repoRoot });

        WriteJson(overview);
        return 0;
    }

    private static int RunDoctor(string[] args)
    {
        var repoRoot = GetOptionValue(args, "--repo");
        var services = CreateServices();
        var overview = services.GetRequiredService<RepoOverviewService>()
            .GetOverview(new ToolScope { RepoRoot = repoRoot });
        var sdkVersion = Environment.Version.ToString();
        var indexStatus = services.GetRequiredService<IndexStatusService>().GetStatus(repoRoot).Items.Single();

        var result = new
        {
            resultKind = "ok",
            summary = "Cold index doctor completed without loading MSBuildWorkspace.",
            dotnetRuntime = sdkVersion,
            index = indexStatus,
            repo = overview.Items.Single(),
            checks = new[]
            {
                new { name = "repo_root_detected", status = "ok" },
                new { name = "solutions_discovered", status = "ok" },
                new { name = "cold_index", status = indexStatus.IndexState },
                new { name = "workspace_load", status = "skipped_cold_index" },
                new { name = "sqlite_cache", status = string.IsNullOrEmpty(indexStatus.CachePath) ? "missing" : "ok" }
            }
        };

        WriteJson(result);
        return 0;
    }

    private static int RunClearCache(string[] args)
    {
        var repoRoot = GetOptionValue(args, "--repo");
        var services = CreateServices();
        var resolvedRoot = services.GetRequiredService<RepoRootResolver>().Resolve(repoRoot);
        var cacheDirectory = services.GetRequiredService<Codex.Roslyn.Index.ColdIndexService>().Clear(resolvedRoot);

        WriteJson(new
        {
            resultKind = "ok",
            summary = "Cleared CodexRoslyn cache for repository.",
            repoRoot = resolvedRoot,
            cacheDirectory
        });
        return 0;
    }

    private static int RunSessionContext()
    {
        Console.WriteLine("C#/.NET semantic tooling is available. For C# code tasks, use Roslyn MCP tools before broad grep: find usages, find references, go to definition, implementations, diagnostics, compile errors, test impact, rename, and refactor planning. Start with cs_repo_overview. In multi-solution repos, select a solution with cs_solution_list/cs_solution_select. Use compact detail first. For validation, call cs_test_impact and prefer the returned dotnet test command before inventing a broad command.");
        return 0;
    }

    private static int RunGuardBash()
    {
        var input = Console.In.ReadToEnd();
        var services = CreateServices();
        var result = services.GetRequiredService<BashGuardService>().EvaluateHookInput(input);

        WriteJson(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PreToolUse",
                additionalContext = result.AdditionalContext
            }
        });

        return 0;
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddCodexRoslynServices();
        return services.BuildServiceProvider();
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }
        }

        return null;
    }

    private static bool IsHelp(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        WriteUsage();
        return 2;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("""
dotnet-roslyn-mcp commands:
  serve --stdio [--enable-apply]
  serve --http --port 38777 [--enable-apply]
  index --repo <path>
  status --repo <path>
  doctor --repo <path>
  clear-cache --repo <path>
  session-context
  guard-bash
""");
    }

    private static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }
}
