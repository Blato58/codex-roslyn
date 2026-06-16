using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Codex.Roslyn.Workspaces;

public sealed class WorkspaceHandle : IDisposable
{
    public WorkspaceHandle(
        string solutionId,
        string solutionPath,
        MSBuildWorkspace workspace,
        Solution solution)
    {
        SolutionId = solutionId;
        SolutionPath = solutionPath;
        Workspace = workspace;
        Solution = solution;
        LoadedUtc = DateTimeOffset.UtcNow;
        LastAccessUtc = LoadedUtc;
        ProjectCount = solution.ProjectIds.Count;
        DocumentCount = solution.Projects.Sum(project => project.DocumentIds.Count);
    }

    public string SolutionId { get; }

    public string SolutionPath { get; }

    public MSBuildWorkspace Workspace { get; }

    public Solution Solution { get; private set; }

    public DateTimeOffset LoadedUtc { get; }

    public DateTimeOffset LastAccessUtc { get; private set; }

    public int ProjectCount { get; }

    public int DocumentCount { get; }

    public string State { get; private set; } = "ready";

    public void Touch()
    {
        LastAccessUtc = DateTimeOffset.UtcNow;
    }

    public void MarkFaulted()
    {
        State = "faulted";
    }

    public void Dispose()
    {
        State = "disposed";
        Workspace.Dispose();
    }
}
