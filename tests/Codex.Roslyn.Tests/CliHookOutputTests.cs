using System.Diagnostics;
using System.Text.Json;

namespace Codex.Roslyn.Tests;

public sealed class CliHookOutputTests
{
    [Fact]
    public void GuardBash_EmitsAdvisoryContextWithoutUnsupportedPermissionDecision()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "run --project src/Codex.Roslyn.Cli --no-build -- guard-bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start dotnet.");

        process.StandardInput.Write("""{"toolInput":{"command":"rg CustomerService"}}""");
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60000), "guard-bash process did not exit within 60 seconds.");
        Assert.Equal(0, process.ExitCode);

        using var document = JsonDocument.Parse(stdout);
        var output = document.RootElement.GetProperty("hookSpecificOutput");

        Assert.Equal("PreToolUse", output.GetProperty("hookEventName").GetString());
        Assert.False(output.TryGetProperty("permissionDecision", out _));
        Assert.Contains("cs_symbol_search", output.GetProperty("additionalContext").GetString());
        Assert.DoesNotContain("unsupported permissionDecision", stderr, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexRoslyn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
