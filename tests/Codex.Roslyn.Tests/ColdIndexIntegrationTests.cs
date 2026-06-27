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
        var absoluteOutline = services.GetRequiredService<DocumentOutlineService>().GetOutline(Path.Combine(repo, "src", "CustomerService.cs"), new ToolScope { RepoRoot = repo });

        Assert.True(File.Exists(build.CachePath));
        Assert.StartsWith(cacheRoot, build.CachePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, build.SolutionCount);
        Assert.Equal("hit", overview.CacheStatus.Index);
        Assert.Contains(search.Items, item => item.Name == "CustomerService" && item.Confidence == "syntax_only");
        Assert.Contains(outline.Items, item => item.Name == "GetAsync" && item.Kind == "method");
        Assert.Contains(absoluteOutline.Items, item => item.Name == "GetAsync" && item.Kind == "method");
    }

    [Fact]
    public void IndexBuildService_BuildsColdIndexResponse()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out var cacheRoot);

        var response = services.GetRequiredService<IndexBuildService>()
            .Build(new ToolScope { RepoRoot = repo });

        var build = Assert.Single(response.Items);
        Assert.Equal("ok", response.ResultKind);
        Assert.Equal("hit", response.CacheStatus.Index);
        Assert.True(File.Exists(build.CachePath));
        Assert.StartsWith(cacheRoot, build.CachePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fresh", build.IndexState);
    }

    [Fact]
    public void SymbolSearch_AutoBuildsMissingIndexAndReturnsResults()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out _);

        var search = services.GetRequiredService<SymbolSearchService>()
            .Search("CustomerService", scope: new ToolScope { RepoRoot = repo });
        var status = services.GetRequiredService<ColdIndexService>().GetStatus(repo);

        Assert.Equal("ok", search.ResultKind);
        Assert.Equal("hit", search.CacheStatus.Index);
        Assert.Contains(search.Warnings, warning => warning.Contains("rebuilt automatically", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(search.Items, item => item.Name == "CustomerService" && item.Confidence == "syntax_only");
        Assert.Equal("fresh", status.IndexState);
    }

    [Fact]
    public void DocumentOutline_AutoBuildsMissingIndexAndReturnsDeclarations()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out _);

        var outline = services.GetRequiredService<DocumentOutlineService>()
            .GetOutline("src/CustomerService.cs", new ToolScope { RepoRoot = repo });
        var status = services.GetRequiredService<ColdIndexService>().GetStatus(repo);

        Assert.Equal("ok", outline.ResultKind);
        Assert.Equal("hit", outline.CacheStatus.Index);
        Assert.Contains(outline.Warnings, warning => warning.Contains("rebuilt automatically", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(outline.Items, item => item.Name == "GetAsync" && item.Kind == "method");
        Assert.Equal("fresh", status.IndexState);
    }

    [Fact]
    public void DocumentOutline_ReturnsFileNotFoundForOutsideRepoAbsolutePath()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var outsideFile = Path.Combine(CreateTempDirectory(), "CustomerService.cs");
        File.WriteAllText(outsideFile, "public class CustomerService { }");

        var outline = services.GetRequiredService<DocumentOutlineService>().GetOutline(outsideFile, new ToolScope { RepoRoot = repo });

        Assert.Equal("file_not_found", outline.ResultKind);
        Assert.Empty(outline.Items);
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

    [Fact]
    public void SymbolSearch_AutoRebuildsStaleIndexBeforeSearching()
    {
        var repo = CreateSampleRepo();
        using var services = CreateServices(out _);
        var index = services.GetRequiredService<ColdIndexService>();
        index.Build(repo);
        File.AppendAllText(Path.Combine(repo, "src", "CustomerService.cs"), Environment.NewLine + "public class Added { }");

        var search = services.GetRequiredService<SymbolSearchService>()
            .Search("Added", scope: new ToolScope { RepoRoot = repo });
        var status = index.GetStatus(repo);

        Assert.Equal("ok", search.ResultKind);
        Assert.Equal("hit", search.CacheStatus.Index);
        Assert.Contains(search.Warnings, warning => warning.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(search.Items, item => item.Name == "Added" && item.Confidence == "syntax_only");
        Assert.Equal("fresh", status.IndexState);
    }

    private static ServiceProvider CreateServices(out string cacheRoot)
    {
        cacheRoot = CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddCodexRoslynServices();
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
