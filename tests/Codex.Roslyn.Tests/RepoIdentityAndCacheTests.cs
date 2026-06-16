using Codex.Roslyn.Index;
using Codex.Roslyn.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Roslyn.Tests;

public sealed class RepoIdentityAndCacheTests
{
    [Fact]
    public void RepoIdentity_IsStableForSameRoot()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var service = new RepoIdentityService();

        var first = service.Create(root);
        var second = service.Create(root);

        Assert.Equal(first.RepoId, second.RepoId);
        Assert.StartsWith("repo_", first.RepoId, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexPath_UsesCacheRootAndRepoId()
    {
        var cacheRoot = CreateTempDirectory();
        var identity = new RepoIdentity("C:\\repo", "repo_abc", null);

        var path = new IndexPathProvider(cacheRoot).GetIndexPath(identity);

        Assert.Equal(Path.Combine(cacheRoot, "indexes", "repo_abc", "index.db"), path);
    }

    [Fact]
    public void Clear_RemovesRepoIndexDirectoryOnly()
    {
        var repoRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        var cacheRoot = CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddCodexRoslynPhaseZero();
        services.AddSingleton(new IndexPathProvider(cacheRoot));
        using var provider = services.BuildServiceProvider();
        var indexService = provider.GetRequiredService<ColdIndexService>();
        var build = indexService.Build(repoRoot);
        var cacheDirectory = Path.GetDirectoryName(build.CachePath)!;
        File.WriteAllText(Path.Combine(cacheDirectory, "extra.tmp"), "cache");

        var clearedDirectory = indexService.Clear(repoRoot);

        Assert.Equal(cacheDirectory, clearedDirectory);
        Assert.False(Directory.Exists(cacheDirectory));
        Assert.True(Directory.Exists(Path.Combine(cacheRoot, "indexes")));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
