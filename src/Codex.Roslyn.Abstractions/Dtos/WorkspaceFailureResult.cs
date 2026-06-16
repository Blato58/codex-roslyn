namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record WorkspaceFailureResult
{
    public required string State { get; init; }

    public required string Reason { get; init; }

    public required string SolutionPath { get; init; }

    public required string SuggestedCommand { get; init; }

    public bool SafeToRunAutomatically { get; init; }
}
