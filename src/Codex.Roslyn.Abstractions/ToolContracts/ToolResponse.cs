namespace Codex.Roslyn.Abstractions.ToolContracts;

public sealed record ToolResponse<TItem>
{
    public string ResultKind { get; init; } = "ok";

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<TItem> Items { get; init; } = [];

    public string? NextCursor { get; init; }

    public CacheStatus CacheStatus { get; init; } = new();

    public TokenPolicy TokenPolicy { get; init; } = new();

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static ToolResponse<TItem> Ok(
        string summary,
        IReadOnlyList<TItem> items,
        string detailLevel = "normal",
        string indexStatus = "miss",
        string workspaceStatus = "cold",
        IReadOnlyList<string>? warnings = null)
    {
        return new ToolResponse<TItem>
        {
            ResultKind = "ok",
            Summary = summary,
            Items = items,
            CacheStatus = new CacheStatus
            {
                Index = indexStatus,
                Workspace = workspaceStatus
            },
            TokenPolicy = new TokenPolicy
            {
                DetailLevel = detailLevel,
                EstimatedTokens = EstimateTokens(items.Count, warnings?.Count ?? 0),
                Truncated = false
            },
            Warnings = warnings ?? []
        };
    }

    private static int EstimateTokens(int itemCount, int warningCount)
    {
        return 80 + (itemCount * 60) + (warningCount * 20);
    }
}
