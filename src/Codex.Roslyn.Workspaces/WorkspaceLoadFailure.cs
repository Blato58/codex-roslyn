namespace Codex.Roslyn.Workspaces;

public sealed record WorkspaceLoadFailure(
    string State,
    string Reason,
    string SolutionPath,
    string SuggestedCommand,
    bool SafeToRunAutomatically);
