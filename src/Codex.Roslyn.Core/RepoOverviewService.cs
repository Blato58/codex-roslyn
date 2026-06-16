using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;

namespace Codex.Roslyn.Core;

public sealed class RepoOverviewService(
    RepoRootResolver repoRootResolver,
    SolutionDiscoveryService solutionDiscoveryService,
    IndexStatusService indexStatusService,
    ColdIndexService coldIndexService)
{
    public ToolResponse<RepoOverviewItem> GetOverview(ToolScope? scope = null, string detailLevel = "normal")
    {
        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var indexStatus = indexStatusService.GetStatus(repoRoot, detailLevel).Items.Single();
        var indexedSolutions = coldIndexService.GetSolutions(repoRoot);
        var solutions = indexedSolutions.Count == 0
            ? solutionDiscoveryService.Discover(repoRoot, scope?.SolutionId)
            : indexedSolutions;

        var item = new RepoOverviewItem
        {
            RepoRoot = repoRoot,
            SolutionCount = solutions.Count,
            IndexState = indexStatus.IndexState,
            WorkspaceState = indexStatus.WorkspaceState,
            Solutions = solutions
        };

        var summary = solutions.Count == 1
            ? "Repository has 1 discovered solution; workspace is cold."
            : $"Repository has {solutions.Count} discovered solutions; workspace is cold.";

        return ToolResponse<RepoOverviewItem>.Ok(
            summary,
            [item],
            detailLevel,
            indexStatus: indexStatus.IndexState == "fresh" ? "hit" : indexStatus.IndexState == "stale" ? "stale" : "miss",
            workspaceStatus: "cold");
    }

    public ToolResponse<SolutionSummary> ListSolutions(ToolScope? scope = null, string detailLevel = "normal")
    {
        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var status = coldIndexService.GetStatus(repoRoot);
        var indexedSolutions = coldIndexService.GetSolutions(repoRoot);
        var solutions = indexedSolutions.Count == 0
            ? solutionDiscoveryService.Discover(repoRoot, scope?.SolutionId)
            : indexedSolutions;

        var summary = solutions.Count == 1
            ? "Found 1 solution without loading MSBuildWorkspace."
            : $"Found {solutions.Count} solutions without loading MSBuildWorkspace.";

        return ToolResponse<SolutionSummary>.Ok(
            summary,
            solutions,
            detailLevel,
            indexStatus: status.IndexState == "fresh" ? "hit" : status.IndexState == "stale" ? "stale" : "miss",
            workspaceStatus: "cold");
    }
}
