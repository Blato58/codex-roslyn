using System.Text.Json;

namespace Codex.Roslyn.Tests;

public sealed class PluginPackagingTests
{
    [Fact]
    public void PluginManifest_ReferencesSkillsAndMcp()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", ".codex-plugin", "plugin.json")));
        var root = document.RootElement;

        Assert.Equal("dotnet-semantic-tools", root.GetProperty("name").GetString());
        Assert.Equal("./skills/", root.GetProperty("skills").GetString());
        Assert.Equal("./.mcp.json", root.GetProperty("mcpServers").GetString());
        Assert.False(root.TryGetProperty("hooks", out _));

        var author = root.GetProperty("author");
        Assert.Equal("CodexRoslyn contributors", author.GetProperty("name").GetString());

        var pluginInterface = root.GetProperty("interface");
        Assert.Equal("Dotnet Semantic Tools", pluginInterface.GetProperty("displayName").GetString());
        Assert.Equal("Productivity", pluginInterface.GetProperty("category").GetString());
    }

    [Fact]
    public void PluginManifest_DefaultPromptContainsPracticalTriggerPhrases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", ".codex-plugin", "plugin.json")));
        var root = document.RootElement;
        var pluginInterface = root.GetProperty("interface");
        var text = string.Join(
            " ",
            root.GetProperty("description").GetString(),
            pluginInterface.GetProperty("shortDescription").GetString(),
            pluginInterface.GetProperty("longDescription").GetString(),
            string.Join(" ", pluginInterface.GetProperty("defaultPrompt").EnumerateArray().Select(item => item.GetString())));

        Assert.Contains("find usages", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("find references", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("go to definition", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("implementations", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostics", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compile errors", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test impact", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rename", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refactor", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cold index", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cs_index_build", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("C#/.NET", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Roslyn", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HooksFile_DeclaresOnlyLightweightSessionStart()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", "hooks", "hooks.json")));
        var hooks = document.RootElement.GetProperty("hooks");
        var sessionStartCommand = hooks
            .GetProperty("SessionStart")[0]
            .GetProperty("hooks")[0]
            .GetProperty("command")
            .GetString();

        Assert.Contains("scripts/roslyn-mcp.ps1", sessionStartCommand);
        Assert.Contains("session-context", sessionStartCommand);
        Assert.False(hooks.TryGetProperty("PreToolUse", out _));
    }

    [Fact]
    public void McpConfig_UsesWrappedServerMapAndLauncher()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", ".mcp.json")));
        var roslynServer = document.RootElement
            .GetProperty("mcp_servers")
            .GetProperty("roslyn");
        var args = roslynServer
            .GetProperty("args")
            .EnumerateArray()
            .Select(arg => arg.GetString())
            .ToArray();

        Assert.Equal("powershell", roslynServer.GetProperty("command").GetString());
        Assert.Contains("./scripts/roslyn-mcp.ps1", args);
        Assert.Contains("serve", args);
        Assert.Contains("--stdio", args);
        Assert.Equal(".", roslynServer.GetProperty("cwd").GetString());
        Assert.True(roslynServer.GetProperty("startup_timeout_sec").GetInt32() >= 60);
    }

    [Fact]
    public void Launcher_PrintsInstallInstructionWithoutInstallingTool()
    {
        var script = File.ReadAllText(RepoPath("plugin", "scripts", "roslyn-mcp.ps1"));

        Assert.Contains("dotnet tool install -g Blato58.RoslynMcp", script);
        Assert.DoesNotContain("& dotnet tool install", script);
        Assert.Contains("exit 127", script);
    }

    [Fact]
    public void McpConfig_EnablesOnlyCoreReadOnlyToolsByDefault()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", ".mcp.json")));
        var roslynServer = document.RootElement
            .GetProperty("mcp_servers")
            .GetProperty("roslyn");
        var enabledTools = roslynServer
            .GetProperty("enabled_tools")
            .EnumerateArray()
            .Select(tool => tool.GetString())
            .ToArray();

        Assert.True(roslynServer.GetProperty("tool_timeout_sec").GetInt32() >= 60);
        Assert.Contains("cs_repo_overview", enabledTools);
        Assert.Contains("cs_solution_list", enabledTools);
        Assert.Contains("cs_solution_select", enabledTools);
        Assert.Contains("cs_index_status", enabledTools);
        Assert.Contains("cs_index_build", enabledTools);
        Assert.Contains("cs_symbol_search", enabledTools);
        Assert.Contains("cs_document_outline", enabledTools);
        Assert.Contains("cs_symbol_at", enabledTools);
        Assert.Contains("cs_find_references", enabledTools);
        Assert.Contains("cs_find_implementations", enabledTools);
        Assert.Contains("cs_type_hierarchy", enabledTools);
        Assert.Contains("cs_callers", enabledTools);
        Assert.Contains("cs_diagnostics", enabledTools);
        Assert.Contains("cs_diagnostics_summary", enabledTools);
        Assert.Contains("cs_change_impact", enabledTools);
        Assert.Contains("cs_test_impact", enabledTools);
        Assert.Contains("cs_refactor_preview", enabledTools);
        Assert.Contains("cs_context_pack", enabledTools);
        Assert.DoesNotContain("cs_apply_workspace_edit", enabledTools);
        Assert.DoesNotContain("cs_full_call_graph", enabledTools);
        Assert.DoesNotContain("cs_data_flow", enabledTools);
        Assert.DoesNotContain("cs_code_fix_preview", enabledTools);
    }

    [Theory]
    [InlineData("plugin", "skills", "csharp-semantic-navigation", "SKILL.md", "find usages")]
    [InlineData("plugin", "skills", "csharp-semantic-navigation", "SKILL.md", "go to definition")]
    [InlineData("plugin", "skills", "csharp-semantic-navigation", "SKILL.md", "compile errors")]
    [InlineData("plugin", "skills", "csharp-safe-refactor", "SKILL.md", "rename")]
    [InlineData("plugin", "skills", "csharp-safe-refactor", "SKILL.md", "change signature")]
    [InlineData("plugin", "skills", "csharp-safe-refactor", "SKILL.md", "organize usings")]
    [InlineData("plugin", "skills", "dotnet-test-impact", "SKILL.md", "targeted dotnet test")]
    [InlineData("plugin", "skills", "dotnet-test-impact", "SKILL.md", "test impact")]
    public void SkillDescriptions_PreserveImplicitTriggerWords(string first, string second, string third, string fourth, string trigger)
    {
        var text = File.ReadAllText(RepoPath(first, second, third, fourth));

        Assert.Contains(trigger, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("plugin", "scripts", "roslyn-mcp.ps1")]
    [InlineData("plugin", "skills", "csharp-semantic-navigation", "SKILL.md")]
    [InlineData("plugin", "skills", "csharp-safe-refactor", "SKILL.md")]
    [InlineData("plugin", "skills", "dotnet-test-impact", "SKILL.md")]
    [InlineData("plugin", "config", "roslyn.config.toml")]
    [InlineData("plugin", "config", "roslyn.daemon.config.toml")]
    [InlineData("plugin", "config", "roslyn.advanced-opt-in.config.toml")]
    [InlineData("plugin", "templates", "AGENTS.md")]
    public void PluginFiles_Exist(params string[] pathParts)
    {
        Assert.True(File.Exists(RepoPath(pathParts)), string.Join(Path.DirectorySeparatorChar, pathParts));
    }

    [Fact]
    public void ConfigSample_EnablesOnlyReadOnlyDefaultTools()
    {
        var config = File.ReadAllText(RepoPath("plugin", "config", "roslyn.config.toml"));

        Assert.Contains("cs_diagnostics", config);
        Assert.Contains("cs_diagnostics_summary", config);
        Assert.Contains("cs_index_build", config);
        Assert.Contains("cs_context_pack", config);
        Assert.Contains("cs_change_impact", config);
        Assert.Contains("cs_test_impact", config);
        Assert.Contains("cs_refactor_preview", config);
        Assert.DoesNotContain("cs_full_call_graph", config);
        Assert.DoesNotContain("cs_apply_workspace_edit", config);
    }

    [Fact]
    public void DaemonConfig_UsesLoopbackHttpEndpointAndReadOnlyDefaultTools()
    {
        var config = File.ReadAllText(RepoPath("plugin", "config", "roslyn.daemon.config.toml"));

        Assert.Contains("http://127.0.0.1:38777/mcp", config);
        Assert.Contains("cs_index_build", config);
        Assert.Contains("cs_refactor_preview", config);
        Assert.DoesNotContain("cs_apply_workspace_edit", config);
    }

    [Fact]
    public void AdvancedConfig_RequiresPromptApprovalAndIncludesApplyTool()
    {
        var config = File.ReadAllText(RepoPath("plugin", "config", "roslyn.advanced-opt-in.config.toml"));

        Assert.Contains("default_tools_approval_mode = \"prompt\"", config);
        Assert.Contains("--enable-apply", config);
        Assert.Contains("CODEX_ROSLYN_ENABLE_APPLY=1", config);
        Assert.Contains("cs_apply_workspace_edit", config);
        Assert.Contains("cs_full_call_graph", config);
        Assert.Contains("cs_code_fix_preview", config);
    }

    private static string RepoPath(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
