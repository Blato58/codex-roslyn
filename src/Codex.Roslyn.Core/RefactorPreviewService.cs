using System.Security.Cryptography;
using System.Text;
using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Index;
using Codex.Roslyn.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Codex.Roslyn.Core;

public sealed class RefactorPreviewService(
    SolutionSelectionService solutionSelectionService,
    WorkspaceManager workspaceManager,
    ColdIndexService coldIndexService,
    SemanticSymbolCache symbolCache)
{
    public async Task<ToolResponse<RefactorPreviewResult>> PreviewAsync(
        string operation,
        string? symbolId = null,
        string? newName = null,
        string? file = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        return NormalizeOperation(operation) switch
        {
            "rename" => await PreviewRenameAsync(symbolId, newName, scope, detailLevel, maxItems, cancellationToken),
            "organize_usings" => await PreviewOrganizeUsingsAsync(file, scope, detailLevel, maxItems, cancellationToken),
            "move_type_to_file" => await PreviewMoveTypeToFileAsync(symbolId, file, scope, detailLevel, maxItems, cancellationToken),
            "move_type_to_namespace" or "extract_interface" or "change_namespace" => Unsupported<RefactorPreviewResult>(operation, detailLevel),
            _ => Unsupported<RefactorPreviewResult>(operation, detailLevel)
        };
    }

    public async Task<ToolResponse<ChangeImpactResult>> ChangeImpactAsync(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? changedFiles = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<ChangeImpactResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var impactedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var changedFile in changedFiles ?? [])
        {
            impactedFiles.Add(NormalizePath(changedFile));
        }

        var reasons = new List<string>();
        foreach (var symbolId in symbolIds ?? [])
        {
            if (!symbolCache.TryGet(symbolId, out var symbol))
            {
                reasons.Add($"Symbol {symbolId} is not in the warm semantic cache.");
                continue;
            }

            var references = await SymbolFinder.FindReferencesAsync(symbol, loaded.Handle!.Solution, cancellationToken);
            foreach (var path in GetReferenceFiles(loaded.Handle.Solution, loaded.RepoRoot, references))
            {
                impactedFiles.Add(path);
            }

            reasons.Add($"Semantic references for {symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} were inspected.");
        }

        var files = impactedFiles.Take(Math.Clamp(maxItems, 1, 500)).ToArray();
        var grouped = files
            .GroupBy(GetImpactArea, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ChangeImpactResult
            {
                Area = group.Key,
                Kind = group.Key.Contains("test", StringComparison.OrdinalIgnoreCase) ? "test" : "source",
                Confidence = reasons.Count > 0 ? "high" : "medium",
                Reasons = reasons.Count > 0 ? reasons : ["Changed files were grouped by path and project naming."],
                Files = group.ToArray()
            })
            .ToArray();

        return ToolResponse<ChangeImpactResult>.Ok(
            $"Identified {grouped.Length} impacted areas across {files.Length} files.",
            grouped,
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
        var impact = await ChangeImpactAsync(symbolIds, changedFiles, scope, detailLevel, maxItems, cancellationToken);
        if (impact.ResultKind != "ok")
        {
            return new ToolResponse<TestImpactResult>
            {
                ResultKind = impact.ResultKind,
                Summary = impact.Summary,
                Items = [],
                CacheStatus = impact.CacheStatus,
                TokenPolicy = impact.TokenPolicy,
                Warnings = impact.Warnings
            };
        }

        var testFiles = impact.Items
            .SelectMany(item => item.Files)
            .Where(file => file.Contains("test", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxItems, 1, 500))
            .ToArray();

        var items = testFiles.Length == 0
            ? [new TestImpactResult
            {
                TestArea = "targeted_project_tests",
                Confidence = "medium",
                Reasons = ["No direct test file reference was found; run tests for projects adjacent to impacted source files."],
                Files = impact.Items.SelectMany(item => item.Files).ToArray()
            }]
            : impact.Items
                .SelectMany(item => item.Files)
                .Where(file => file.Contains("test", StringComparison.OrdinalIgnoreCase))
                .GroupBy(GetImpactArea, StringComparer.OrdinalIgnoreCase)
                .Select(group => new TestImpactResult
                {
                    TestArea = group.Key,
                    Confidence = "high",
                    Reasons = ["Test files were directly referenced or grouped with impacted symbols."],
                    Files = group.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                })
                .ToArray();

        return ToolResponse<TestImpactResult>.Ok(
            $"Recommended {items.Length} test areas.",
            items,
            detailLevel,
            "hit",
            "warm");
    }

    private async Task<ToolResponse<RefactorPreviewResult>> PreviewRenameAsync(
        string? symbolId,
        string? newName,
        ToolScope? scope,
        string detailLevel,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbolId) || !symbolCache.TryGet(symbolId, out var symbol))
        {
            return Empty<RefactorPreviewResult>("stale_symbol", "Symbol is not in the warm semantic cache. Use cs_symbol_at first.", detailLevel);
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return Empty<RefactorPreviewResult>("invalid_request", "Rename preview requires newName.", detailLevel);
        }

        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<RefactorPreviewResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var changes = await BuildRenameChangesAsync(loaded.Handle!.Solution, loaded.RepoRoot, symbol, newName, cancellationToken);
        var beforeDiagnostics = await CountDiagnosticsAsync(loaded.Handle.Solution, cancellationToken);
        var previewSolution = await ApplyChangesAsync(loaded.Handle.Solution, changes, cancellationToken);
        var afterDiagnostics = await CountDiagnosticsAsync(previewSolution, cancellationToken);
        var riskReasons = GetRiskReasons(symbol, changes, afterDiagnostics - beforeDiagnostics);
        var result = CreatePreviewResult(
            "rename",
            symbolId,
            loaded.Solution!.SolutionId,
            changes,
            beforeDiagnostics,
            afterDiagnostics,
            riskReasons,
            maxItems);

        return ToolResponse<RefactorPreviewResult>.Ok(
            $"Rename would update {result.ChangedSpans} spans across {result.ChangedFiles} files.",
            [result],
            detailLevel,
            "hit",
            "warm");
    }

    private async Task<ToolResponse<RefactorPreviewResult>> PreviewOrganizeUsingsAsync(
        string? file,
        ToolScope? scope,
        string detailLevel,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return Empty<RefactorPreviewResult>("invalid_request", "Organize usings preview requires file.", detailLevel);
        }

        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<RefactorPreviewResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        var document = FindDocument(loaded.Handle!.Solution, loaded.RepoRoot, file);
        if (document is null)
        {
            return Empty<RefactorPreviewResult>("error", $"File '{file}' was not found in the selected solution.", detailLevel);
        }

        var organized = await Formatter.OrganizeImportsAsync(document, cancellationToken);
        var oldText = await document.GetTextAsync(cancellationToken);
        var newText = await organized.GetTextAsync(cancellationToken);
        var textChanges = newText.GetTextChanges(oldText)
            .Select(change => new TextChange(change.Span, change.NewText ?? string.Empty))
            .ToArray();
        var relative = RelativePath(loaded.RepoRoot, document.FilePath!);
        DocumentPreviewChanges[] changes = textChanges.Length == 0
            ? []
            : [new DocumentPreviewChanges(relative, document.Id, oldText, textChanges)];
        var beforeDiagnostics = await CountDiagnosticsAsync(loaded.Handle.Solution, cancellationToken);
        var afterDiagnostics = beforeDiagnostics;
        var result = CreatePreviewResult(
            "organize_usings",
            null,
            loaded.Solution!.SolutionId,
            changes,
            beforeDiagnostics,
            afterDiagnostics,
            [],
            maxItems);

        return ToolResponse<RefactorPreviewResult>.Ok(
            $"Organize usings would update {result.ChangedSpans} spans across {result.ChangedFiles} files.",
            [result],
            detailLevel,
            "hit",
            "warm");
    }

    private async Task<ToolResponse<RefactorPreviewResult>> PreviewMoveTypeToFileAsync(
        string? symbolId,
        string? file,
        ToolScope? scope,
        string detailLevel,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbolId) || !symbolCache.TryGet(symbolId, out var symbol))
        {
            return Empty<RefactorPreviewResult>("stale_symbol", "Symbol is not in the warm semantic cache. Use cs_symbol_at first.", detailLevel);
        }

        if (symbol is not INamedTypeSymbol namedType)
        {
            return Empty<RefactorPreviewResult>("invalid_request", "Move type preview requires a class, record, struct, interface, enum, or delegate symbol.", detailLevel);
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            return Empty<RefactorPreviewResult>("invalid_request", "Move type preview requires file as the destination path.", detailLevel);
        }

        var loaded = await LoadAsync(scope, detailLevel, cancellationToken);
        if (loaded.ResponseKind is not null)
        {
            return Empty<RefactorPreviewResult>(loaded.ResponseKind, loaded.Summary, detailLevel, loaded.Warning);
        }

        if (!TryNormalizeDestinationPath(loaded.RepoRoot, file, out var destinationFile, out var destinationError))
        {
            return Empty<RefactorPreviewResult>("invalid_request", destinationError, detailLevel);
        }

        var changes = await BuildMoveTypeToFileChangesAsync(loaded.Handle!.Solution, loaded.RepoRoot, namedType, destinationFile, cancellationToken);
        if (changes.ResponseKind is not null)
        {
            return Empty<RefactorPreviewResult>(changes.ResponseKind, changes.Summary, detailLevel);
        }

        var beforeDiagnostics = await CountDiagnosticsAsync(loaded.Handle.Solution, cancellationToken);
        var previewSolution = await ApplyChangesAsync(loaded.Handle.Solution, changes.Changes, cancellationToken);
        var afterDiagnostics = await CountDiagnosticsAsync(previewSolution, cancellationToken);
        var riskReasons = GetRiskReasons(namedType, changes.Changes, afterDiagnostics - beforeDiagnostics);
        var result = CreatePreviewResult(
            "move_type_to_file",
            symbolId,
            loaded.Solution!.SolutionId,
            changes.Changes,
            beforeDiagnostics,
            afterDiagnostics,
            riskReasons,
            maxItems);

        return ToolResponse<RefactorPreviewResult>.Ok(
            $"Move type to file would update {result.ChangedSpans} spans across {result.ChangedFiles} files.",
            [result],
            detailLevel,
            "hit",
            "warm");
    }

    private async Task<IReadOnlyList<DocumentPreviewChanges>> BuildRenameChangesAsync(
        Solution solution,
        string repoRoot,
        ISymbol symbol,
        string newName,
        CancellationToken cancellationToken)
    {
        var locations = new List<Location>();
        locations.AddRange(symbol.Locations.Where(location => location.IsInSource));
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
        locations.AddRange(references
            .SelectMany(reference => reference.Locations)
            .Select(reference => reference.Location)
            .Where(location => location is { IsInSource: true })!);

        var changes = new List<DocumentPreviewChanges>();
        foreach (var group in locations
            .Where(location => location.SourceTree is not null)
            .GroupBy(location => solution.GetDocument(location.SourceTree!)?.Id)
            .Where(group => group.Key is not null))
        {
            var document = solution.GetDocument(group.Key!)!;
            if (document.FilePath is null)
            {
                continue;
            }

            var oldText = await document.GetTextAsync(cancellationToken);
            var textChanges = group
                .Select(location => new TextChange(location.SourceSpan, newName))
                .DistinctBy(change => change.Span)
                .OrderByDescending(change => change.Span.Start)
                .ToArray();

            changes.Add(new DocumentPreviewChanges(RelativePath(repoRoot, document.FilePath), document.Id, oldText, textChanges));
        }

        return changes;
    }

    private static async Task<Solution> ApplyChangesAsync(
        Solution solution,
        IReadOnlyList<DocumentPreviewChanges> changes,
        CancellationToken cancellationToken)
    {
        var preview = solution;
        foreach (var change in changes)
        {
            var document = preview.GetDocument(change.DocumentId);
            if (document is null)
            {
                if (change.ProjectId is not null && change.FilePath is not null)
                {
                    var addedText = change.OriginalText.WithChanges(change.Changes);
                    preview = preview.AddDocument(
                        change.DocumentId,
                        Path.GetFileName(change.FilePath),
                        addedText,
                        GetDocumentFolders(change.File),
                        change.FilePath);
                }

                continue;
            }

            var oldText = await document.GetTextAsync(cancellationToken);
            var newText = oldText.WithChanges(change.Changes);
            preview = preview.WithDocumentText(change.DocumentId, newText);
        }

        return preview;
    }

    private static async Task<MoveTypeChangesResult> BuildMoveTypeToFileChangesAsync(
        Solution solution,
        string repoRoot,
        INamedTypeSymbol namedType,
        string destinationFile,
        CancellationToken cancellationToken)
    {
        var declarationReference = namedType.DeclaringSyntaxReferences.FirstOrDefault(reference => reference.SyntaxTree is not null);
        if (declarationReference is null)
        {
            return MoveTypeChangesResult.Fail("invalid_request", "Move type preview requires a source-declared type.");
        }

        var syntax = await declarationReference.GetSyntaxAsync(cancellationToken);
        if (syntax is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax)
        {
            return MoveTypeChangesResult.Fail("invalid_request", "Move type preview requires a type declaration.");
        }

        if (syntax.Parent is BaseTypeDeclarationSyntax)
        {
            return MoveTypeChangesResult.Fail("invalid_request", "Move type preview only supports top-level types in this slice.");
        }

        var sourceDocument = solution.GetDocument(declarationReference.SyntaxTree);
        if (sourceDocument?.FilePath is null)
        {
            return MoveTypeChangesResult.Fail("invalid_request", "Move type preview could not resolve the source document.");
        }

        var sourceFile = RelativePath(repoRoot, sourceDocument.FilePath);
        if (string.Equals(sourceFile, destinationFile, StringComparison.OrdinalIgnoreCase))
        {
            return MoveTypeChangesResult.Fail("invalid_request", "Destination file must be different from the source file.");
        }

        var sourceText = await sourceDocument.GetTextAsync(cancellationToken);
        var sourceChange = new DocumentPreviewChanges(
            sourceFile,
            sourceDocument.Id,
            sourceText,
            [new TextChange(syntax.FullSpan, string.Empty)],
            null,
            sourceDocument.FilePath);

        var destinationPath = Path.Combine(repoRoot, destinationFile.Replace('/', Path.DirectorySeparatorChar));
        var destinationText = BuildMovedTypeText(syntax, namedType, declarationReference.SyntaxTree);
        var destinationDocument = FindDocument(solution, repoRoot, destinationFile);
        DocumentPreviewChanges destinationChange;
        if (destinationDocument?.FilePath is not null)
        {
            var oldText = await destinationDocument.GetTextAsync(cancellationToken);
            var insertText = oldText.Length == 0 || oldText.ToString().EndsWith(Environment.NewLine, StringComparison.Ordinal)
                ? destinationText
                : Environment.NewLine + destinationText;
            destinationChange = new DocumentPreviewChanges(
                destinationFile,
                destinationDocument.Id,
                oldText,
                [new TextChange(new TextSpan(oldText.Length, 0), insertText)],
                null,
                destinationDocument.FilePath);
        }
        else
        {
            var documentId = DocumentId.CreateNewId(sourceDocument.Project.Id);
            destinationChange = new DocumentPreviewChanges(
                destinationFile,
                documentId,
                SourceText.From(string.Empty, Encoding.UTF8),
                [new TextChange(new TextSpan(0, 0), destinationText)],
                sourceDocument.Project.Id,
                destinationPath);
        }

        return MoveTypeChangesResult.Ok([sourceChange, destinationChange]);
    }

    private static RefactorPreviewResult CreatePreviewResult(
        string operation,
        string? symbolId,
        string solutionId,
        IReadOnlyList<DocumentPreviewChanges> changes,
        int diagnosticsBefore,
        int diagnosticsAfter,
        IReadOnlyList<string> riskReasons,
        int maxItems)
    {
        var changedSpans = changes.Sum(change => change.Changes.Count);
        var changedFiles = changes.Count(change => change.Changes.Count > 0);
        return new RefactorPreviewResult
        {
            EditId = $"edit_{Hash($"{operation}:{symbolId}:{solutionId}:{changedSpans}")}",
            Operation = operation,
            SymbolId = symbolId,
            SolutionId = solutionId,
            ChangedFiles = changedFiles,
            ChangedSpans = changedSpans,
            DiagnosticsBefore = diagnosticsBefore,
            DiagnosticsAfter = diagnosticsAfter,
            NewDiagnostics = Math.Max(0, diagnosticsAfter - diagnosticsBefore),
            Risk = GetRisk(riskReasons, diagnosticsAfter - diagnosticsBefore),
            RiskReasons = riskReasons,
            Changes = changes
                .Where(change => change.Changes.Count > 0)
                .Take(Math.Clamp(maxItems, 1, 200))
                .Select(change => new RefactorFileChange { File = change.File, Edits = change.Changes.Count })
                .ToArray(),
            DiffPreview = BuildCompactDiff(changes, maxItems),
            RequiresApproval = true
        };
    }

    private static string BuildCompactDiff(IReadOnlyList<DocumentPreviewChanges> changes, int maxItems)
    {
        var builder = new StringBuilder();
        foreach (var change in changes.Where(change => change.Changes.Count > 0).Take(Math.Clamp(maxItems, 1, 50)))
        {
            builder.AppendLine($"--- {change.File}");
            builder.AppendLine($"+++ {change.File}");
            foreach (var textChange in change.Changes.Take(20))
            {
                var line = change.OriginalText.Lines.GetLineFromPosition(textChange.Span.Start);
                var oldText = CompactChangeText(change.OriginalText.ToString(textChange.Span));
                var newText = CompactChangeText(textChange.NewText);
                builder.AppendLine($"@@ {line.LineNumber + 1}:{textChange.Span.Start - line.Start + 1} @@ {oldText} -> {newText}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string CompactChangeText(string? text)
    {
        const int maxLength = 160;
        var compact = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return compact.Length <= maxLength
            ? compact
            : compact[..maxLength] + "...";
    }

    private static IReadOnlyList<string> GetRiskReasons(ISymbol symbol, IReadOnlyList<DocumentPreviewChanges> changes, int newDiagnosticDelta)
    {
        var reasons = new List<string>();
        if (symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
        {
            reasons.Add("Public or protected API change.");
        }

        if (changes.Select(change => GetProjectLikeSegment(change.File)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("Symbol is used across multiple project-like paths.");
        }

        if (changes.Any(change => change.File.Contains("test", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("Symbol is used from test code.");
        }

        if (symbol.Locations.Any(location => location.IsInSource && IsGeneratedPath(location.GetLineSpan().Path)))
        {
            reasons.Add("Symbol is declared in generated-looking source.");
        }

        if (newDiagnosticDelta > 0)
        {
            reasons.Add("Preview introduces new compiler diagnostics.");
        }

        return reasons;
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

    private static async Task<int> CountDiagnosticsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            count += compilation.GetDiagnostics(cancellationToken)
                .Count(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        }

        return count;
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

                return string.Equals(RelativePath(repoRoot, document.FilePath), normalized, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static IEnumerable<string> GetReferenceFiles(
        Solution solution,
        string repoRoot,
        IEnumerable<ReferencedSymbol> references)
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
                if (document?.FilePath is not null)
                {
                    yield return RelativePath(repoRoot, document.FilePath);
                }
            }
        }
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

    private static ToolResponse<TItem> Unsupported<TItem>(string operation, string detailLevel)
    {
        return Empty<TItem>(
            "unsupported_operation",
            $"Operation '{operation}' is not implemented in the current Phase 4 preview slice.",
            detailLevel);
    }

    private static string NormalizeOperation(string operation)
    {
        return operation.Trim().Replace('-', '_').ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static bool TryNormalizeDestinationPath(string repoRoot, string file, out string destinationFile, out string error)
    {
        destinationFile = NormalizePath(file).TrimStart('/');
        error = string.Empty;

        if (Path.IsPathRooted(file))
        {
            error = "Destination file must be repo-relative.";
            return false;
        }

        if (!destinationFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            error = "Destination file must be a .cs path.";
            return false;
        }

        var repoFullPath = Path.GetFullPath(repoRoot);
        var destinationFullPath = Path.GetFullPath(Path.Combine(repoFullPath, destinationFile.Replace('/', Path.DirectorySeparatorChar)));
        var repoPrefix = repoFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? repoFullPath
            : repoFullPath + Path.DirectorySeparatorChar;
        if (!destinationFullPath.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Destination file must stay inside the repository.";
            return false;
        }

        destinationFile = NormalizePath(Path.GetRelativePath(repoFullPath, destinationFullPath));
        return true;
    }

    private static string RelativePath(string repoRoot, string path)
    {
        return NormalizePath(Path.GetRelativePath(repoRoot, path));
    }

    private static string BuildMovedTypeText(SyntaxNode syntax, INamedTypeSymbol namedType, SyntaxTree sourceTree)
    {
        var builder = new StringBuilder();
        if (sourceTree.GetRoot() is CompilationUnitSyntax root)
        {
            foreach (var usingDirective in root.Usings)
            {
                builder.Append(usingDirective.ToFullString());
            }

            if (root.Usings.Count > 0)
            {
                builder.AppendLine();
            }
        }

        var namespaceName = namedType.ContainingNamespace?.IsGlobalNamespace == false
            ? namedType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            : null;
        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            builder.Append("namespace ");
            builder.Append(namespaceName);
            builder.AppendLine(";");
            builder.AppendLine();
        }

        builder.AppendLine(syntax.ToFullString().Trim());
        return builder.ToString();
    }

    private static IReadOnlyList<string> GetDocumentFolders(string file)
    {
        var directory = Path.GetDirectoryName(NormalizePath(file));
        return string.IsNullOrWhiteSpace(directory)
            ? []
            : directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetRisk(IReadOnlyList<string> reasons, int newDiagnosticDelta)
    {
        if (newDiagnosticDelta > 0 || reasons.Any(reason => reason.Contains("Public", StringComparison.OrdinalIgnoreCase)))
        {
            return "high";
        }

        return reasons.Count > 0 ? "medium" : "low";
    }

    private static string GetImpactArea(string file)
    {
        var parts = NormalizePath(file).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && (parts[0] is "src" or "tests" or "test"))
        {
            return $"{parts[0]}/{parts[1]}";
        }

        return parts.FirstOrDefault() ?? "repo";
    }

    private static string GetProjectLikeSegment(string file)
    {
        var parts = NormalizePath(file).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : file;
    }

    private static bool IsGeneratedPath(string path)
    {
        return path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private sealed record DocumentPreviewChanges(
        string File,
        DocumentId DocumentId,
        SourceText OriginalText,
        IReadOnlyList<TextChange> Changes,
        ProjectId? ProjectId = null,
        string? FilePath = null);

    private sealed record MoveTypeChangesResult(
        string? ResponseKind,
        string Summary,
        IReadOnlyList<DocumentPreviewChanges> Changes)
    {
        public static MoveTypeChangesResult Ok(IReadOnlyList<DocumentPreviewChanges> changes)
        {
            return new MoveTypeChangesResult(null, string.Empty, changes);
        }

        public static MoveTypeChangesResult Fail(string responseKind, string summary)
        {
            return new MoveTypeChangesResult(responseKind, summary, []);
        }
    }

    private sealed record SemanticLoadContext(
        string RepoRoot,
        SolutionSummary? Solution,
        WorkspaceHandle? Handle,
        string? ResponseKind,
        string Summary,
        string? Warning);
}
