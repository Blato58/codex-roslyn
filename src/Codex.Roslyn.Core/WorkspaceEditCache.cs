using System.Security.Cryptography;
using System.Text;

namespace Codex.Roslyn.Core;

public sealed class WorkspaceEditCache
{
    private readonly object sync = new();
    private readonly Dictionary<string, CachedWorkspaceEdit> edits = new(StringComparer.Ordinal);

    public void Store(CachedWorkspaceEdit edit)
    {
        lock (sync)
        {
            edits[edit.EditId] = edit;
        }
    }

    public bool TryGet(string editId, out CachedWorkspaceEdit edit)
    {
        lock (sync)
        {
            return edits.TryGetValue(editId, out edit!);
        }
    }

    public void Remove(string editId)
    {
        lock (sync)
        {
            edits.Remove(editId);
        }
    }

    public static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record CachedWorkspaceEdit(
    string EditId,
    string RepoRoot,
    string SolutionId,
    string Operation,
    int DiagnosticsBefore,
    int DiagnosticsAfter,
    IReadOnlyList<CachedWorkspaceEditFile> Files);

public sealed record CachedWorkspaceEditFile(
    string RelativePath,
    string FullPath,
    string? OriginalHash,
    string NewText,
    int Edits);
