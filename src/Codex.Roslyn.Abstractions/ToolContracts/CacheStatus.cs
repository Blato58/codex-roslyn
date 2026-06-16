namespace Codex.Roslyn.Abstractions.ToolContracts;

public sealed record CacheStatus
{
    public string Index { get; init; } = "miss";

    public string Workspace { get; init; } = "cold";
}
