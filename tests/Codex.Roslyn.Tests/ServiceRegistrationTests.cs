using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Core;
using Codex.Roslyn.Index;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Roslyn.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void RepoOverview_ReturnsDiscoveredSolutionsAndColdWorkspace()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "CodexRoslyn.slnx"), string.Empty);
        var services = CreateServices();

        var overview = services.GetRequiredService<RepoOverviewService>()
            .GetOverview(new ToolScope { RepoRoot = root });

        var item = Assert.Single(overview.Items);
        Assert.Equal(root, item.RepoRoot);
        Assert.Equal(1, item.SolutionCount);
        Assert.Equal("cold", item.WorkspaceState);
        Assert.Equal("miss", overview.CacheStatus.Index);
    }

    [Fact]
    public void IndexStatus_DoesNotCreateIndexFiles()
    {
        var root = CreateTempDirectory();
        var services = CreateServices();

        var status = services.GetRequiredService<IndexStatusService>().GetStatus(root);

        var item = Assert.Single(status.Items);
        Assert.Equal("missing", item.IndexState);
        Assert.Empty(Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories));
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddCodexRoslynServices();
        services.AddSingleton(new IndexPathProvider(CreateTempDirectory()));
        return services.BuildServiceProvider();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
