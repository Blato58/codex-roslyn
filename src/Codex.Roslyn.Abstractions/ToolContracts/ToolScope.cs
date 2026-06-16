namespace Codex.Roslyn.Abstractions.ToolContracts;

public sealed record ToolScope
{
    public string? RepoRoot { get; init; }

    public string? SolutionId { get; init; }

    public string? ProjectId { get; init; }

    public string? TargetFramework { get; init; }

    public string? File { get; init; }
}
