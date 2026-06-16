using Codex.Roslyn.Abstractions.Dtos;
using Microsoft.Data.Sqlite;

namespace Codex.Roslyn.Index;

public sealed class ColdIndexService(
    RepoIdentityService repoIdentityService,
    IndexPathProvider indexPathProvider,
    RepoScanner repoScanner,
    SyntaxDeclarationIndexer syntaxDeclarationIndexer,
    IndexDatabase indexDatabase)
{
    public IndexBuildSummary Build(string repoRoot, bool includeGenerated = false)
    {
        var identity = repoIdentityService.Create(repoRoot);
        var indexPath = indexPathProvider.GetIndexPath(identity);
        var scan = repoScanner.Scan(identity.RepoRoot, includeGenerated);
        var declarationsByFile = new Dictionary<string, IReadOnlyList<SyntaxDeclaration>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in scan.SourceFiles)
        {
            declarationsByFile[file.RelativePath] = syntaxDeclarationIndexer.Index(identity.RepoId, file);
        }

        indexDatabase.ReplaceIndex(indexPath, identity, scan, declarationsByFile);
        ClearDirty(identity);
        var status = indexDatabase.GetStatus(indexPath, identity);

        return new IndexBuildSummary
        {
            RepoRoot = identity.RepoRoot,
            RepoId = identity.RepoId,
            CachePath = indexPath,
            SchemaVersion = status.SchemaVersion,
            SolutionCount = status.SolutionCount,
            FilesIndexed = status.FilesIndexed,
            DeclarationsIndexed = status.DeclarationsIndexed,
            SymbolsIndexed = status.SymbolsIndexed,
            IndexState = status.IndexState
        };
    }

    public string Clear(string repoRoot)
    {
        var identity = repoIdentityService.Create(repoRoot);
        var indexPath = indexPathProvider.GetIndexPath(identity);
        var indexDirectory = Path.GetDirectoryName(indexPath)!;
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(indexDirectory))
        {
            Directory.Delete(indexDirectory, recursive: true);
        }

        return indexDirectory;
    }

    public IndexStatusSummary GetStatus(string repoRoot)
    {
        var identity = repoIdentityService.Create(repoRoot);
        var indexPath = indexPathProvider.GetIndexPath(identity);
        var status = indexDatabase.GetStatus(indexPath, identity);

        if (status.IndexState == "missing" || !IsDirty(identity))
        {
            return status;
        }

        return status with
        {
            IndexState = "stale",
            Message = "Cold index was marked dirty by file watcher."
        };
    }

    public IReadOnlyList<SolutionSummary> GetSolutions(string repoRoot)
    {
        var identity = repoIdentityService.Create(repoRoot);
        return indexDatabase.GetSolutions(indexPathProvider.GetIndexPath(identity));
    }

    public IReadOnlyList<SymbolSearchResult> SearchSymbols(string repoRoot, string query, string? kind, int maxItems)
    {
        var identity = repoIdentityService.Create(repoRoot);
        return indexDatabase.SearchSymbols(indexPathProvider.GetIndexPath(identity), query, kind, maxItems);
    }

    public IReadOnlyList<DocumentOutlineItem> GetDocumentOutline(string repoRoot, string file, int maxItems)
    {
        var identity = repoIdentityService.Create(repoRoot);
        return indexDatabase.GetDocumentOutline(indexPathProvider.GetIndexPath(identity), file, maxItems);
    }

    public void RecordSemanticSymbol(string repoRoot, SemanticSymbolResult symbol)
    {
        var identity = repoIdentityService.Create(repoRoot);
        indexDatabase.RecordSemanticSymbol(indexPathProvider.GetIndexPath(identity), identity, symbol);
    }

    public void RecordSemanticReferences(string repoRoot, string targetSymbolId, IReadOnlyList<SemanticReferenceResult> references)
    {
        var identity = repoIdentityService.Create(repoRoot);
        indexDatabase.RecordSemanticReferences(indexPathProvider.GetIndexPath(identity), identity, targetSymbolId, references);
    }

    public void RecordSemanticHierarchy(string repoRoot, string sourceSymbolId, IReadOnlyList<SemanticHierarchyResult> hierarchy)
    {
        var identity = repoIdentityService.Create(repoRoot);
        indexDatabase.RecordSemanticHierarchy(indexPathProvider.GetIndexPath(identity), sourceSymbolId, hierarchy);
    }

    public void RecordSemanticCallers(string repoRoot, IReadOnlyList<SemanticCallerResult> callers)
    {
        var identity = repoIdentityService.Create(repoRoot);
        indexDatabase.RecordSemanticCallers(indexPathProvider.GetIndexPath(identity), identity, callers);
    }

    public void RecordSemanticDiagnostics(string repoRoot, IReadOnlyList<SemanticDiagnosticResult> diagnostics)
    {
        var identity = repoIdentityService.Create(repoRoot);
        indexDatabase.RecordSemanticDiagnostics(indexPathProvider.GetIndexPath(identity), identity, diagnostics);
    }

    public void MarkDirty(string repoRoot)
    {
        var identity = repoIdentityService.Create(repoRoot);
        var dirtyPath = GetDirtyPath(identity);
        Directory.CreateDirectory(Path.GetDirectoryName(dirtyPath)!);
        File.WriteAllText(dirtyPath, DateTimeOffset.UtcNow.ToString("O"));
    }

    private void ClearDirty(RepoIdentity identity)
    {
        var dirtyPath = GetDirtyPath(identity);
        if (File.Exists(dirtyPath))
        {
            File.Delete(dirtyPath);
        }
    }

    private bool IsDirty(RepoIdentity identity)
    {
        return File.Exists(GetDirtyPath(identity));
    }

    private string GetDirtyPath(RepoIdentity identity)
    {
        var indexPath = indexPathProvider.GetIndexPath(identity);
        return Path.Combine(Path.GetDirectoryName(indexPath)!, "dirty");
    }
}
