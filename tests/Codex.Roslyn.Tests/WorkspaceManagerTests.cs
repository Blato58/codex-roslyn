using Codex.Roslyn.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Codex.Roslyn.Tests;

public sealed class WorkspaceManagerTests
{
    [Fact]
    public void MarkAllStale_DisposesWarmHandles()
    {
        using var firstWorkspace = MSBuildWorkspace.Create();
        using var secondWorkspace = MSBuildWorkspace.Create();
        var manager = new WorkspaceManager(maxWarmSolutions: 2);
        var first = new WorkspaceHandle("sln_1", "one.sln", firstWorkspace, new AdhocWorkspace().CurrentSolution);
        var second = new WorkspaceHandle("sln_2", "two.sln", secondWorkspace, new AdhocWorkspace().CurrentSolution);

        var handlesField = typeof(WorkspaceManager).GetField("handles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var handles = (Dictionary<string, WorkspaceHandle>)handlesField.GetValue(manager)!;
        handles[first.SolutionId] = first;
        handles[second.SolutionId] = second;

        manager.MarkAllStale();

        Assert.Empty(manager.Handles);
        Assert.Equal("disposed", first.State);
        Assert.Equal("disposed", second.State);
    }

    [Fact]
    public async Task LoadAsync_ReturnsWorkspaceLoadFailureForMissingSolution()
    {
        var manager = new WorkspaceManager();

        var result = await manager.LoadAsync("sln_missing", Path.Combine(Path.GetTempPath(), "missing.sln"));

        Assert.False(result.Success);
        Assert.Equal("workspace_load_failed", result.Failure!.State);
        Assert.False(result.Failure.SafeToRunAutomatically);
        Assert.Contains("dotnet restore", result.Failure.SuggestedCommand, StringComparison.Ordinal);
    }
}
