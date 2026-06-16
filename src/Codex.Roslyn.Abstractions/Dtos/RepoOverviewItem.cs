namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record RepoOverviewItem
{
    public required string RepoRoot { get; init; }

    public int SolutionCount { get; init; }

    public string IndexState { get; init; } = "uninitialized";

    public string WorkspaceState { get; init; } = "cold";

    public IReadOnlyList<SolutionSummary> Solutions { get; init; } = [];
}
