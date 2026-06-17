namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record CodeFixPreviewResult
{
    public string DiagnosticId { get; init; } = string.Empty;

    public string File { get; init; } = string.Empty;

    public int Line { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Confidence { get; init; } = "low";

    public string Preview { get; init; } = string.Empty;

    public bool RequiresApproval { get; init; } = true;
}
