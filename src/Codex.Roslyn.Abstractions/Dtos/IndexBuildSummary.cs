namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record IndexBuildSummary
{
    public required string RepoRoot { get; init; }

    public required string RepoId { get; init; }

    public required string CachePath { get; init; }

    public int SchemaVersion { get; init; }

    public int SolutionCount { get; init; }

    public int FilesIndexed { get; init; }

    public int DeclarationsIndexed { get; init; }

    public int SymbolsIndexed { get; init; }

    public string IndexState { get; init; } = "fresh";
}
