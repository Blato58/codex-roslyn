namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record RefactorPreviewResult
{
    public string EditId { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public string? SymbolId { get; init; }

    public string? SolutionId { get; init; }

    public int ChangedFiles { get; init; }

    public int ChangedSpans { get; init; }

    public int DiagnosticsBefore { get; init; }

    public int DiagnosticsAfter { get; init; }

    public int NewDiagnostics { get; init; }

    public string Risk { get; init; } = "low";

    public IReadOnlyList<string> RiskReasons { get; init; } = [];

    public IReadOnlyList<RefactorFileChange> Changes { get; init; } = [];

    public string DiffPreview { get; init; } = string.Empty;

    public bool RequiresApproval { get; init; } = true;
}
