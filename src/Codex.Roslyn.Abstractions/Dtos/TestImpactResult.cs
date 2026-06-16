namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record TestImpactResult
{
    public string TestArea { get; init; } = string.Empty;

    public string? Project { get; init; }

    public string? ProjectFile { get; init; }

    public string? Command { get; init; }

    public string Confidence { get; init; } = "medium";

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<string> Files { get; init; } = [];
}
