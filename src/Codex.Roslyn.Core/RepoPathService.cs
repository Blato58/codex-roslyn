namespace Codex.Roslyn.Core;

public sealed class RepoPathService
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".idea",
        ".vscode",
        "node_modules",
        "packages",
        "TestResults",
        "coverage"
    };

    public string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public bool TryNormalizeRepoRelativePath(string repoRoot, string path, out string relativePath, out string error)
    {
        relativePath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "File path is required.";
            return false;
        }

        var repoFullPath = Path.GetFullPath(repoRoot);
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repoFullPath, path.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsInsideRepository(repoFullPath, fullPath))
        {
            error = $"File '{path}' is outside repository root '{repoFullPath}'.";
            return false;
        }

        relativePath = NormalizePath(Path.GetRelativePath(repoFullPath, fullPath));
        return true;
    }

    public bool TryNormalizeDocumentPath(string repoRoot, string? path, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path)
            || !TryNormalizeRepoRelativePath(repoRoot, path, out relativePath, out _)
            || IsExcludedRelativePath(relativePath)
            || IsGeneratedPath(relativePath))
        {
            return false;
        }

        return true;
    }

    public bool IsExcludedRelativePath(string relativePath)
    {
        var segments = NormalizePath(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => ExcludedDirectoryNames.Contains(segment));
    }

    public bool IsGeneratedPath(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsPathInsideRepository(string repoRoot, string path)
    {
        return IsInsideRepository(Path.GetFullPath(repoRoot), Path.GetFullPath(path));
    }

    private static bool IsInsideRepository(string repoFullPath, string fullPath)
    {
        var repoPrefix = repoFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? repoFullPath
            : repoFullPath + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, repoFullPath, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
