using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Core;
using Codex.Roslyn.Index;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Roslyn.Tests;

public sealed class ColdIndexIntegrationTests
{
    [Fact]
    public void Build_CreatesSqliteIndexAndSearchesBeforeWorkspaceLoad()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out var cacheRoot);

        var build = services.GetRequiredService<ColdIndexService>().Build(repo);
        var overview = services.GetRequiredService<RepoOverviewService>().GetOverview(new ToolScope { RepoRoot = repo });
        var search = services.GetRequiredService<SymbolSearchService>().Search("CustomerService", scope: new ToolScope { RepoRoot = repo });
        var outline = services.GetRequiredService<DocumentOutlineService>().GetOutline("src/CustomerService.cs", new ToolScope { RepoRoot = repo });

        Assert.True(File.Exists(build.CachePath));
        Assert.StartsWith(cacheRoot, build.CachePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, build.SolutionCount);
        Assert.Equal("hit", overview.CacheStatus.Index);
        Assert.Contains(search.Items, item => item.Name == "CustomerService" && item.Confidence == "syntax_only");
        Assert.Contains(outline.Items, item => item.Name == "GetAsync" && item.Kind == "method");
    }

    [Fact]
    public void Status_BecomesStaleWhenIndexedFileChanges()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out _);
        var index = services.GetRequiredService<ColdIndexService>();
        index.Build(repo);

        File.AppendAllText(Path.Combine(repo, "src", "CustomerService.cs"), Environment.NewLine + "public class Added { }");

        var status = index.GetStatus(repo);

        Assert.Equal("stale", status.IndexState);
    }

    private static ServiceProvider CreateServices(out string cacheRoot)
    {
        cacheRoot = CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddCodexRoslynPhaseZero();
        services.AddSingleton(new IndexPathProvider(cacheRoot));
        return services.BuildServiceProvider();
    }

    private static string CreateSampleRepo()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "App.slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "Other"));
        File.WriteAllText(Path.Combine(root, "Other", "Other.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(
            Path.Combine(root, "src", "CustomerService.cs"),
            """
            namespace Demo;

            public class CustomerService
            {
                public string Name { get; set; } = "";
                public void GetAsync(int id) { }
            }
            """);
        File.WriteAllText(Path.Combine(root, "src", "Ignored.generated.cs"), "public class Ignored { }");
        return root;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
