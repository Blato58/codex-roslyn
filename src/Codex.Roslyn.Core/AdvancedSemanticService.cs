using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;
using Codex.Roslyn.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Codex.Roslyn.Core;

public sealed class AdvancedSemanticService(
    RepoRootResolver repoRootResolver,
    SolutionSelectionService solutionSelectionService,
    WorkspaceManager workspaceManager,
    ColdIndexService coldIndexService,
    SemanticSymbolCache symbolCache,
    SemanticSymbolIdService symbolIdService,
    ImpactAnalysisService impactAnalysisService,
    RepoPathService repoPathService)
{
    public async Task<ToolResponse<ContextPackResult>> ContextPackAsync(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? files = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 20,
        int maxTokens = 2500,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<ContextPackResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var primarySymbols = new List<string>();
        var referenceSummary = new List<string>();
        var selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbolId in symbolIds ?? [])
        {
            if (!symbolCache.TryGet(symbolId, out var symbol))
            {
                continue;
            }

            primarySymbols.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
            var sourcePath = sourceLocation?.SourceTree?.FilePath ?? sourceLocation?.GetLineSpan().Path;
            if (repoPathService.TryNormalizeDocumentPath(loaded.RepoRoot, sourcePath, out var sourceRelativePath))
            {
                selectedFiles.Add(sourceRelativePath);
            }

            foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
            {
                if (repoPathService.TryNormalizeDocumentPath(loaded.RepoRoot, syntaxReference.SyntaxTree.FilePath, out var declarationPath))
                {
                    selectedFiles.Add(declarationPath);
                }
            }

            var references = await SymbolFinder.FindReferencesAsync(symbol, loaded.Handle!.Solution, cancellationToken);
            var filesForSymbol = ReferenceFiles(loaded.Handle.Solution, loaded.RepoRoot, references)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(maxItems, 1, 100))
                .ToArray();
            foreach (var file in filesForSymbol)
            {
                selectedFiles.Add(file);
            }

            referenceSummary.Add($"{symbol.Name}: {filesForSymbol.Length} files");
        }

        foreach (var file in files ?? [])
        {
            if (repoPathService.TryNormalizeDocumentPath(loaded.RepoRoot, file, out var normalizedFile))
            {
                selectedFiles.Add(normalizedFile);
            }
        }

        var testSummary = selectedFiles
            .Where(file => file.Contains("test", StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();
        var filePage = selectedFiles.Take(Math.Clamp(maxItems, 1, 100)).ToArray();
        var testImpact = await impactAnalysisService.TestImpactAsync(
            changedFiles: filePage,
            scope: scope,
            detailLevel: detailLevel,
            maxItems: 50,
            cancellationToken: cancellationToken);
        var validationCommands = testImpact.ResultKind == "ok"
            ? testImpact.Items
                .Select(item => item.Command)
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Cast<string>()
                .ToArray()
            : [];
        var diagnostics = await DiagnosticsCoreAsync(loaded.Handle!.Solution, loaded.RepoRoot, null, "warning", Math.Clamp(maxItems, 1, 100), cancellationToken);
        var diagnosticsSummary = diagnostics
            .GroupBy(diagnostic => diagnostic.Severity, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToArray();
        var recommendedNextTools = RecommendedNextTools(symbolIds, files, validationCommands);
        var estimatedTokens = Math.Min(
            maxTokens,
            200
            + (primarySymbols.Count * 40)
            + (filePage.Length * 20)
            + (diagnosticsSummary.Length * 20)
            + (validationCommands.Length * 20));

        return ToolResponse<ContextPackResult>.Ok(
            $"Built context pack with {primarySymbols.Count} symbols and {filePage.Length} files.",
            [new ContextPackResult
            {
                ActiveSolution = loaded.Solution!.Path,
                PrimarySymbols = primarySymbols.Take(Math.Clamp(maxItems, 1, 100)).ToArray(),
                ReferenceSummary = referenceSummary.Take(Math.Clamp(maxItems, 1, 100)).ToArray(),
                TestSummary = testSummary,
                DiagnosticSummary = diagnosticsSummary,
                SelectedFiles = filePage,
                ValidationCommands = validationCommands,
                RecommendedNextTools = recommendedNextTools,
                EstimatedTokens = estimatedTokens,
                Truncated = selectedFiles.Count > filePage.Length || estimatedTokens >= maxTokens
            }],
            detailLevel,
            "hit",
            "warm");
    }

    public async Task<ToolResponse<DiagnosticsSummaryResult>> DiagnosticsSummaryAsync(
        ToolScope? scope = null,
        string? path = null,
        string severityAtLeast = "warning",
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<DiagnosticsSummaryResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var diagnostics = await DiagnosticsCoreAsync(loaded.Handle!.Solution, loaded.RepoRoot, path, severityAtLeast, Math.Clamp(maxItems, 1, 1000), cancellationToken);
        var result = new DiagnosticsSummaryResult
        {
            Scope = string.IsNullOrWhiteSpace(path)
                ? loaded.Solution!.Path
                : repoPathService.TryNormalizeRepoRelativePath(loaded.RepoRoot, path, out var normalizedPath, out _) ? normalizedPath : path,
            ErrorCount = diagnostics.Count(diagnostic => diagnostic.Severity == "error"),
            WarningCount = diagnostics.Count(diagnostic => diagnostic.Severity == "warning"),
            InfoCount = diagnostics.Count(diagnostic => diagnostic.Severity is "info" or "hidden"),
            CountsByProject = diagnostics
                .GroupBy(diagnostic => FirstSegment(diagnostic.File ?? "project"))
                .Select(group => $"{group.Key}: {group.Count()}")
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray(),
            CountsByFile = diagnostics
                .Where(diagnostic => diagnostic.File is not null)
                .GroupBy(diagnostic => diagnostic.File!)
                .Select(group => $"{group.Key}: {group.Count()}")
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray(),
            TopDiagnostics = diagnostics.Take(Math.Clamp(maxItems, 1, 100)).ToArray()
        };

        return ToolResponse<DiagnosticsSummaryResult>.Ok(
            $"Found {diagnostics.Count} diagnostics at or above {severityAtLeast}.",
            [result],
            detailLevel,
            "hit",
            "warm");
    }

    public async Task<ToolResponse<CallGraphResult>> FullCallGraphAsync(
        string symbolId,
        ToolScope? scope = null,
        string direction = "callers",
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolId) || !symbolCache.TryGet(symbolId, out var symbol))
        {
            return Empty<CallGraphResult>("stale_symbol", "Call graph requires a warm symbol. Use cs_symbol_at first.", detailLevel);
        }

        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<CallGraphResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        if (!direction.Equals("callers", StringComparison.OrdinalIgnoreCase))
        {
            return Empty<CallGraphResult>("unsupported_operation", "Full call graph currently supports direction='callers'.", detailLevel);
        }

        var callers = await SymbolFinder.FindCallersAsync(symbol, loaded.Handle!.Solution, cancellationToken);
        var items = callers
            .Take(Math.Clamp(maxItems, 1, 500))
            .Select(caller =>
            {
                var callerId = symbolIdService.CreateSymbolId(loaded.RepoRoot, loaded.Solution!.SolutionId, caller.CallingSymbol);
                symbolCache.Store(callerId, caller.CallingSymbol);
                return new CallGraphResult
                {
                    SymbolId = callerId,
                    DisplayName = caller.CallingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    Direction = "caller",
                    Locations = caller.Locations.Select(location => symbolIdService.CreateLocation(loaded.RepoRoot, location)).Where(location => location is not null).Cast<SemanticLocation>().ToArray()
                };
            })
            .ToArray();

        return ToolResponse<CallGraphResult>.Ok($"Found {items.Length} caller graph entries.", items, detailLevel, "hit", "warm");
    }

    public Task<ToolResponse<FlowAnalysisResult>> DataFlowAsync(string file, int line, int column, ToolScope? scope = null, string detailLevel = "normal", int maxItems = 50, CancellationToken cancellationToken = default)
    {
        return FlowAsync(file, line, column, "data_flow", scope, detailLevel, maxItems, cancellationToken);
    }

    public Task<ToolResponse<FlowAnalysisResult>> ControlFlowAsync(string file, int line, int column, ToolScope? scope = null, string detailLevel = "normal", int maxItems = 50, CancellationToken cancellationToken = default)
    {
        return FlowAsync(file, line, column, "control_flow", scope, detailLevel, maxItems, cancellationToken);
    }

    public Task<ToolResponse<FlowAnalysisResult>> OperationTreeAsync(string file, int line, int column, ToolScope? scope = null, string detailLevel = "normal", int maxItems = 50, CancellationToken cancellationToken = default)
    {
        return FlowAsync(file, line, column, "operation_tree", scope, detailLevel, maxItems, cancellationToken);
    }

    public async Task<ToolResponse<SemanticDiagnosticResult>> RunAnalyzersAsync(
        ToolScope? scope = null,
        string? path = null,
        string severityAtLeast = "warning",
        string detailLevel = "normal",
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<SemanticDiagnosticResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var diagnostics = await DiagnosticsCoreAsync(loaded.Handle!.Solution, loaded.RepoRoot, path, severityAtLeast, Math.Clamp(maxItems, 1, 1000), cancellationToken);
        return new ToolResponse<SemanticDiagnosticResult>
        {
            ResultKind = "partial",
            Summary = "Analyzer execution is disabled by default; returned compiler diagnostics only.",
            Items = diagnostics.ToArray(),
            CacheStatus = new CacheStatus { Index = "hit", Workspace = "warm" },
            TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 120 + diagnostics.Count * 40, Truncated = diagnostics.Count >= maxItems },
            Warnings = ["Configured analyzer execution is intentionally opt-in and not run by this default implementation."]
        };
    }

    public async Task<ToolResponse<CodeFixPreviewResult>> CodeFixPreviewAsync(
        string? diagnosticId = null,
        string? file = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<CodeFixPreviewResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var diagnostics = await DiagnosticsCoreAsync(loaded.Handle!.Solution, loaded.RepoRoot, file, "warning", Math.Clamp(maxItems, 1, 100), cancellationToken);
        var items = diagnostics
            .Where(diagnostic => string.IsNullOrWhiteSpace(diagnosticId) || diagnostic.Id.Equals(diagnosticId, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(maxItems, 1, 100))
            .Select(diagnostic => new CodeFixPreviewResult
            {
                DiagnosticId = diagnostic.Id,
                File = diagnostic.File ?? string.Empty,
                Line = diagnostic.Line ?? 0,
                Title = $"Review fix for {diagnostic.Id}",
                Confidence = "low",
                Preview = diagnostic.Message,
                RequiresApproval = true
            })
            .ToArray();

        return new ToolResponse<CodeFixPreviewResult>
        {
            ResultKind = "partial",
            Summary = $"Found {items.Length} diagnostic candidates only; no applyable code-fix workspace edits were generated.",
            Items = items,
            CacheStatus = new CacheStatus { Index = "hit", Workspace = "warm" },
            TokenPolicy = new TokenPolicy { DetailLevel = detailLevel, EstimatedTokens = 120 + items.Length * 40, Truncated = items.Length >= maxItems },
            Warnings = ["Code fix preview returns diagnostic candidates only; use cs_refactor_preview for applyable edits."]
        };
    }

    public async Task<ToolResponse<PublicApiDiffResult>> PublicApiDiffAsync(ToolScope? scope = null, string detailLevel = "normal", int maxItems = 100, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<PublicApiDiffResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var items = new List<PublicApiDiffResult>();
        foreach (var project in loaded.Handle!.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            var symbols = PublicSymbols(compilation.Assembly.GlobalNamespace)
                .Take(Math.Clamp(maxItems, 1, 500))
                .ToArray();
            items.Add(new PublicApiDiffResult
            {
                Project = project.Name,
                PublicSymbols = symbols,
                Warnings = ["Baseline comparison is not configured; this is the current public API inventory."]
            });
        }

        return ToolResponse<PublicApiDiffResult>.Ok(
            $"Collected current public API inventory for {items.Count} projects; no baseline diff was configured.",
            items,
            detailLevel,
            "hit",
            "warm");
    }

    public async Task<ToolResponse<DeadCodeCandidateResult>> DeadCodeCandidatesAsync(ToolScope? scope = null, string detailLevel = "normal", int maxItems = 50, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<DeadCodeCandidateResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var candidates = new List<DeadCodeCandidateResult>();
        foreach (var document in loaded.Handle!.Solution.Projects.SelectMany(project => project.Documents))
        {
            if (!repoPathService.TryNormalizeDocumentPath(loaded.RepoRoot, document.FilePath, out var documentPath) || candidates.Count >= maxItems)
            {
                continue;
            }

            var model = await document.GetSemanticModelAsync(cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (model is null || root is null)
            {
                continue;
            }

            var declarations = root.DescendantNodes()
                .Where(node => node is MethodDeclarationSyntax or PropertyDeclarationSyntax or FieldDeclarationSyntax)
                .Take(200);
            foreach (var declaration in declarations)
            {
                if (candidates.Count >= maxItems)
                {
                    break;
                }

                foreach (var symbol in GetPrivateMemberSymbols(model, declaration, cancellationToken))
                {
                    if (candidates.Count >= maxItems)
                    {
                        break;
                    }

                    var references = await SymbolFinder.FindReferencesAsync(symbol, loaded.Handle.Solution, cancellationToken);
                    if (references.SelectMany(reference => reference.Locations).Any())
                    {
                        continue;
                    }

                    var location = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.GetLineSpan();
                    if (location is null)
                    {
                        continue;
                    }

                    candidates.Add(new DeadCodeCandidateResult
                    {
                        SymbolId = symbolIdService.CreateSymbolId(loaded.RepoRoot, loaded.Solution!.SolutionId, symbol),
                        DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        File = documentPath,
                        Line = location.Value.StartLinePosition.Line + 1,
                        Confidence = "low",
                        Reasons = ["Private symbol has no semantic references in the selected solution."]
                    });
                }
            }
        }

        return ToolResponse<DeadCodeCandidateResult>.Ok($"Found {candidates.Count} dead code candidates.", candidates.ToArray(), detailLevel, "hit", "warm");
    }

    public ToolResponse<SymbolSearchResult> GeneratedCodeSearch(string query, ToolScope? scope = null, string detailLevel = "normal", int maxItems = 50)
    {
        var repoRoot = repoRootResolver.Resolve(scope?.RepoRoot);
        var files = Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => repoPathService.IsGeneratedPath(path))
            .Where(path => repoPathService.TryNormalizeRepoRelativePath(repoRoot, path, out var relative, out _)
                && !repoPathService.IsExcludedRelativePath(relative))
            .Select(path =>
            {
                repoPathService.TryNormalizeRepoRelativePath(repoRoot, path, out var relative, out _);
                return relative;
            })
            .Where(path => string.IsNullOrWhiteSpace(query) || path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(maxItems, 1, 500))
            .Select(path => new SymbolSearchResult
            {
                SymbolId = "generated:" + path,
                Kind = "generated_file",
                Name = Path.GetFileName(path),
                DisplayName = path,
                File = path,
                Line = 1,
                Confidence = "generated_file"
            })
            .ToArray();

        return ToolResponse<SymbolSearchResult>.Ok(
            $"Found {files.Length} generated files by scanning the filesystem; the cold index is not used for this tool.",
            files,
            detailLevel,
            "bypass",
            "cold",
            ["Generated code search intentionally bypasses the cold index because generated files are excluded from default indexing."]);
    }

    private async Task<ToolResponse<FlowAnalysisResult>> FlowAsync(string file, int line, int column, string kind, ToolScope? scope, string detailLevel, int maxItems, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<FlowAnalysisResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        if (!repoPathService.TryNormalizeRepoRelativePath(loaded.RepoRoot, file, out var normalizedFile, out var pathError))
        {
            return Empty<FlowAnalysisResult>("file_not_found", pathError, detailLevel);
        }

        var document = FindDocument(loaded.Handle!.Solution, loaded.RepoRoot, normalizedFile);
        if (document is null)
        {
            return Empty<FlowAnalysisResult>("file_not_found", $"File '{normalizedFile}' was not found in the selected solution.", detailLevel);
        }

        var text = await document.GetTextAsync(cancellationToken);
        if (line < 1 || line > text.Lines.Count)
        {
            return Empty<FlowAnalysisResult>("error", $"Line {line} is outside the file.", detailLevel);
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var model = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null)
        {
            return Empty<FlowAnalysisResult>("error", "Semantic model was not available for the document.", detailLevel);
        }

        var position = text.Lines[line - 1].Start + Math.Max(0, column - 1);
        var node = root.FindToken(position).Parent?.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault()
            ?? root.FindToken(position).Parent?.AncestorsAndSelf().FirstOrDefault();
        if (node is null)
        {
            return Empty<FlowAnalysisResult>("error", "No analyzable syntax node was found at the requested position.", detailLevel);
        }

        var operations = new List<string>();
        var reads = Array.Empty<string>();
        var writes = Array.Empty<string>();
        var captured = Array.Empty<string>();
        if (kind == "operation_tree")
        {
            var operation = model.GetOperation(node, cancellationToken);
            operations = FlattenOperation(operation, Math.Clamp(maxItems, 1, 200)).ToList();
        }
        else if (node is StatementSyntax statement)
        {
            var data = model.AnalyzeDataFlow(statement);
            if (data is not null && data.Succeeded)
            {
                reads = data.ReadInside.Select(symbol => symbol.Name).Distinct().Take(maxItems).ToArray();
                writes = data.WrittenInside.Select(symbol => symbol.Name).Distinct().Take(maxItems).ToArray();
                captured = data.Captured.Select(symbol => symbol.Name).Distinct().Take(maxItems).ToArray();
            }
            if (kind == "control_flow")
            {
                var control = model.AnalyzeControlFlow(statement);
                if (control is not null && control.Succeeded)
                {
                    operations.Add($"start_reachable={control.StartPointIsReachable}");
                    operations.Add($"end_reachable={control.EndPointIsReachable}");
                    operations.Add($"returns={control.ReturnStatements.Length}");
                }
            }
        }

        return ToolResponse<FlowAnalysisResult>.Ok(
            $"{kind} inspected syntax at {normalizedFile}:{line}.",
            [new FlowAnalysisResult
            {
                File = normalizedFile,
                Line = line,
                Kind = kind,
                Reads = reads,
                Writes = writes,
                Captured = captured,
                Operations = operations
            }],
            detailLevel,
            "hit",
            "warm");
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

    private async Task<List<SemanticDiagnosticResult>> DiagnosticsCoreAsync(Solution solution, string repoRoot, string? path, string severityAtLeast, int maxItems, CancellationToken cancellationToken)
    {
        string? normalizedPath = null;
        if (!string.IsNullOrWhiteSpace(path)
            && !repoPathService.TryNormalizeRepoRelativePath(repoRoot, path, out normalizedPath, out _))
        {
            return [];
        }

        var minSeverity = ParseSeverity(severityAtLeast);
        var diagnostics = new List<SemanticDiagnosticResult>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                if (diagnostic.Severity < minSeverity)
                {
                    continue;
                }

                var lineSpan = diagnostic.Location.IsInSource ? diagnostic.Location.GetLineSpan() : default;
                var relativePath = diagnostic.Location.IsInSource
                    && repoPathService.TryNormalizeRepoRelativePath(repoRoot, lineSpan.Path, out var diagnosticPath, out _)
                    ? diagnosticPath
                    : null;
                if (!string.IsNullOrWhiteSpace(normalizedPath) && !string.Equals(relativePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                diagnostics.Add(new SemanticDiagnosticResult
                {
                    Id = diagnostic.Id,
                    Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                    Message = diagnostic.GetMessage(),
                    File = relativePath,
                    Line = diagnostic.Location.IsInSource ? lineSpan.StartLinePosition.Line + 1 : null,
                    Column = diagnostic.Location.IsInSource ? lineSpan.StartLinePosition.Character + 1 : null
                });
                if (diagnostics.Count >= maxItems)
                {
                    return diagnostics;
                }
            }
        }

        return diagnostics;
    }

    private IEnumerable<string> ReferenceFiles(Solution solution, string repoRoot, IEnumerable<ReferencedSymbol> references)
    {
        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Definition.Locations.Concat(referencedSymbol.Locations.Select(reference => reference.Location)))
            {
                if (location.SourceTree is null)
                {
                    continue;
                }

                var document = solution.GetDocument(location.SourceTree);
                if (repoPathService.TryNormalizeDocumentPath(repoRoot, document?.FilePath, out var relativePath))
                {
                    yield return relativePath;
                }
            }
        }
    }

    private static IEnumerable<string> FlattenOperation(IOperation? operation, int maxItems)
    {
        if (operation is null)
        {
            yield break;
        }

        var stack = new Stack<IOperation>();
        stack.Push(operation);
        var count = 0;
        while (stack.Count > 0 && count++ < maxItems)
        {
            var current = stack.Pop();
            yield return $"{current.Kind}: {current.Syntax.Kind()}";
            foreach (var child in current.ChildOperations.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    private static IEnumerable<string> PublicSymbols(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var symbol in PublicSymbols(type))
            {
                yield return symbol;
            }
        }

        foreach (var nested in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var symbol in PublicSymbols(nested))
            {
                yield return symbol;
            }
        }
    }

    private static IEnumerable<ISymbol> GetPrivateMemberSymbols(SemanticModel model, SyntaxNode declaration, CancellationToken cancellationToken)
    {
        if (declaration is FieldDeclarationSyntax fieldDeclaration)
        {
            foreach (var variable in fieldDeclaration.Declaration.Variables)
            {
                var fieldSymbol = model.GetDeclaredSymbol(variable, cancellationToken);
                if (fieldSymbol?.DeclaredAccessibility == Accessibility.Private)
                {
                    yield return fieldSymbol;
                }
            }

            yield break;
        }

        var symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
        if (symbol?.DeclaredAccessibility == Accessibility.Private)
        {
            yield return symbol;
        }
    }

    private static IEnumerable<string> PublicSymbols(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
        {
            yield return typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        foreach (var member in typeSymbol.GetMembers().Where(member => member.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
        {
            yield return member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        foreach (var nested in typeSymbol.GetTypeMembers())
        {
            foreach (var symbol in PublicSymbols(nested))
            {
                yield return symbol;
            }
        }
    }

    private Document? FindDocument(Solution solution, string repoRoot, string file)
    {
        var normalized = repoPathService.NormalizePath(file);
        return solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document => repoPathService.TryNormalizeRepoRelativePath(repoRoot, document.FilePath ?? string.Empty, out var relativePath, out _)
                && string.Equals(relativePath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static DiagnosticSeverity ParseSeverity(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "hidden" => DiagnosticSeverity.Hidden,
            "info" => DiagnosticSeverity.Info,
            "warning" => DiagnosticSeverity.Warning,
            "error" => DiagnosticSeverity.Error,
            _ => DiagnosticSeverity.Warning
        };
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

    private static string FirstSegment(string value)
    {
        return value.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? value;
    }

    private static IReadOnlyList<string> RecommendedNextTools(
        IReadOnlyList<string>? symbolIds,
        IReadOnlyList<string>? files,
        IReadOnlyList<string> validationCommands)
    {
        var tools = new List<string>();
        if (symbolIds?.Count > 0 || files?.Count > 0)
        {
            tools.Add("cs_change_impact");
            tools.Add("cs_test_impact");
        }

        if (validationCommands.Count == 0)
        {
            tools.Add("cs_diagnostics_summary");
        }

        return tools.Distinct(StringComparer.Ordinal).ToArray();
    }

    private sealed record SemanticLoadContext(
        string RepoRoot,
        SolutionSummary? Solution,
        WorkspaceHandle? Handle,
        string? ResponseKind,
        string Summary,
        string? Warning);
}
