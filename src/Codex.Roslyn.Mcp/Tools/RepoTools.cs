using System.ComponentModel;
using Codex.Roslyn.Abstractions.Dtos;
using Codex.Roslyn.Abstractions.ToolContracts;
using Codex.Roslyn.Core;
using ModelContextProtocol.Server;

namespace Codex.Roslyn.Mcp.Tools;

[McpServerToolType]
public sealed class RepoTools(
    RepoOverviewService repoOverviewService,
    IndexStatusService indexStatusService,
    SymbolSearchService symbolSearchService,
    DocumentOutlineService documentOutlineService,
    SolutionSelectionService solutionSelectionService,
    SemanticQueryService semanticQueryService,
    RefactorPreviewService refactorPreviewService)
{
    [McpServerTool]
    [Description("Summarize the repository, discovered solutions, and cold index freshness without loading MSBuildWorkspace.")]
    public ToolResponse<RepoOverviewItem> cs_repo_overview(
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        int maxDepth = 2,
        bool includeSource = false,
        bool includeGenerated = false,
        string? cursor = null)
    {
        return repoOverviewService.GetOverview(scope, detailLevel);
    }

    [McpServerTool]
    [Description("List .sln and .slnx files discovered under the repository root without loading MSBuildWorkspace.")]
    public ToolResponse<SolutionSummary> cs_solution_list(
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        int maxDepth = 2,
        bool includeSource = false,
        bool includeGenerated = false,
        string? cursor = null)
    {
        return repoOverviewService.ListSolutions(scope, detailLevel);
    }

    [McpServerTool]
    [Description("Select the active solution for semantic Roslyn tools in this MCP server process.")]
    public ToolResponse<SolutionSelectResult> cs_solution_select(
        string solutionId,
        ToolScope? scope = null,
        string detailLevel = "normal")
    {
        return solutionSelectionService.Select(solutionId, scope, detailLevel);
    }

    [McpServerTool]
    [Description("Report cold index state and workspace cache status without loading every solution.")]
    public ToolResponse<IndexStatusSummary> cs_index_status(
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        int maxDepth = 2,
        bool includeSource = false,
        bool includeGenerated = false,
        string? cursor = null)
    {
        return indexStatusService.GetStatus(scope?.RepoRoot, detailLevel);
    }

    [McpServerTool]
    [Description("Search syntax-only C# declarations from the cold SQLite FTS index without loading MSBuildWorkspace.")]
    public ToolResponse<SymbolSearchResult> cs_symbol_search(
        string query,
        string? kind = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        int maxDepth = 2,
        bool includeSource = false,
        bool includeGenerated = false,
        string? cursor = null)
    {
        return symbolSearchService.Search(query, kind, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return a compact syntax-only outline for a C# file from the cold SQLite index.")]
    public ToolResponse<DocumentOutlineItem> cs_document_outline(
        string file,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        int maxDepth = 2,
        bool includeSource = false,
        bool includeGenerated = false,
        string? cursor = null)
    {
        return documentOutlineService.GetOutline(file, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Identify the Roslyn semantic symbol at a 1-based file line and column in the selected solution.")]
    public Task<ToolResponse<SemanticSymbolResult>> cs_symbol_at(
        string file,
        int line,
        int column,
        ToolScope? scope = null,
        string detailLevel = "normal")
    {
        return semanticQueryService.SymbolAtAsync(file, line, column, scope, detailLevel);
    }

    [McpServerTool]
    [Description("Find semantic references for a symbol returned by cs_symbol_at or another warm semantic tool.")]
    public Task<ToolResponse<SemanticReferenceResult>> cs_find_references(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        string? cursor = null)
    {
        return semanticQueryService.FindReferencesAsync(symbolId, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Find implementations for a semantic symbol in the selected solution.")]
    public Task<ToolResponse<SemanticHierarchyResult>> cs_find_implementations(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        string? cursor = null)
    {
        return semanticQueryService.FindImplementationsAsync(symbolId, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return base/interface/derived type hierarchy entries for a semantic named type.")]
    public Task<ToolResponse<SemanticHierarchyResult>> cs_type_hierarchy(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        string? cursor = null)
    {
        return semanticQueryService.TypeHierarchyAsync(symbolId, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return one-hop callers for a method-like semantic symbol in the selected solution.")]
    public Task<ToolResponse<SemanticCallerResult>> cs_callers(
        string symbolId,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        string? cursor = null)
    {
        return semanticQueryService.CallersAsync(symbolId, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return compiler diagnostics for the selected solution, project, or file without analyzer execution.")]
    public Task<ToolResponse<SemanticDiagnosticResult>> cs_diagnostics(
        ToolScope? scope = null,
        string? path = null,
        string severityAtLeast = "warning",
        string detailLevel = "normal",
        int maxItems = 200,
        string? cursor = null)
    {
        return semanticQueryService.DiagnosticsAsync(scope, path, severityAtLeast, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Preview a read-only Roslyn refactor such as rename or organize_usings. Does not mutate files.")]
    public Task<ToolResponse<RefactorPreviewResult>> cs_refactor_preview(
        string operation,
        string? symbolId = null,
        string? newName = null,
        string? file = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return refactorPreviewService.PreviewAsync(operation, symbolId, newName, file, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Estimate impacted source/test areas from changed files or warm semantic symbols.")]
    public Task<ToolResponse<ChangeImpactResult>> cs_change_impact(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? changedFiles = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        string? cursor = null)
    {
        return refactorPreviewService.ChangeImpactAsync(symbolIds, changedFiles, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Recommend likely impacted .NET test areas from changed files or warm semantic symbols.")]
    public Task<ToolResponse<TestImpactResult>> cs_test_impact(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? changedFiles = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50,
        string? cursor = null)
    {
        return refactorPreviewService.TestImpactAsync(symbolIds, changedFiles, scope, detailLevel, maxItems);
    }
}
