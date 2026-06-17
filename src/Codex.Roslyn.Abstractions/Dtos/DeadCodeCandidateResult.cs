namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record DeadCodeCandidateResult
{
    public string SymbolId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string File { get; init; } = string.Empty;

    public int Line { get; init; }

    public string Confidence { get; init; } = "low";

    public IReadOnlyList<string> Reasons { get; init; } = [];
}
