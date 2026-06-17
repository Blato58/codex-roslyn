namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record DiagnosticsSummaryResult
{
    public string Scope { get; init; } = string.Empty;

    public int ErrorCount { get; init; }

    public int WarningCount { get; init; }

    public int InfoCount { get; init; }

    public IReadOnlyList<string> CountsByProject { get; init; } = [];

    public IReadOnlyList<string> CountsByFile { get; init; } = [];

    public IReadOnlyList<SemanticDiagnosticResult> TopDiagnostics { get; init; } = [];
}
