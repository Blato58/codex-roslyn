using Codex.Roslyn.Core;

namespace Codex.Roslyn.Tests;

public sealed class RepoRootResolverTests
{
    [Fact]
    public void Resolve_ReturnsNearestGitRoot()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;

        var resolved = new RepoRootResolver().Resolve(startDirectory: nested);

        Assert.Equal(root, resolved);
    }

    [Fact]
    public void Resolve_UsesExistingDirectoryWhenNoGitRootExists()
    {
        var root = CreateTempDirectory();

        var resolved = new RepoRootResolver().Resolve(root);

        Assert.Equal(root, resolved);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
