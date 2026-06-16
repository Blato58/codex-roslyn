using System.Security.Cryptography;
using System.Text;
using Codex.Roslyn.Abstractions.Dtos;

namespace Codex.Roslyn.Core;

public sealed class SolutionDiscoveryService(RepoRootResolver repoRootResolver)
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        ".vscode",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "TestResults",
        "coverage"
    };

    public IReadOnlyList<SolutionSummary> Discover(string? repoRoot = null, string? activeSolutionId = null)
    {
        var root = repoRootResolver.Resolve(repoRoot);

        return EnumerateSolutionFiles(root)
            .Select(path => CreateSummary(root, path, activeSolutionId))
            .OrderBy(solution => solution.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SolutionSummary CreateSummary(string repoRoot, string solutionPath, string? activeSolutionId)
    {
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(repoRoot, solutionPath));
        var solutionId = CreateSolutionId(relativePath);

        return new SolutionSummary
        {
            SolutionId = solutionId,
            Path = relativePath,
            DisplayName = Path.GetFileNameWithoutExtension(solutionPath),
            IsActive = string.Equals(solutionId, activeSolutionId, StringComparison.Ordinal)
        };
    }

    private static IEnumerable<string> EnumerateSolutionFiles(string repoRoot)
    {
        var pending = new Stack<string>();
        pending.Push(repoRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsSolutionFile)
                    .ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => !ExcludedDirectoryNames.Contains(Path.GetFileName(path)))
                    .ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
            }
        }
    }

    private static bool IsSolutionFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSolutionId(string relativePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return "sln_" + Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
