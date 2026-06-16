namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record ContextPackResult
{
    public string ActiveSolution { get; init; } = string.Empty;

    public IReadOnlyList<string> PrimarySymbols { get; init; } = [];

    public IReadOnlyList<string> ReferenceSummary { get; init; } = [];

    public IReadOnlyList<string> TestSummary { get; init; } = [];

    public IReadOnlyList<string> DiagnosticSummary { get; init; } = [];

    public IReadOnlyList<string> SelectedFiles { get; init; } = [];

    public int EstimatedTokens { get; init; }

    public bool Truncated { get; init; }
}
