namespace Codex.Roslyn.Workspaces;

public sealed record WorkspaceLoadResult(WorkspaceHandle? Handle, WorkspaceLoadFailure? Failure)
{
    public bool Success => Handle is not null;

    public static WorkspaceLoadResult FromHandle(WorkspaceHandle handle)
    {
        return new WorkspaceLoadResult(handle, null);
    }

    public static WorkspaceLoadResult FromFailure(WorkspaceLoadFailure failure)
    {
        return new WorkspaceLoadResult(null, failure);
    }
}
