using System.Reflection;
using Codex.Roslyn.Mcp.Tools;
using ModelContextProtocol.Server;

namespace Codex.Roslyn.Tests;

public sealed class McpToolSurfaceTests
{
    [Fact]
    public void RepoTools_ExposeCurrentRoadmapTools()
    {
        var tools = typeof(RepoTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "cs_apply_workspace_edit",
                "cs_callers",
                "cs_change_impact",
                "cs_code_fix_preview",
                "cs_context_pack",
                "cs_control_flow",
                "cs_data_flow",
                "cs_dead_code_candidates",
                "cs_diagnostics",
                "cs_diagnostics_summary",
                "cs_document_outline",
                "cs_find_implementations",
                "cs_find_references",
                "cs_full_call_graph",
                "cs_generated_code_search",
                "cs_index_build",
                "cs_index_status",
                "cs_operation_tree",
                "cs_public_api_diff",
                "cs_refactor_preview",
                "cs_repo_overview",
                "cs_run_analyzers",
                "cs_solution_list",
                "cs_solution_select",
                "cs_symbol_at",
                "cs_symbol_search",
                "cs_test_impact",
                "cs_type_hierarchy"
            ],
            tools);
    }
}
