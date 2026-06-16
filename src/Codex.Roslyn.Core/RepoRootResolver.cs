namespace Codex.Roslyn.Core;

public sealed class RepoRootResolver
{
    public string Resolve(string? explicitRepoRoot = null, string? startDirectory = null)
    {
        var candidate = explicitRepoRoot;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = startDirectory;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Directory.GetCurrentDirectory();
        }

        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath)
                ?? throw new DirectoryNotFoundException($"Could not resolve directory for '{candidate}'.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Repository root '{fullPath}' does not exist.");
        }

        var current = new DirectoryInfo(fullPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return fullPath;
    }
}
