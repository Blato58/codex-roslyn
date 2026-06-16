namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SemanticSymbolResult
{
    public required string SymbolId { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? DocumentationCommentId { get; init; }

    public string? AssemblyName { get; init; }

    public string? DeclaredAccessibility { get; init; }

    public string? ReturnType { get; init; }

    public SemanticLocation? Definition { get; init; }

    public string Confidence { get; init; } = "semantic";
}
