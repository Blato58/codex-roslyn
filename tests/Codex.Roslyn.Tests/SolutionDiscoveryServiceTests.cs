using Codex.Roslyn.Core;

namespace Codex.Roslyn.Tests;

public sealed class SolutionDiscoveryServiceTests
{
    [Fact]
    public void Discover_ReturnsSlnAndSlnxWithoutLoadingWorkspace()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "Nested"));
        File.WriteAllText(Path.Combine(root, "Nested", "Other.slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "bin", "Ignored.sln"), string.Empty);

        var service = new SolutionDiscoveryService(new RepoRootResolver());

        var solutions = service.Discover(root);

        Assert.Collection(
            solutions,
            solution => Assert.Equal("App.sln", solution.Path),
            solution => Assert.Equal("Nested/Other.slnx", solution.Path));
        Assert.All(solutions, solution => Assert.StartsWith("sln_", solution.SolutionId, StringComparison.Ordinal));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
