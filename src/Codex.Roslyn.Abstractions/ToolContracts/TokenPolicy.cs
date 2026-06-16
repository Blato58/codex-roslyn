namespace Codex.Roslyn.Abstractions.ToolContracts;

public sealed record TokenPolicy
{
    public string DetailLevel { get; init; } = "normal";

    public int EstimatedTokens { get; init; }

    public bool Truncated { get; init; }
}
