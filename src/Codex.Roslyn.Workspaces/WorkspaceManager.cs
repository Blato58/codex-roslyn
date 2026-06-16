using Microsoft.CodeAnalysis.MSBuild;

namespace Codex.Roslyn.Workspaces;

public sealed class WorkspaceManager
{
    private readonly object sync = new();
    private readonly Dictionary<string, WorkspaceHandle> handles = new(StringComparer.Ordinal);
    private readonly int maxWarmSolutions;

    public WorkspaceManager() : this(maxWarmSolutions: 2)
    {
    }

    public WorkspaceManager(int maxWarmSolutions)
    {
        this.maxWarmSolutions = Math.Max(1, maxWarmSolutions);
    }

    public IReadOnlyList<WorkspaceHandle> Handles
    {
        get
        {
            lock (sync)
            {
                return handles.Values.OrderByDescending(handle => handle.LastAccessUtc).ToArray();
            }
        }
    }

    public async Task<WorkspaceLoadResult> LoadAsync(
        string solutionId,
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (handles.TryGetValue(solutionId, out var existing))
            {
                existing.Touch();
                return WorkspaceLoadResult.FromHandle(existing);
            }
        }

        try
        {
            MSBuildRegistration.RegisterDefaults();
            var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
            {
                ["Configuration"] = "Debug"
            });
            var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
            var handle = new WorkspaceHandle(solutionId, solutionPath, workspace, solution);

            lock (sync)
            {
                handles[solutionId] = handle;
                EvictIfNeeded();
            }

            return WorkspaceLoadResult.FromHandle(handle);
        }
        catch (Exception ex)
        {
            return WorkspaceLoadResult.FromFailure(CreateFailure(solutionPath, ex));
        }
    }

    public void MarkAllStale()
    {
        lock (sync)
        {
            foreach (var handle in handles.Values)
            {
                handle.Dispose();
            }

            handles.Clear();
        }
    }

    private void EvictIfNeeded()
    {
        while (handles.Count > maxWarmSolutions)
        {
            var evicted = handles.Values.OrderBy(handle => handle.LastAccessUtc).First();
            handles.Remove(evicted.SolutionId);
            evicted.Dispose();
        }
    }

    private static WorkspaceLoadFailure CreateFailure(string solutionPath, Exception ex)
    {
        var reason = ex.Message;
        var suggestedCommand = $"dotnet restore \"{solutionPath}\"";

        if (reason.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("assets file", StringComparison.OrdinalIgnoreCase))
        {
            reason = "missing_assets_file";
        }

        return new WorkspaceLoadFailure(
            "workspace_load_failed",
            reason,
            solutionPath,
            suggestedCommand,
            SafeToRunAutomatically: false);
    }
}
