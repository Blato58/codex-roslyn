using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;

namespace Codex.Roslyn.Core;

public sealed class IndexStatusService(RepoRootResolver repoRootResolver, ColdIndexService coldIndexService)
{
    public ToolResponse<IndexStatusSummary> GetStatus(string? repoRoot = null, string detailLevel = "normal")
    {
        var resolvedRoot = repoRootResolver.Resolve(repoRoot);
        var status = coldIndexService.GetStatus(resolvedRoot);
        var indexStatus = status.IndexState switch
        {
            "fresh" => "hit",
            "stale" => "stale",
            _ => "miss"
        };

        return ToolResponse<IndexStatusSummary>.Ok(
            status.Message,
            [status],
            detailLevel,
            indexStatus: indexStatus,
            workspaceStatus: "cold");
    }
}
