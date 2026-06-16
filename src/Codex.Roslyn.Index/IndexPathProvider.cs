namespace Codex.Roslyn.Index;

public sealed class IndexPathProvider(string? cacheRootOverride = null)
{
    public string GetIndexPath(RepoIdentity identity)
    {
        var root = cacheRootOverride ?? GetDefaultCacheRoot();
        return Path.Combine(root, "indexes", identity.RepoId, "index.db");
    }

    private static string GetDefaultCacheRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "CodexRoslyn");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Caches",
                "CodexRoslyn");
        }

        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgCacheHome))
        {
            return Path.Combine(xdgCacheHome, "codex-roslyn");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "codex-roslyn");
    }
}
