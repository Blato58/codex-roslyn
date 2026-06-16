using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;
using Codex.Roslyn.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Codex.Roslyn.Core;

public sealed class ImpactAnalysisService(
    SolutionSelectionService solutionSelectionService,
    WorkspaceManager workspaceManager,
    ColdIndexService coldIndexService,
    SemanticSymbolCache symbolCache)
{
    public async Task<ToolResponse<ChangeImpactResult>> ChangeImpactAsync(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? changedFiles = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(symbolIds, changedFiles, scope, detailLevel, maxItems, cancellationToken);
        if (analysis.ResponseKind is not null)
        {
            return Empty<ChangeImpactResult>(analysis.ResponseKind, analysis.Summary, detailLevel, analysis.Warning);
        }

        var items = analysis.ProjectImpacts
            .Take(Math.Clamp(maxItems, 1, 500))
            .Select(project => new ChangeImpactResult
            {
                Area = project.ProjectFile ?? project.ProjectName,
                Kind = project.IsTestProject ? "test" : "source",
                Project = project.ProjectName,
                ProjectFile = project.ProjectFile,
                Confidence = project.Reasons.Any(reason => reason.Contains("semantic", StringComparison.OrdinalIgnoreCase))
                    ? "high"
                    : "medium",
                Reasons = project.Reasons,
                Files = project.Files,
                SuggestedCommands = SuggestedCommands(project)
            })
            .ToArray();

        return ToolResponse<ChangeImpactResult>.Ok(
            $"Identified {items.Length} impacted projects across {analysis.ImpactedFiles.Count} files.",
            items,
            detailLevel,
            "hit",
            "warm");
    }

    public async Task<ToolResponse<TestImpactResult>> TestImpactAsync(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? changedFiles = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(symbolIds, changedFiles, scope, detailLevel, maxItems, cancellationToken);
        if (analysis.ResponseKind is not null)
        {
            return new ToolResponse<TestImpactResult>
            {
                ResultKind = analysis.ResponseKind,
                Summary = analysis.Summary,
                Items = [],
                CacheStatus = new CacheStatus { Index = "hit", Workspace = analysis.ResponseKind == "workspace_load_failed" ? "faulted" : "cold" },
                TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 80 },
                Warnings = string.IsNullOrWhiteSpace(analysis.Warning) ? [] : [analysis.Warning]
            };
        }

        var sourceProjectIds = analysis.ProjectImpacts
            .Where(project => !project.IsTestProject)
            .Select(project => project.ProjectId)
            .ToHashSet();
        var items = new List<TestImpactResult>();
        foreach (var project in analysis.Projects.Where(project => IsTestProject(project, analysis.RepoRoot)))
        {
            var projectFile = ProjectFile(analysis.RepoRoot, project);
            var directTestFiles = ProjectFiles(project, analysis.RepoRoot)
                .Where(file => analysis.ImpactedFiles.Contains(file))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var referencesImpactedSource = project.ProjectReferences.Any(reference => sourceProjectIds.Contains(reference.ProjectId));
            var proximity = sourceProjectIds
                .Select(id => analysis.Projects.FirstOrDefault(candidate => candidate.Id == id))
                .Where(candidate => candidate is not null)
                .Cast<Project>()
                .Any(sourceProject => LooksLikeTestFor(project, sourceProject));

            var score = 0;
            var reasons = new List<string>();
            if (directTestFiles.Length > 0)
            {
                score += 60;
                reasons.Add("Impacted files include tests in this project.");
            }

            if (referencesImpactedSource)
            {
                score += 40;
                reasons.Add("Test project references an impacted source project.");
            }

            if (proximity)
            {
                score += 20;
                reasons.Add("Test project name or path is near an impacted source project.");
            }

            if (score == 0)
            {
                continue;
            }

            var testClassNames = await FindTestClassNamesAsync(project, analysis.RepoRoot, directTestFiles, cancellationToken);
            items.Add(new TestImpactResult
            {
                TestArea = projectFile ?? project.Name,
                Project = project.Name,
                ProjectFile = projectFile,
                Command = BuildDotnetTestCommand(projectFile, testClassNames),
                Confidence = score >= 60 ? "high" : "medium",
                Reasons = reasons,
                Files = directTestFiles.Length > 0 ? directTestFiles : ProjectFiles(project, analysis.RepoRoot).Take(10).ToArray()
            });
        }

        if (items.Count == 0 && analysis.ProjectImpacts.Any(project => !project.IsTestProject))
        {
            var sourceProjects = analysis.ProjectImpacts
                .Where(project => !project.IsTestProject)
                .Select(project => analysis.Projects.FirstOrDefault(candidate => candidate.Id == project.ProjectId))
                .Where(project => project is not null)
                .Cast<Project>()
                .ToArray();
            foreach (var project in analysis.Projects.Where(project => IsTestProject(project, analysis.RepoRoot)))
            {
                if (sourceProjects.Length > 0 && !sourceProjects.Any(sourceProject => LooksLikeTestFor(project, sourceProject)))
                {
                    continue;
                }

                var projectFile = ProjectFile(analysis.RepoRoot, project);
                items.Add(new TestImpactResult
                {
                    TestArea = projectFile ?? project.Name,
                    Project = project.Name,
                    ProjectFile = projectFile,
                    Command = BuildDotnetTestCommand(projectFile, []),
                    Confidence = "medium",
                    Reasons = ["Test project is near impacted source projects; no direct test file reference was found."],
                    Files = ProjectFiles(project, analysis.RepoRoot).Take(10).ToArray()
                });
            }
        }

        if (items.Count == 0 && analysis.ProjectImpacts.Count > 0)
        {
            var commands = analysis.ProjectImpacts
                .Where(project => !project.IsTestProject)
                .Select(project => project.ProjectFile)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => $"dotnet build \"{path}\"")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();
            items.Add(new TestImpactResult
            {
                TestArea = "targeted_project_validation",
                Confidence = "medium",
                Reasons = ["No test project was found; build impacted projects and run adjacent tests manually."],
                Files = analysis.ImpactedFiles.Take(Math.Clamp(maxItems, 1, 500)).ToArray(),
                Command = commands.FirstOrDefault()
            });
        }

        var page = items
            .OrderByDescending(item => item.Confidence == "high")
            .ThenBy(item => item.ProjectFile ?? item.TestArea, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxItems, 1, 500))
            .ToArray();

        return ToolResponse<TestImpactResult>.Ok(
            $"Recommended {page.Length} test areas.",
            page,
            detailLevel,
            "hit",
            "warm");
    }

    public async Task<IReadOnlyList<string>> ValidationCommandsAsync(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? changedFiles = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 5,
        CancellationToken cancellationToken = default)
    {
        var impact = await TestImpactAsync(symbolIds, changedFiles, scope, detailLevel, maxItems, cancellationToken);
        if (impact.ResultKind != "ok")
        {
            return [];
        }

        return impact.Items
            .Select(item => item.Command)
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxItems, 1, 20))
            .Cast<string>()
            .ToArray();
    }

    private async Task<ImpactAnalysis> AnalyzeAsync(
        IReadOnlyList<string>? symbolIds,
        IReadOnlyList<string>? changedFiles,
        ToolScope? scope,
        string detailLevel,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return ImpactAnalysis.Failed(loaded.ResponseKind, loaded.Summary, loaded.Warning);
        }

        var impactedFiles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileReasons = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var changedFile in changedFiles ?? [])
        {
            AddFile(NormalizePath(changedFile), "File was supplied as changed input.");
        }

        foreach (var symbolId in symbolIds ?? [])
        {
            if (!symbolCache.TryGet(symbolId, out var symbol))
            {
                continue;
            }

            var symbolDisplay = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
            {
                if (!string.IsNullOrWhiteSpace(syntaxReference.SyntaxTree.FilePath))
                {
                    AddFile(RelativePath(loaded.RepoRoot, syntaxReference.SyntaxTree.FilePath), $"Declaration for semantic symbol {symbolDisplay}.");
                }
            }

            foreach (var location in symbol.Locations.Where(location => location.IsInSource))
            {
                AddLocation(location, $"Declaration for semantic symbol {symbolDisplay}.");
            }

            var references = await SymbolFinder.FindReferencesAsync(symbol, loaded.Handle!.Solution, cancellationToken);
            foreach (var referencedSymbol in references)
            {
                foreach (var location in referencedSymbol.Definition.Locations.Where(location => location.IsInSource))
                {
                    AddLocation(location, $"Declaration for semantic symbol {symbolDisplay}.");
                }

                foreach (var reference in referencedSymbol.Locations)
                {
                    if (reference.Location is { IsInSource: true } location)
                    {
                        AddLocation(location, $"Semantic reference to {symbolDisplay}.");
                    }
                }
            }
        }

        var projectImpacts = loaded.Handle!.Solution.Projects
            .Select(project => BuildProjectImpact(project, loaded.RepoRoot, impactedFiles, fileReasons))
            .Where(project => project.Files.Count > 0)
            .OrderBy(project => project.ProjectFile ?? project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxItems, 1, 500))
            .ToArray();

        return new ImpactAnalysis(
            loaded.RepoRoot,
            loaded.Handle.Solution.Projects.ToArray(),
            impactedFiles,
            projectImpacts,
            null,
            string.Empty,
            null);

        void AddLocation(Location location, string reason)
        {
            var path = location.SourceTree?.FilePath ?? location.GetLineSpan().Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                AddFile(RelativePath(loaded.RepoRoot, path), reason);
            }
        }

        void AddFile(string file, string reason)
        {
            var normalized = NormalizePath(file);
            impactedFiles.Add(normalized);
            if (!fileReasons.TryGetValue(normalized, out var reasons))
            {
                reasons = [];
                fileReasons[normalized] = reasons;
            }

            if (!reasons.Contains(reason, StringComparer.Ordinal))
            {
                reasons.Add(reason);
            }
        }
    }

    private static ProjectImpact BuildProjectImpact(
        Project project,
        string repoRoot,
        IReadOnlySet<string> impactedFiles,
        IReadOnlyDictionary<string, List<string>> fileReasons)
    {
        var files = ProjectFiles(project, repoRoot)
            .Where(impactedFiles.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reasons = files
            .SelectMany(file => fileReasons.TryGetValue(file, out var fileReason) ? fileReason : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ProjectImpact(
            project.Id,
            project.Name,
            ProjectFile(repoRoot, project),
            IsTestProject(project, repoRoot),
            files,
            reasons.Length > 0 ? reasons : ["Changed files were mapped to this project."]);
    }

    private async Task<SemanticLoadContext> LoadAsync(ToolScope? scope, string detailLevel, CancellationToken cancellationToken)
    {
        var resolution = solutionSelectionService.Resolve(scope);
        if (resolution.ResultKind != "ok")
        {
            return new SemanticLoadContext(resolution.RepoRoot, null, null, resolution.ResultKind, resolution.Summary, null);
        }

        var solutionPath = Path.Combine(resolution.RepoRoot, resolution.Solution!.Path.Replace('/', Path.DirectorySeparatorChar));
        var status = coldIndexService.GetStatus(resolution.RepoRoot);
        if (status.IndexState == "stale")
        {
            workspaceManager.MarkAllStale();
        }

        var loaded = await workspaceManager.LoadAsync(resolution.Solution.SolutionId, solutionPath, cancellationToken);
        if (!loaded.Success)
        {
            var failure = loaded.Failure!;
            return new SemanticLoadContext(
                resolution.RepoRoot,
                resolution.Solution,
                null,
                failure.State,
                $"Workspace load failed: {failure.Reason}.",
                failure.SuggestedCommand);
        }

        return new SemanticLoadContext(resolution.RepoRoot, resolution.Solution, loaded.Handle, null, string.Empty, null);
    }

    private static IReadOnlyList<string> SuggestedCommands(ProjectImpact project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectFile))
        {
            return [];
        }

        var command = project.IsTestProject
            ? $"dotnet test \"{project.ProjectFile}\""
            : $"dotnet build \"{project.ProjectFile}\"";
        return [command];
    }

    private static async Task<IReadOnlyList<string>> FindTestClassNamesAsync(
        Project project,
        string repoRoot,
        IReadOnlyList<string> directTestFiles,
        CancellationToken cancellationToken)
    {
        if (directTestFiles.Count == 0)
        {
            return [];
        }

        var classes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var document in project.Documents)
        {
            if (document.FilePath is null)
            {
                continue;
            }

            var relative = RelativePath(repoRoot, document.FilePath);
            if (!directTestFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root is null)
            {
                continue;
            }

            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                classes.Add(declaration.Identifier.ValueText);
            }
        }

        return classes.Take(3).ToArray();
    }

    private static string? BuildDotnetTestCommand(string? projectFile, IReadOnlyList<string> testClassNames)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return null;
        }

        var command = $"dotnet test \"{projectFile}\"";
        return testClassNames.Count == 1
            ? command + $" --filter FullyQualifiedName~{testClassNames[0]}"
            : command;
    }

    private static bool IsTestProject(Project project, string repoRoot)
    {
        if (project.Name.Contains("test", StringComparison.OrdinalIgnoreCase)
            || ProjectFile(repoRoot, project)?.Contains("test", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (project.FilePath is null || !File.Exists(project.FilePath))
        {
            return false;
        }

        var projectFile = File.ReadAllText(project.FilePath);
        return projectFile.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
            || projectFile.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTestFor(Project testProject, Project sourceProject)
    {
        var testName = NormalizeTestName(testProject.Name);
        var sourceName = NormalizeTestName(sourceProject.Name);
        if (testName.Contains(sourceName, StringComparison.OrdinalIgnoreCase)
            || sourceName.Contains(testName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return testProject.FilePath is not null
            && sourceProject.FilePath is not null
            && Path.GetDirectoryName(testProject.FilePath)?.Contains(Path.GetFileNameWithoutExtension(sourceProject.FilePath), StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<string> ProjectFiles(Project project, string repoRoot)
    {
        return project.Documents
            .Select(document => document.FilePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Select(path => RelativePath(repoRoot, path));
    }

    private static string NormalizeTestName(string value)
    {
        return value
            .Replace(".Tests", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".Test", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Tests", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Test", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolResponse<TItem> Empty<TItem>(string resultKind, string summary, string detailLevel, string? warning = null)
    {
        return new ToolResponse<TItem>
        {
            ResultKind = resultKind,
            Summary = summary,
            Items = [],
            CacheStatus = new CacheStatus { Index = "hit", Workspace = resultKind == "workspace_load_failed" ? "faulted" : "cold" },
            TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 80 },
            Warnings = string.IsNullOrWhiteSpace(warning) ? [] : [warning]
        };
    }

    private static string? ProjectFile(string repoRoot, Project project)
    {
        return project.FilePath is null ? null : RelativePath(repoRoot, project.FilePath);
    }

    private static string RelativePath(string repoRoot, string path)
    {
        return NormalizePath(Path.GetRelativePath(repoRoot, path));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private sealed record ImpactAnalysis(
        string RepoRoot,
        IReadOnlyList<Project> Projects,
        IReadOnlySet<string> ImpactedFiles,
        IReadOnlyList<ProjectImpact> ProjectImpacts,
        string? ResponseKind,
        string Summary,
        string? Warning)
    {
        public static ImpactAnalysis Failed(string responseKind, string summary, string? warning)
        {
            return new ImpactAnalysis(string.Empty, [], new HashSet<string>(), [], responseKind, summary, warning);
        }
    }

    private sealed record ProjectImpact(
        ProjectId ProjectId,
        string ProjectName,
        string? ProjectFile,
        bool IsTestProject,
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Reasons);

    private sealed record SemanticLoadContext(
        string RepoRoot,
        SolutionSummary? Solution,
        WorkspaceHandle? Handle,
        string? ResponseKind,
        string Summary,
        string? Warning);
}
