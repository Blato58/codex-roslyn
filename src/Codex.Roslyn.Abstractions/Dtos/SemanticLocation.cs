namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record SemanticLocation
{
    public required string File { get; init; }

    public int StartLine { get; init; }

    public int StartColumn { get; init; }

    public int EndLine { get; init; }

    public int EndColumn { get; init; }
}
