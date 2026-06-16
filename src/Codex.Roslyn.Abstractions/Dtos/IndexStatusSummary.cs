namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record IndexStatusSummary
{
    public required string RepoRoot { get; init; }

    public string RepoId { get; init; } = string.Empty;

    public string CachePath { get; init; } = string.Empty;

    public string IndexState { get; init; } = "uninitialized";

    public string WorkspaceState { get; init; } = "cold";

    public int SchemaVersion { get; init; }

    public int SolutionCount { get; init; }

    public int FilesIndexed { get; init; }

    public int SymbolsIndexed { get; init; }

    public int DeclarationsIndexed { get; init; }

    public string Message { get; init; } = "Phase 0 skeleton does not create SQLite indexes.";
}
