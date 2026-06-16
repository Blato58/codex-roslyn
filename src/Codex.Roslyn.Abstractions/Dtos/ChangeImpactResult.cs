namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record ChangeImpactResult
{
    public string Area { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string? Project { get; init; }

    public string? ProjectFile { get; init; }

    public string Confidence { get; init; } = "medium";

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> SuggestedCommands { get; init; } = [];
}
