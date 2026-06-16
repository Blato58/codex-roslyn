using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;
using Codex.Roslyn.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Codex.Roslyn.Core;

public sealed class SemanticQueryService(
    SolutionSelectionService solutionSelectionService,
    WorkspaceManager workspaceManager,
    ColdIndexService coldIndexService,
    SemanticSymbolIdService symbolIdService,
    SemanticSymbolCache symbolCache)
{
    public async Task<ToolResponse<SemanticSymbolResult>> SymbolAtAsync(
        string file,
        int line,
        int column,
        ToolScope? scope = null,
        string detailLevel = "normal",
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<SemanticSymbolResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var handle = loaded.Handle!;
        var document = FindDocument(handle.Solution, loaded.RepoRoot, file);
        if (document is null)
        {
            return Empty<SemanticSymbolResult>("error", $"File '{file}' was not found in the selected solution.", detailLevel);
        }

        var sourceText = await document.GetTextAsync(cancellationToken);
        if (line < 1 || line > sourceText.Lines.Count)
        {
            return Empty<SemanticSymbolResult>("error", $"Line {line} is outside the file.", detailLevel);
        }

        var position = sourceText.Lines[line - 1].Start + Math.Max(0, column - 1);
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var model = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null)
        {
            return Empty<SemanticSymbolResult>("error", "Semantic model was not available for the document.", detailLevel);
        }

        var token = root.FindToken(position);
        var symbol = token.Parent?.AncestorsAndSelf()
            .Select(node => model.GetDeclaredSymbol(node, cancellationToken) ?? model.GetSymbolInfo(node, cancellationToken).Symbol)
            .FirstOrDefault(candidate => candidate is not null);

        if (symbol is null)
        {
            return Empty<SemanticSymbolResult>("error", "No semantic symbol was found at the requested position.", detailLevel);
        }

        var item = CreateSymbolResult(loaded.RepoRoot, loaded.Solution!.SolutionId, symbol);
        symbolCache.Store(item.SymbolId, symbol);
        coldIndexService.RecordSemanticSymbol(loaded.RepoRoot, item);

        return ToolResponse<SemanticSymbolResult>.Ok(
            $"Symbol is {item.Kind} {item.DisplayName}.",
            [item],
            detailLevel,
            indexStatus: coldIndexService.GetStatus(loaded.RepoRoot).IndexState == "fresh" ? "hit" : "stale",
            workspaceStatus: "warm");
    }

    public async Task<ToolResponse<SemanticReferenceResult>> FindReferencesAsync(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveCachedSymbolAsync<SemanticReferenceResult>(symbolId, scope, detailLevel, cancellationToken);
        if (resolved.Response is not null)
        {
            return resolved.Response;
        }

        var references = await SymbolFinder.FindReferencesAsync(resolved.Symbol!, resolved.Handle!.Solution, cancellationToken);
        var items = new List<SemanticReferenceResult>();

        foreach (var referencedSymbol in references)
        {
            foreach (var definitionLocation in referencedSymbol.Definition.Locations.Where(location => location.IsInSource))
            {
                var definitionSpan = definitionLocation.GetLineSpan();
                items.Add(new SemanticReferenceResult
                {
                    SymbolId = symbolId,
                    DisplayName = referencedSymbol.Definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    File = Path.GetRelativePath(resolved.RepoRoot!, definitionSpan.Path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'),
                    StartLine = definitionSpan.StartLinePosition.Line + 1,
                    StartColumn = definitionSpan.StartLinePosition.Character + 1,
                    IsDefinition = true,
                    ReferenceKind = "declaration"
                });
            }

            foreach (var location in referencedSymbol.Locations)
            {
                if (location.Location is null || !location.Location.IsInSource)
                {
                    continue;
                }

                var span = location.Location.GetLineSpan();
                items.Add(new SemanticReferenceResult
                {
                    SymbolId = symbolId,
                    DisplayName = referencedSymbol.Definition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    File = Path.GetRelativePath(resolved.RepoRoot!, span.Path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'),
                    StartLine = span.StartLinePosition.Line + 1,
                    StartColumn = span.StartLinePosition.Character + 1,
                    IsDefinition = false,
                    ReferenceKind = location.IsImplicit ? "implicit_reference" : "reference"
                });
            }
        }

        var page = items.Take(Math.Clamp(maxItems, 1, 500)).ToArray();
        coldIndexService.RecordSemanticReferences(resolved.RepoRoot!, symbolId, page);

        return ToolResponse<SemanticReferenceResult>.Ok(
            $"Found {Math.Min(items.Count, maxItems)} semantic references.",
            page,
            detailLevel,
            indexStatus: "hit",
            workspaceStatus: "warm");
    }

    public async Task<ToolResponse<SemanticHierarchyResult>> FindImplementationsAsync(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveCachedSymbolAsync<SemanticHierarchyResult>(symbolId, scope, detailLevel, cancellationToken);
        if (resolved.Response is not null)
        {
            return resolved.Response;
        }

        var implementations = await SymbolFinder.FindImplementationsAsync(resolved.Symbol!, resolved.Handle!.Solution, cancellationToken: cancellationToken);
        var items = implementations.Take(Math.Clamp(maxItems, 1, 500))
            .Select(symbol => CreateHierarchyResult(resolved.RepoRoot!, resolved.Solution!.SolutionId, symbol, "implementation"))
            .ToArray();
        coldIndexService.RecordSemanticHierarchy(resolved.RepoRoot!, symbolId, items);

        return ToolResponse<SemanticHierarchyResult>.Ok($"Found {items.Length} implementations.", items, detailLevel, "hit", "warm");
    }

    public async Task<ToolResponse<SemanticHierarchyResult>> TypeHierarchyAsync(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveCachedSymbolAsync<SemanticHierarchyResult>(symbolId, scope, detailLevel, cancellationToken);
        if (resolved.Response is not null)
        {
            return resolved.Response;
        }

        if (resolved.Symbol is not INamedTypeSymbol namedType)
        {
            return Empty<SemanticHierarchyResult>("error", "Type hierarchy requires a named type symbol.", detailLevel);
        }

        var items = new List<SemanticHierarchyResult>();
        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            items.Add(CreateHierarchyResult(resolved.RepoRoot!, resolved.Solution!.SolutionId, current, "base"));
        }

        foreach (var iface in namedType.AllInterfaces)
        {
            items.Add(CreateHierarchyResult(resolved.RepoRoot!, resolved.Solution!.SolutionId, iface, "interface"));
        }

        var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, resolved.Handle!.Solution, cancellationToken: cancellationToken);
        items.AddRange(derived.Select(symbol => CreateHierarchyResult(resolved.RepoRoot!, resolved.Solution!.SolutionId, symbol, "derived")));

        var page = items.Take(Math.Clamp(maxItems, 1, 500)).ToArray();
        coldIndexService.RecordSemanticHierarchy(resolved.RepoRoot!, symbolId, page);

        return ToolResponse<SemanticHierarchyResult>.Ok(
            $"Found {Math.Min(items.Count, maxItems)} hierarchy entries.",
            page,
            detailLevel,
            "hit",
            "warm");
    }

    public async Task<ToolResponse<SemanticCallerResult>> CallersAsync(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveCachedSymbolAsync<SemanticCallerResult>(symbolId, scope, detailLevel, cancellationToken);
        if (resolved.Response is not null)
        {
            return resolved.Response;
        }

        var callers = await SymbolFinder.FindCallersAsync(resolved.Symbol!, resolved.Handle!.Solution, cancellationToken);
        var items = callers.Take(Math.Clamp(maxItems, 1, 500))
            .Select(caller =>
            {
                var callerId = symbolIdService.CreateSymbolId(resolved.RepoRoot!, resolved.Solution!.SolutionId, caller.CallingSymbol);
                symbolCache.Store(callerId, caller.CallingSymbol);
                return new SemanticCallerResult
                {
                    CallerSymbolId = callerId,
                    CallerDisplayName = caller.CallingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    CalleeSymbolId = symbolId,
                    CalleeDisplayName = resolved.Symbol!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    Location = symbolIdService.CreateLocation(resolved.RepoRoot!, caller.Locations.FirstOrDefault())
                };
            })
            .ToArray();
        coldIndexService.RecordSemanticCallers(resolved.RepoRoot!, items);

        return ToolResponse<SemanticCallerResult>.Ok($"Found {items.Length} callers.", items, detailLevel, "hit", "warm");
    }

    public async Task<ToolResponse<SemanticDiagnosticResult>> DiagnosticsAsync(
        ToolScope? scope = null,
        string? path = null,
        string severityAtLeast = "warning",
        string detailLevel = "normal",
        int maxItems = 200,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<SemanticDiagnosticResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var minSeverity = ParseSeverity(severityAtLeast);
        var diagnostics = new List<SemanticDiagnosticResult>();
        foreach (var project in loaded.Handle!.Solution.Projects)
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
                    ? Path.GetRelativePath(loaded.RepoRoot, lineSpan.Path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')
                    : null;
                if (!string.IsNullOrWhiteSpace(path)
                    && !string.Equals(relativePath, NormalizePath(path), StringComparison.OrdinalIgnoreCase))
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
            }
        }

        var page = diagnostics.Take(Math.Clamp(maxItems, 1, 1000)).ToArray();
        coldIndexService.RecordSemanticDiagnostics(loaded.RepoRoot, page);

        return ToolResponse<SemanticDiagnosticResult>.Ok(
            $"Found {Math.Min(diagnostics.Count, maxItems)} diagnostics.",
            page,
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

    private async Task<ResolvedSymbol<TItem>> ResolveCachedSymbolAsync<TItem>(
        string symbolId,
        ToolScope? scope,
        string detailLevel,
        CancellationToken cancellationToken)
    {
        if (!symbolCache.TryGet(symbolId, out var symbol))
        {
            return new ResolvedSymbol<TItem>(
                null,
                null,
                null,
                null,
                Empty<TItem>("stale_symbol", "Symbol is not in the warm semantic cache. Use cs_symbol_at first or search again.", detailLevel));
        }

        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return new ResolvedSymbol<TItem>(
                loaded.RepoRoot,
                loaded.Solution,
                loaded.Handle,
                symbol,
                Empty<TItem>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning));
        }

        return new ResolvedSymbol<TItem>(loaded.RepoRoot, loaded.Solution, loaded.Handle, symbol, null);
    }

    private SemanticSymbolResult CreateSymbolResult(string repoRoot, string solutionId, ISymbol symbol)
    {
        var symbolId = symbolIdService.CreateSymbolId(repoRoot, solutionId, symbol);
        return new SemanticSymbolResult
        {
            SymbolId = symbolId,
            Kind = GetDisplayKind(symbol),
            Name = symbol.Name,
            DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            DocumentationCommentId = DocumentationCommentId.CreateDeclarationId(symbol),
            AssemblyName = symbol.ContainingAssembly?.Name,
            DeclaredAccessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            ReturnType = symbol is IMethodSymbol method ? method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) : null,
            Definition = symbolIdService.CreateLocation(repoRoot, symbol.Locations.FirstOrDefault(location => location.IsInSource))
        };
    }

    private SemanticHierarchyResult CreateHierarchyResult(string repoRoot, string solutionId, ISymbol symbol, string relationKind)
    {
        var symbolId = symbolIdService.CreateSymbolId(repoRoot, solutionId, symbol);
        symbolCache.Store(symbolId, symbol);
        return new SemanticHierarchyResult
        {
            SymbolId = symbolId,
            DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            RelationKind = relationKind,
            Location = symbolIdService.CreateLocation(repoRoot, symbol.Locations.FirstOrDefault(location => location.IsInSource))
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

    private static Document? FindDocument(Solution solution, string repoRoot, string file)
    {
        var normalized = NormalizePath(file);
        return solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(document =>
            {
                if (document.FilePath is null)
                {
                    return false;
                }

                var relative = Path.GetRelativePath(repoRoot, document.FilePath);
                return string.Equals(NormalizePath(relative), normalized, StringComparison.OrdinalIgnoreCase);
            });
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

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string GetDisplayKind(ISymbol symbol)
    {
        return symbol is INamedTypeSymbol namedType
            ? namedType.TypeKind.ToString().ToLowerInvariant()
            : symbol.Kind.ToString().ToLowerInvariant();
    }

    private sealed record SemanticLoadContext(
        string RepoRoot,
        SolutionSummary? Solution,
        WorkspaceHandle? Handle,
        string? ResponseKind,
        string Summary,
        string? Warning);

    private sealed record ResolvedSymbol<TItem>(
        string? RepoRoot,
        SolutionSummary? Solution,
        WorkspaceHandle? Handle,
        ISymbol? Symbol,
        ToolResponse<TItem>? Response);
}
