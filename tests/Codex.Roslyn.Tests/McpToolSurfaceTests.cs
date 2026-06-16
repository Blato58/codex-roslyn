using System.Reflection;
using Codex.Roslyn.Mcp.Tools;
using ModelContextProtocol.Server;

namespace Codex.Roslyn.Tests;

public sealed class McpToolSurfaceTests
{
    [Fact]
    public void RepoTools_ExposePhaseZeroTools()
    {
        var tools = typeof(RepoTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "cs_callers",
                "cs_change_impact",
                "cs_diagnostics",
                "cs_document_outline",
                "cs_find_implementations",
                "cs_find_references",
                "cs_index_status",
                "cs_refactor_preview",
                "cs_repo_overview",
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
