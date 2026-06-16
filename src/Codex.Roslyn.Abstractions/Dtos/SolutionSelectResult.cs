namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SolutionSelectResult
{
    public required string SolutionId { get; init; }

    public required string Path { get; init; }

    public required string State { get; init; }
}
