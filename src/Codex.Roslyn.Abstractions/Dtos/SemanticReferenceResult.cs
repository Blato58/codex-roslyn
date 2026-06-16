namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SemanticReferenceResult
{
    public required string SymbolId { get; init; }

    public required string DisplayName { get; init; }

    public required string File { get; init; }

    public int StartLine { get; init; }

    public int StartColumn { get; init; }

    public bool IsDefinition { get; init; }

    public string ReferenceKind { get; init; } = "reference";

    public string Confidence { get; init; } = "semantic";
}
