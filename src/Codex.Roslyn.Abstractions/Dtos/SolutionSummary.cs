namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SolutionSummary
{
    public required string SolutionId { get; init; }

    public required string Path { get; init; }

    public required string DisplayName { get; init; }

    public bool IsActive { get; init; }

    public string Reason { get; init; } = "Discovered by filesystem scan";
}
