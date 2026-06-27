using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;

namespace Codex.Roslyn.Core;

public sealed class IndexBuildService(RepoRootResolver repoRootResolver, ColdIndexService coldIndexService)
{
    public ToolResponse<IndexBuildSummary> Build(
        ToolScope? scope = null,
        string detailLevel = "normal",
        bool includeGenerated = false)
    {
        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var result = coldIndexService.Build(repoRoot, includeGenerated);

        return ToolResponse<IndexBuildSummary>.Ok(
            $"Indexed {result.FilesIndexed} files and {result.DeclarationsIndexed} declarations.",
            [result],
            detailLevel,
            indexStatus: "hit",
            workspaceStatus: "cold");
    }

    public IndexRecoveryResult EnsureFresh(string repoRoot, bool includeGenerated = false)
    {
        var status = coldIndexService.GetStatus(repoRoot);
        if (status.IndexState == "fresh")
        {
            return new IndexRecoveryResult(status, null);
        }

        var previousState = status.IndexState;
        var result = coldIndexService.Build(repoRoot, includeGenerated);
        var refreshedStatus = coldIndexService.GetStatus(repoRoot);
        var warning = $"Cold index was {previousState} and was rebuilt automatically ({result.FilesIndexed} files, {result.DeclarationsIndexed} declarations).";

        return new IndexRecoveryResult(refreshedStatus, warning);
    }
}

public sealed record IndexRecoveryResult(IndexStatusSummary Status, string? Warning);
