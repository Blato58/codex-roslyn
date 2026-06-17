namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record WorkspaceEditApplyResult
{
    public string EditId { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public int AppliedFiles { get; init; }

    public int AppliedSpans { get; init; }

    public IReadOnlyList<string> AppliedFilePaths { get; init; } = [];

    public IReadOnlyList<string> SkippedFiles { get; init; } = [];

    public int DiagnosticsBefore { get; init; }

    public int DiagnosticsAfter { get; init; }

    public string? RollbackWarning { get; init; }
}
