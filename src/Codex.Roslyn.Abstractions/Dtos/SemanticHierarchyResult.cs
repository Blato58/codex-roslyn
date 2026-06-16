namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SemanticHierarchyResult
{
    public required string SymbolId { get; init; }

    public required string DisplayName { get; init; }

    public required string RelationKind { get; init; }

    public SemanticLocation? Location { get; init; }

    public string Confidence { get; init; } = "semantic";
}
