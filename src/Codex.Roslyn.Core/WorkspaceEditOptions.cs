namespace Codex.Roslyn.Core;

public sealed record WorkspaceEditOptions(bool EnableApply)
{
    public static WorkspaceEditOptions FromEnvironment()
    {
        return new WorkspaceEditOptions(IsEnabled(Environment.GetEnvironmentVariable("CODEX_ROSLYN_ENABLE_APPLY")));
    }

    public static WorkspaceEditOptions FromEnvironmentAndArgs(string[] args)
    {
        return new WorkspaceEditOptions(
            args.Contains("--enable-apply", StringComparer.OrdinalIgnoreCase)
            || IsEnabled(Environment.GetEnvironmentVariable("CODEX_ROSLYN_ENABLE_APPLY")));
    }

    private static bool IsEnabled(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
