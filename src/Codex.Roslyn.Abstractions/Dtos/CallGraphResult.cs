namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record CallGraphResult
{
    public string SymbolId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public IReadOnlyList<SemanticLocation> Locations { get; init; } = [];
}
