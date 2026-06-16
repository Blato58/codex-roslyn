using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Core;
using Codex.Roslyn.Index;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Roslyn.Tests;

public sealed class SolutionSelectionServiceTests
{
    [Fact]
    public void Resolve_ReturnsAmbiguousWhenMultipleSolutionsExistWithoutSelection()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "A.sln"), string.Empty);
        File.WriteAllText(Path.Combine(root, "B.sln"), string.Empty);
        using var services = CreateServices(out _);

        var resolution = services.GetRequiredService<SolutionSelectionService>().Resolve(new ToolScope { RepoRoot = root });

        Assert.Equal("ambiguous_solution", resolution.ResultKind);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public void Select_SetsActiveSolutionForLaterResolution()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "A.sln"), string.Empty);
        File.WriteAllText(Path.Combine(root, "B.sln"), string.Empty);
        using var services = CreateServices(out _);
        var selector = services.GetRequiredService<SolutionSelectionService>();
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>()
            .Discover(root)
            .First(solution => solution.Path == "B.sln")
            .SolutionId;

        selector.Select(solutionId, new ToolScope { RepoRoot = root });
        var resolution = selector.Resolve(new ToolScope { RepoRoot = root });

        Assert.Equal("ok", resolution.ResultKind);
        Assert.Equal("B.sln", resolution.Solution!.Path);
    }

    private static ServiceProvider CreateServices(out string cacheRoot)
    {
        cacheRoot = CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddCodexRoslynPhaseZero();
        services.AddSingleton(new IndexPathProvider(cacheRoot));
        return services.BuildServiceProvider();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
