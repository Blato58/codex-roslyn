using Codex.Roslyn.Abstractions.Dtos;

namespace Codex.Roslyn.Index;

public sealed class PhaseZeroIndexStatusProvider : IIndexStatusProvider
{
    public IndexStatusSummary GetStatus(string repoRoot)
    {
        return new IndexStatusSummary
        {
            RepoRoot = repoRoot,
            IndexState = "uninitialized",
            WorkspaceState = "cold",
            FilesIndexed = 0,
            SymbolsIndexed = 0
        };
    }
}
