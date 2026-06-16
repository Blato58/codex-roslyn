using System.Text.Json;

namespace Codex.Roslyn.Tests;

public sealed class PluginPackagingTests
{
    [Fact]
    public void PluginManifest_ReferencesSkillsMcpAndHooks()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", ".codex-plugin", "plugin.json")));
        var root = document.RootElement;

        Assert.Equal("dotnet-semantic-tools", root.GetProperty("name").GetString());
        Assert.Equal("./skills/", root.GetProperty("skills").GetString());
        Assert.Equal("./.mcp.json", root.GetProperty("mcpServers").GetString());
        Assert.Equal("./hooks/hooks.json", root.GetProperty("hooks").GetString());
    }

    [Fact]
    public void HooksFile_DeclaresSessionStartAndWarningOnlyPreToolUse()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoPath("plugin", "hooks", "hooks.json")));
        var hooks = document.RootElement.GetProperty("hooks");
        var sessionStartCommand = hooks
            .GetProperty("SessionStart")[0]
            .GetProperty("hooks")[0]
            .GetProperty("command")
            .GetString();
        var preToolUseCommand = hooks
            .GetProperty("PreToolUse")[0]
            .GetProperty("hooks")[0]
            .GetProperty("command")
            .GetString();

        Assert.Equal("dotnet-roslyn-mcp session-context", sessionStartCommand);
        Assert.Equal("Bash", hooks.GetProperty("PreToolUse")[0].GetProperty("matcher").GetString());
        Assert.Equal("dotnet-roslyn-mcp guard-bash", preToolUseCommand);
    }

    [Theory]
    [InlineData("plugin", "skills", "csharp-semantic-navigation", "SKILL.md")]
    [InlineData("plugin", "skills", "csharp-safe-refactor", "SKILL.md")]
    [InlineData("plugin", "skills", "dotnet-test-impact", "SKILL.md")]
    [InlineData("plugin", "config", "roslyn.config.toml")]
    [InlineData("plugin", "config", "roslyn.daemon.config.toml")]
    [InlineData("plugin", "templates", "AGENTS.md")]
    public void PhaseThreePluginFiles_Exist(params string[] pathParts)
    {
        Assert.True(File.Exists(RepoPath(pathParts)), string.Join(Path.DirectorySeparatorChar, pathParts));
    }

    [Fact]
    public void ConfigSample_EnablesOnlyReadOnlyPhaseZeroToTwoTools()
    {
        var config = File.ReadAllText(RepoPath("plugin", "config", "roslyn.config.toml"));

        Assert.Contains("cs_diagnostics", config);
        Assert.Contains("cs_change_impact", config);
        Assert.Contains("cs_test_impact", config);
        Assert.Contains("cs_refactor_preview", config);
        Assert.DoesNotContain("cs_apply_workspace_edit", config);
    }

    [Fact]
    public void DaemonConfig_UsesLoopbackHttpEndpointAndReadOnlyDefaultTools()
    {
        var config = File.ReadAllText(RepoPath("plugin", "config", "roslyn.daemon.config.toml"));

        Assert.Contains("http://127.0.0.1:38777/mcp", config);
        Assert.Contains("cs_refactor_preview", config);
        Assert.DoesNotContain("cs_apply_workspace_edit", config);
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
