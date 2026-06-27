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
    IndexBuildService indexBuildService,
    SymbolSearchService symbolSearchService,
    DocumentOutlineService documentOutlineService,
    SolutionSelectionService solutionSelectionService,
    SemanticQueryService semanticQueryService,
    RefactorPreviewService refactorPreviewService,
    ImpactAnalysisService impactAnalysisService,
    AdvancedSemanticService advancedSemanticService)
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
    [Description("Build or refresh the cold SQLite index for the current repository without loading MSBuildWorkspace. Writes only CodexRoslyn cache files.")]
    public ToolResponse<IndexBuildSummary> cs_index_build(
        ToolScope? scope = null,
        string detailLevel = "normal",
        bool includeGenerated = false)
    {
        return indexBuildService.Build(scope, detailLevel, includeGenerated);
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
    [Description("Apply a cached workspace edit produced by a preview tool in this server process. Mutating and server-disabled unless --enable-apply or CODEX_ROSLYN_ENABLE_APPLY=1 is set.")]
    public Task<ToolResponse<WorkspaceEditApplyResult>> cs_apply_workspace_edit(
        string editId,
        ToolScope? scope = null,
        string detailLevel = "normal")
    {
        return refactorPreviewService.ApplyWorkspaceEditAsync(editId, scope, detailLevel);
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
        return impactAnalysisService.ChangeImpactAsync(symbolIds, changedFiles, scope, detailLevel, maxItems);
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
        return impactAnalysisService.TestImpactAsync(symbolIds, changedFiles, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Build a compact semantic context pack for a task, symbols, and files.")]
    public Task<ToolResponse<ContextPackResult>> cs_context_pack(
        IReadOnlyList<string>? symbolIds = null,
        IReadOnlyList<string>? files = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 20,
        int maxTokens = 2500)
    {
        return advancedSemanticService.ContextPackAsync(symbolIds, files, scope, detailLevel, maxItems, maxTokens);
    }

    [McpServerTool]
    [Description("Return compiler diagnostics grouped by severity, project, and file.")]
    public Task<ToolResponse<DiagnosticsSummaryResult>> cs_diagnostics_summary(
        ToolScope? scope = null,
        string? path = null,
        string severityAtLeast = "warning",
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.DiagnosticsSummaryAsync(scope, path, severityAtLeast, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return a capped call graph slice for a warm semantic symbol. Disabled by default.")]
    public Task<ToolResponse<CallGraphResult>> cs_full_call_graph(
        string symbolId,
        ToolScope? scope = null,
        string direction = "callers",
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.FullCallGraphAsync(symbolId, scope, direction, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return capped Roslyn data-flow facts for the syntax node at a file position. Disabled by default.")]
    public Task<ToolResponse<FlowAnalysisResult>> cs_data_flow(
        string file,
        int line,
        int column,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.DataFlowAsync(file, line, column, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return capped Roslyn control-flow facts for the statement at a file position. Disabled by default.")]
    public Task<ToolResponse<FlowAnalysisResult>> cs_control_flow(
        string file,
        int line,
        int column,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.ControlFlowAsync(file, line, column, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return a compact operation tree for the syntax node at a file position. Disabled by default.")]
    public Task<ToolResponse<FlowAnalysisResult>> cs_operation_tree(
        string file,
        int line,
        int column,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.OperationTreeAsync(file, line, column, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return compiler diagnostics only; analyzer execution is not run by this partial tool. Disabled by default.")]
    public Task<ToolResponse<SemanticDiagnosticResult>> cs_run_analyzers(
        ToolScope? scope = null,
        string? path = null,
        string severityAtLeast = "warning",
        string detailLevel = "normal",
        int maxItems = 100)
    {
        return advancedSemanticService.RunAnalyzersAsync(scope, path, severityAtLeast, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return diagnostic candidates only, not applyable code-fix workspace edits. Disabled by default.")]
    public Task<ToolResponse<CodeFixPreviewResult>> cs_code_fix_preview(
        string? diagnosticId = null,
        string? file = null,
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.CodeFixPreviewAsync(diagnosticId, file, scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return current public API inventory unless a baseline comparison is configured. Disabled by default.")]
    public Task<ToolResponse<PublicApiDiffResult>> cs_public_api_diff(
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 100)
    {
        return advancedSemanticService.PublicApiDiffAsync(scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Return low-confidence private symbols with no semantic references. Disabled by default.")]
    public Task<ToolResponse<DeadCodeCandidateResult>> cs_dead_code_candidates(
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.DeadCodeCandidatesAsync(scope, detailLevel, maxItems);
    }

    [McpServerTool]
    [Description("Search generated-looking C# files that are excluded from default cold indexing. Disabled by default.")]
    public ToolResponse<SymbolSearchResult> cs_generated_code_search(
        string query = "",
        ToolScope? scope = null,
        string detailLevel = "normal",
        int maxItems = 50)
    {
        return advancedSemanticService.GeneratedCodeSearch(query, scope, detailLevel, maxItems);
    }
}
