using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;

namespace Codex.Roslyn.Core;

public sealed class SolutionSelectionService(
    RepoRootResolver repoRootResolver,
    SolutionDiscoveryService solutionDiscoveryService,
    ColdIndexService coldIndexService)
{
    private readonly object sync = new();
    private string? activeSolutionId;

    public ToolResponse<SolutionSelectResult> Select(string solutionId, ToolScope? scope = null, string detailLevel = "normal")
    {
        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var solution = GetSolutions(repoRoot).FirstOrDefault(item => item.SolutionId == solutionId);
        if (solution is null)
        {
            return new ToolResponse<SolutionSelectResult>
            {
                ResultKind = "error",
                Summary = $"Solution '{solutionId}' was not found.",
                CacheStatus = new CacheStatus { Index = "miss", Workspace = "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 60 }
            };
        }

        lock (sync)
        {
            activeSolutionId = solutionId;
        }

        return ToolResponse<SolutionSelectResult>.Ok(
            $"Selected solution {solution.Path}.",
            [new SolutionSelectResult { SolutionId = solution.SolutionId, Path = solution.Path, State = "selected" }],
            detailLevel,
            indexStatus: "hit",
            workspaceStatus: "cold");
    }

    public SolutionResolution Resolve(ToolScope? scope = null)
    {
        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var solutions = GetSolutions(repoRoot);
        var requestedSolutionId = scope?.SolutionId;

        if (string.IsNullOrWhiteSpace(requestedSolutionId))
        {
            lock (sync)
            {
                requestedSolutionId = activeSolutionId;
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedSolutionId))
        {
            var selected = solutions.FirstOrDefault(solution => solution.SolutionId == requestedSolutionId);
            return selected is null
                ? SolutionResolution.Error(repoRoot, $"Solution '{requestedSolutionId}' was not found.")
                : SolutionResolution.Selected(repoRoot, selected);
        }

        if (solutions.Count == 1)
        {
            return SolutionResolution.Selected(repoRoot, solutions[0]);
        }

        return SolutionResolution.Ambiguous(repoRoot, solutions);
    }

    private IReadOnlyList<SolutionSummary> GetSolutions(string repoRoot)
    {
        var indexed = coldIndexService.GetSolutions(repoRoot);
        return indexed.Count == 0 ? solutionDiscoveryService.Discover(repoRoot) : indexed;
    }
}

public sealed record SolutionResolution(
    string RepoRoot,
    SolutionSummary? Solution,
    IReadOnlyList<SolutionSummary> Candidates,
    string ResultKind,
    string Summary)
{
    public static SolutionResolution Selected(string repoRoot, SolutionSummary solution)
    {
        return new SolutionResolution(repoRoot, solution, [solution], "ok", $"Selected solution {solution.Path}.");
    }

    public static SolutionResolution Ambiguous(string repoRoot, IReadOnlyList<SolutionSummary> candidates)
    {
        return new SolutionResolution(repoRoot, null, candidates, "ambiguous_solution", "Multiple solutions are available. Select one with cs_solution_select or pass scope.solutionId.");
    }

    public static SolutionResolution Error(string repoRoot, string summary)
    {
        return new SolutionResolution(repoRoot, null, [], "error", summary);
    }
}
