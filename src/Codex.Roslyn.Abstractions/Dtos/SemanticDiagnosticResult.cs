namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SemanticDiagnosticResult
{
    public required string Id { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }

    public string? File { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public string Confidence { get; init; } = "semantic";
}
