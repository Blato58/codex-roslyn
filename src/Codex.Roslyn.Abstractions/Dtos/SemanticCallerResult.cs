namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SemanticCallerResult
{
    public required string CallerSymbolId { get; init; }

    public required string CallerDisplayName { get; init; }

    public required string CalleeSymbolId { get; init; }

    public required string CalleeDisplayName { get; init; }

    public SemanticLocation? Location { get; init; }

    public string Confidence { get; init; } = "semantic";
}
