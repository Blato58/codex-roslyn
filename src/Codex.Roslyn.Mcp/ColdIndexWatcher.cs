using Codex.Roslyn.Core;
using Codex.Roslyn.Index;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Codex.Roslyn.Mcp;

public sealed class ColdIndexWatcher(
    RepoRootResolver repoRootResolver,
    ColdIndexService coldIndexService,
    ILogger<ColdIndexWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var repoRoot = repoRootResolver.Resolve();
        using var watcher = new FileSystemWatcher(repoRoot)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, args) => MarkDirty(repoRoot, args.FullPath);
        watcher.Created += (_, args) => MarkDirty(repoRoot, args.FullPath);
        watcher.Deleted += (_, args) => MarkDirty(repoRoot, args.FullPath);
        watcher.Renamed += (_, args) => MarkDirty(repoRoot, args.FullPath);
        watcher.Error += (_, args) =>
        {
            logger.LogWarning(args.GetException(), "File watcher overflow or error; marking cold index dirty.");
            coldIndexService.MarkDirty(repoRoot);
        };

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void MarkDirty(string repoRoot, string path)
    {
        if (!ShouldInvalidate(path))
        {
            return;
        }

        coldIndexService.MarkDirty(repoRoot);
    }

    private static bool ShouldInvalidate(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/packages/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/TestResults/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/coverage/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        var fileName = Path.GetFileName(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ruleset", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("global.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase);
    }
}
