using Codex.Roslyn.Abstractions.Dtos;

namespace Codex.Roslyn.Index;

public sealed class BasicIndexStatusProvider : IIndexStatusProvider
{
    public IndexStatusSummary GetStatus(string repoRoot)
    {
        return new IndexStatusSummary { RepoRoot = repoRoot };
    }
}
