namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SymbolSearchResult
{
    public required string SymbolId { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required string File { get; init; }

    public int Line { get; init; }

    public string Confidence { get; init; } = "syntax_only";
}
