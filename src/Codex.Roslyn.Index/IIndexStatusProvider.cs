using Codex.Roslyn.Abstractions.Dtos;

namespace Codex.Roslyn.Index;

public interface IIndexStatusProvider
{
    IndexStatusSummary GetStatus(string repoRoot);
}
