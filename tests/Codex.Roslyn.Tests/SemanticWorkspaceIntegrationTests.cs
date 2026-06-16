using System.Diagnostics;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Core;
using Codex.Roslyn.Index;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Roslyn.Tests;

public sealed class SemanticWorkspaceIntegrationTests
{
    [Fact]
    public async Task SemanticTools_ResolveReferencesHierarchyCallersAndDiagnostics()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var selector = services.GetRequiredService<SolutionSelectionService>();
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        selector.Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");

        var classPosition = FindPosition(sourcePath, "CustomerService");
        var classSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", classPosition.Line, classPosition.Column, new ToolScope { RepoRoot = repo });
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });
        var references = await semantic.FindReferencesAsync(methodSymbol.Items.Single().SymbolId, new ToolScope { RepoRoot = repo });
        var hierarchy = await semantic.TypeHierarchyAsync(classSymbol.Items.Single().SymbolId, new ToolScope { RepoRoot = repo });
        var callers = await semantic.CallersAsync(methodSymbol.Items.Single().SymbolId, new ToolScope { RepoRoot = repo });
        var diagnostics = await semantic.DiagnosticsAsync(new ToolScope { RepoRoot = repo }, severityAtLeast: "error");

        Assert.Equal("ok", classSymbol.ResultKind);
        Assert.Equal("class", classSymbol.Items.Single().Kind);
        Assert.Contains(references.Items, item => item.ReferenceKind == "declaration");
        Assert.Contains(references.Items, item => item.ReferenceKind == "reference");
        Assert.Contains(hierarchy.Items, item => item.RelationKind == "interface" && item.DisplayName.Contains("IService", StringComparison.Ordinal));
        Assert.Contains(callers.Items, item => item.CallerDisplayName.Contains("Use", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Items, item => item.Id == "CS0029");
    }

    [Fact]
    public async Task RefactorPreview_RenameReturnsCompactDiffWithoutMutatingFiles()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });

        var preview = await refactors.PreviewAsync(
            "rename",
            methodSymbol.Items.Single().SymbolId,
            "GetCustomerAsync",
            scope: new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", preview.ResultKind);
        Assert.Equal("rename", preview.Items.Single().Operation);
        Assert.True(preview.Items.Single().ChangedSpans >= 2);
        Assert.Contains("GetAsync -> GetCustomerAsync", preview.Items.Single().DiffPreview);
        Assert.Contains("GetAsync", File.ReadAllText(sourcePath));
        Assert.DoesNotContain("GetCustomerAsync", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task ImpactTools_ReturnConfidenceAndReasonsFromWarmSymbol()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });

        var impact = await refactors.ChangeImpactAsync([methodSymbol.Items.Single().SymbolId], scope: new ToolScope { RepoRoot = repo });
        var testImpact = await refactors.TestImpactAsync(changedFiles: ["src/SampleLib/CustomerService.cs"], scope: new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", impact.ResultKind);
        Assert.Contains(impact.Items, item => item.Confidence == "high" && item.Reasons.Count > 0);
        Assert.Equal("ok", testImpact.ResultKind);
        Assert.Contains(testImpact.Items, item => item.Confidence == "medium" && item.Reasons.Count > 0);
    }

    [Fact]
    public async Task RefactorPreview_MoveTypeToFileReturnsCompactDiffWithoutMutatingFiles()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var classPosition = FindPosition(sourcePath, "CustomerService");
        var classSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", classPosition.Line, classPosition.Column, new ToolScope { RepoRoot = repo });

        var preview = await refactors.PreviewAsync(
            "move_type_to_file",
            classSymbol.Items.Single().SymbolId,
            file: "src/SampleLib/CustomerService.Moved.cs",
            scope: new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", preview.ResultKind);
        Assert.Equal("move_type_to_file", preview.Items.Single().Operation);
        Assert.Equal(2, preview.Items.Single().ChangedFiles);
        Assert.True(preview.Items.Single().ChangedSpans >= 2);
        Assert.Contains("src/SampleLib/CustomerService.Moved.cs", preview.Items.Single().DiffPreview);
        Assert.Contains("CustomerService", File.ReadAllText(sourcePath));
        Assert.False(File.Exists(Path.Combine(repo, "src", "SampleLib", "CustomerService.Moved.cs")));
    }

    [Fact]
    public async Task ApplyWorkspaceEdit_AppliesCachedRenameAndRejectsReuse()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });
        var preview = await refactors.PreviewAsync("rename", methodSymbol.Items.Single().SymbolId, "GetCustomerAsync", scope: new ToolScope { RepoRoot = repo });

        var applied = await refactors.ApplyWorkspaceEditAsync(preview.Items.Single().EditId, new ToolScope { RepoRoot = repo });
        var reused = await refactors.ApplyWorkspaceEditAsync(preview.Items.Single().EditId, new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", applied.ResultKind);
        Assert.Contains("GetCustomerAsync", File.ReadAllText(sourcePath));
        Assert.Equal("unknown_edit", reused.ResultKind);
    }

    [Fact]
    public async Task ApplyWorkspaceEdit_RejectsStalePreview()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });
        var preview = await refactors.PreviewAsync("rename", methodSymbol.Items.Single().SymbolId, "GetCustomerAsync", scope: new ToolScope { RepoRoot = repo });
        File.AppendAllText(sourcePath, Environment.NewLine + "// changed after preview");

        var applied = await refactors.ApplyWorkspaceEditAsync(preview.Items.Single().EditId, new ToolScope { RepoRoot = repo });

        Assert.Equal("stale_edit", applied.ResultKind);
        Assert.DoesNotContain("GetCustomerAsync", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task RefactorPreview_ChangeNamespaceAndExtractInterfaceReturnApplyablePreviews()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var classPosition = FindPosition(sourcePath, "CustomerService");
        var classSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", classPosition.Line, classPosition.Column, new ToolScope { RepoRoot = repo });

        var namespacePreview = await refactors.PreviewAsync("change_namespace", classSymbol.Items.Single().SymbolId, "Demo.Renamed", scope: new ToolScope { RepoRoot = repo });
        var interfacePreview = await refactors.PreviewAsync("extract_interface", classSymbol.Items.Single().SymbolId, "ICustomerService", "src/SampleLib/ICustomerService.cs", new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", namespacePreview.ResultKind);
        Assert.Contains("Demo -> Demo.Renamed", namespacePreview.Items.Single().DiffPreview);
        Assert.Equal("ok", interfacePreview.ResultKind);
        Assert.Contains("ICustomerService.cs", interfacePreview.Items.Single().DiffPreview);
        Assert.True(interfacePreview.Items.Single().RequiresApproval);
    }

    [Fact]
    public async Task RefactorPreview_ChangeNamespaceUpdatesContainingNamespaceAndReferences()
    {
        var repo = CreateSemanticRepo();
        File.WriteAllText(
            Path.Combine(repo, "src", "SampleLib", "MultiNamespace.cs"),
            """
            namespace Alpha
            {
                public class Unrelated
                {
                }
            }

            namespace Demo
            {
                public class MultiTarget
                {
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(repo, "src", "SampleLib", "Consumer.cs"),
            """
            using Demo;

            namespace Consumers;

            public class Consumer
            {
                private MultiTarget target = new();

                public Demo.MultiTarget Create() => target;
            }
            """);
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var targetPath = Path.Combine(repo, "src", "SampleLib", "MultiNamespace.cs");
        var targetPosition = FindPosition(targetPath, "MultiTarget");
        var targetSymbol = await semantic.SymbolAtAsync("src/SampleLib/MultiNamespace.cs", targetPosition.Line, targetPosition.Column, new ToolScope { RepoRoot = repo });

        var preview = await refactors.PreviewAsync("change_namespace", targetSymbol.Items.Single().SymbolId, "Demo.Renamed", scope: new ToolScope { RepoRoot = repo });
        var applied = await refactors.ApplyWorkspaceEditAsync(preview.Items.Single().EditId, new ToolScope { RepoRoot = repo });
        var targetText = File.ReadAllText(targetPath);
        var consumerText = File.ReadAllText(Path.Combine(repo, "src", "SampleLib", "Consumer.cs"));

        Assert.Equal("ok", preview.ResultKind);
        Assert.Equal("ok", applied.ResultKind);
        Assert.Contains("namespace Alpha", targetText);
        Assert.Contains("namespace Demo.Renamed", targetText);
        Assert.Contains("using Demo.Renamed;", consumerText);
        Assert.Contains("Demo.Renamed.MultiTarget", consumerText);
    }

    [Fact]
    public async Task RefactorPreview_RejectsExistingDestinationFiles()
    {
        var repo = CreateSemanticRepo();
        var destinationPath = Path.Combine(repo, "src", "SampleLib", "Existing.cs");
        File.WriteAllText(destinationPath, "namespace Demo;\n");
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var classPosition = FindPosition(sourcePath, "CustomerService");
        var classSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", classPosition.Line, classPosition.Column, new ToolScope { RepoRoot = repo });

        var movePreview = await refactors.PreviewAsync("move_type_to_file", classSymbol.Items.Single().SymbolId, file: "src/SampleLib/Existing.cs", scope: new ToolScope { RepoRoot = repo });
        var interfacePreview = await refactors.PreviewAsync("extract_interface", classSymbol.Items.Single().SymbolId, "ICustomerService", "src/SampleLib/Existing.cs", new ToolScope { RepoRoot = repo });

        Assert.Equal("invalid_request", movePreview.ResultKind);
        Assert.Equal("invalid_request", interfacePreview.ResultKind);
    }

    [Fact]
    public async Task AdvancedTools_DeadCodeCandidatesIncludesPrivateFields()
    {
        var repo = CreateSemanticRepo();
        File.WriteAllText(
            Path.Combine(repo, "src", "SampleLib", "FieldHolder.cs"),
            """
            namespace Demo;

            public class FieldHolder
            {
                private int unusedField;
                private int usedField;

                public int ReadUsed() => usedField;
            }
            """);
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var advanced = services.GetRequiredService<AdvancedSemanticService>();

        var candidates = await advanced.DeadCodeCandidatesAsync(new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", candidates.ResultKind);
        Assert.Contains(candidates.Items, item => item.DisplayName.Contains("unusedField", StringComparison.Ordinal));
        Assert.DoesNotContain(candidates.Items, item => item.DisplayName.EndsWith(".usedField", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdvancedTools_ReturnCompactContextAndDiagnosticsSummary()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var advanced = services.GetRequiredService<AdvancedSemanticService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });

        var context = await advanced.ContextPackAsync([methodSymbol.Items.Single().SymbolId], scope: new ToolScope { RepoRoot = repo });
        var summary = await advanced.DiagnosticsSummaryAsync(new ToolScope { RepoRoot = repo }, severityAtLeast: "error");
        var flow = await advanced.DataFlowAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });

        Assert.Equal("ok", context.ResultKind);
        Assert.Contains(context.Items.Single().PrimarySymbols, item => item.Contains("GetAsync", StringComparison.Ordinal));
        Assert.Equal("ok", summary.ResultKind);
        Assert.True(summary.Items.Single().ErrorCount >= 1);
        Assert.Equal("ok", flow.ResultKind);
    }

    [Fact]
    public async Task RefactorPreview_MoveTypeToFileValidatesInputs()
    {
        var repo = CreateSemanticRepo();
        using var services = CreateServices(out _);
        services.GetRequiredService<ColdIndexService>().Build(repo);
        var solutionId = services.GetRequiredService<SolutionDiscoveryService>().Discover(repo).Single().SolutionId;
        services.GetRequiredService<SolutionSelectionService>().Select(solutionId, new ToolScope { RepoRoot = repo });
        var semantic = services.GetRequiredService<SemanticQueryService>();
        var refactors = services.GetRequiredService<RefactorPreviewService>();
        var sourcePath = Path.Combine(repo, "src", "SampleLib", "CustomerService.cs");
        var classPosition = FindPosition(sourcePath, "CustomerService");
        var classSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", classPosition.Line, classPosition.Column, new ToolScope { RepoRoot = repo });
        var methodPosition = FindPosition(sourcePath, "GetAsync");
        var methodSymbol = await semantic.SymbolAtAsync("src/SampleLib/CustomerService.cs", methodPosition.Line, methodPosition.Column, new ToolScope { RepoRoot = repo });

        var missingSymbol = await refactors.PreviewAsync("move_type_to_file", file: "src/SampleLib/Moved.cs", scope: new ToolScope { RepoRoot = repo });
        var missingFile = await refactors.PreviewAsync("move_type_to_file", classSymbol.Items.Single().SymbolId, scope: new ToolScope { RepoRoot = repo });
        var nonType = await refactors.PreviewAsync("move_type_to_file", methodSymbol.Items.Single().SymbolId, file: "src/SampleLib/Moved.cs", scope: new ToolScope { RepoRoot = repo });
        var sameFile = await refactors.PreviewAsync("move_type_to_file", classSymbol.Items.Single().SymbolId, file: "src/SampleLib/CustomerService.cs", scope: new ToolScope { RepoRoot = repo });

        Assert.Equal("stale_symbol", missingSymbol.ResultKind);
        Assert.Equal("invalid_request", missingFile.ResultKind);
        Assert.Equal("invalid_request", nonType.ResultKind);
        Assert.Equal("invalid_request", sameFile.ResultKind);
    }

    [Fact]
    public async Task SymbolAt_ReturnsAmbiguousSolutionWhenNoSolutionIsSelected()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "A.sln"), string.Empty);
        File.WriteAllText(Path.Combine(root, "B.sln"), string.Empty);
        File.WriteAllText(Path.Combine(root, "File.cs"), "public class File { }");
        using var services = CreateServices(out _);

        var result = await services.GetRequiredService<SemanticQueryService>()
            .SymbolAtAsync("File.cs", 1, 14, new ToolScope { RepoRoot = root });

        Assert.Equal("ambiguous_solution", result.ResultKind);
    }

    private static ServiceProvider CreateServices(out string cacheRoot)
    {
        cacheRoot = CreateTempDirectory();
        var services = new ServiceCollection();
        services.AddCodexRoslynServices();
        services.AddSingleton(new IndexPathProvider(cacheRoot));
        return services.BuildServiceProvider();
    }

    private static string CreateSemanticRepo()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        RunDotnet(root, "new sln --format sln --name Sample");
        RunDotnet(root, "new classlib --framework net10.0 --name SampleLib --output src/SampleLib");
        RunDotnet(root, "sln Sample.sln add src/SampleLib/SampleLib.csproj");

        File.WriteAllText(
            Path.Combine(root, "src", "SampleLib", "CustomerService.cs"),
            """
            namespace Demo;

            public interface IService
            {
                void Run();
            }

            public class BaseService
            {
                public virtual void BaseCall() { }
            }

            public class CustomerService : BaseService, IService
            {
                public string Name { get; set; } = "";

                public void Run() { }

                public string GetAsync(int id) => id.ToString();

                public void Use()
                {
                    _ = GetAsync(1);
                }
            }

            public class Broken
            {
                public void Fail()
                {
                    string value = 1;
                }
            }
            """);
        File.Delete(Path.Combine(root, "src", "SampleLib", "Class1.cs"));
        RunDotnet(root, "restore Sample.sln");
        return root;
    }

    private static (int Line, int Column) FindPosition(string path, string text)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var index = lines[i].IndexOf(text, StringComparison.Ordinal);
            if (index >= 0)
            {
                return (i + 1, index + 1);
            }
        }

        throw new InvalidOperationException($"Could not find '{text}' in {path}.");
    }

    private static void RunDotnet(string workingDirectory, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start dotnet.");
        process.WaitForExit(60000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
