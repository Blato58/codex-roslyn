using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;

namespace Codex.Roslyn.Core;

public sealed class DocumentOutlineService(
    RepoRootResolver repoRootResolver,
    RepoPathService repoPathService,
    ColdIndexService coldIndexService)
{
    public ToolResponse<DocumentOutlineItem> GetOutline(
        string file,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return new ToolResponse<DocumentOutlineItem>
            {
                ResultKind = "error",
                Summary = "File is required.",
                CacheStatus = new CacheStatus { Index = "miss", Workspace = "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 40 }
            };
        }

        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var status = coldIndexService.GetStatus(repoRoot);
        if (status.IndexState == "missing")
        {
            return new ToolResponse<DocumentOutlineItem>
            {
                ResultKind = "stale_index",
                Summary = "Cold index is missing. Run 'dotnet-roslyn-mcp index --repo <path>' first.",
                CacheStatus = new CacheStatus { Index = "miss", Workspace = "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 60 },
                Warnings = ["Document outline requires the cold SQLite index."]
            };
        }

        if (!repoPathService.TryNormalizeRepoRelativePath(repoRoot, file, out var normalizedFile, out var pathError))
        {
            return new ToolResponse<DocumentOutlineItem>
            {
                ResultKind = "file_not_found",
                Summary = pathError,
                CacheStatus = new CacheStatus { Index = status.IndexState == "fresh" ? "hit" : "stale", Workspace = "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 60 }
            };
        }

        if (!coldIndexService.ContainsFile(repoRoot, normalizedFile))
        {
            return new ToolResponse<DocumentOutlineItem>
            {
                ResultKind = "file_not_found",
                Summary = $"File '{normalizedFile}' was not found in the cold index for repository '{repoRoot}'.",
                CacheStatus = new CacheStatus { Index = status.IndexState == "fresh" ? "hit" : "stale", Workspace = "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 60 }
            };
        }

        var results = coldIndexService.GetDocumentOutline(repoRoot, normalizedFile, maxItems);
        var summary = results.Count == 1 ? "Found 1 syntax declaration." : $"Found {results.Count} syntax declarations.";

        return ToolResponse<DocumentOutlineItem>.Ok(
            summary,
            results,
            detailLevel,
            indexStatus: status.IndexState == "fresh" ? "hit" : "stale",
            workspaceStatus: "cold");
    }
}
