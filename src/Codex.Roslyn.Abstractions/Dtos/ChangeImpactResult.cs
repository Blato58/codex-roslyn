namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record ChangeImpactResult
{
    public string Area { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Confidence { get; init; } = "medium";

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<string> Files { get; init; } = [];
}
