namespace Codex.Roslyn.Index;

public sealed class RepoScanner(FileHasher fileHasher)
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

    private static readonly HashSet<string> ProjectAndConfigFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "NuGet.Config",
        "packages.lock.json"
    };

    public RepoScanResult Scan(string repoRoot, bool includeGenerated = false)
    {
        var solutions = new List<string>();
        var sourceFiles = new List<ScannedFile>();
        var projectAndConfig = new List<string>();

        foreach (var file in EnumerateFiles(repoRoot))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(repoRoot, file));
            var extension = Path.GetExtension(file);
            var fileName = Path.GetFileName(file);

            if (IsSolutionFile(extension))
            {
                solutions.Add(relativePath);
                projectAndConfig.Add(relativePath);
                continue;
            }

            if (IsProjectOrConfigFile(extension, fileName))
            {
                projectAndConfig.Add(relativePath);
            }

            if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isGenerated = IsGeneratedFile(file);
            if (isGenerated && !includeGenerated)
            {
                continue;
            }

            sourceFiles.Add(new ScannedFile(
                file,
                relativePath,
                extension,
                isGenerated,
                fileHasher.Hash(file)));
        }

        return new RepoScanResult(
            solutions.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceFiles.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            projectAndConfig.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static bool IsGeneratedFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string repoRoot)
    {
        var pending = new Stack<string>();
        pending.Push(repoRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => !ExcludedDirectoryNames.Contains(Path.GetFileName(path)))
                    .ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in directories)
            {
                pending.Push(child);
            }
        }
    }

    private static bool IsSolutionFile(string extension)
    {
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectOrConfigFile(string extension, string fileName)
    {
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ruleset", StringComparison.OrdinalIgnoreCase)
            || ProjectAndConfigFileNames.Contains(fileName);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
