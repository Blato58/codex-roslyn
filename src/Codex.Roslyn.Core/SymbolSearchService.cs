using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;

namespace Codex.Roslyn.Core;

public sealed class SymbolSearchService(
    RepoRootResolver repoRootResolver,
    ColdIndexService coldIndexService,
    IndexBuildService indexBuildService)
{
    public ToolResponse<SymbolSearchResult> Search(
        string query,
        string? kind = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolResponse<SymbolSearchResult>
            {
                ResultKind = "error",
                Summary = "Query is required.",
                CacheStatus = new CacheStatus { Index = "miss", Workspace = "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 40 }
            };
        }

        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var recovery = indexBuildService.EnsureFresh(repoRoot);
        var status = recovery.Status;

        var results = coldIndexService.SearchSymbols(repoRoot, query, kind, maxItems);
        var summary = results.Count == 1 ? "Found 1 syntax-only symbol." : $"Found {results.Count} syntax-only symbols.";

        return ToolResponse<SymbolSearchResult>.Ok(
            summary,
            results,
            detailLevel,
            indexStatus: status.IndexState == "fresh" ? "hit" : "stale",
            workspaceStatus: "cold",
            warnings: recovery.Warning is null ? null : [recovery.Warning]);
    }
}
