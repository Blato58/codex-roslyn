namespace Codex.Roslyn.Mcp;

public static class Instructions
{
    public const string Text = """
Use this server first for C#/.NET repositories. Prefer cs_repo_overview, cs_symbol_search, cs_symbol_at, cs_find_references, cs_find_implementations, cs_type_hierarchy, cs_callers, cs_diagnostics, cs_diagnostics_summary, cs_context_pack, and cs_refactor_preview before grep or broad file reads. Return compact results first. Do not load every solution unless asked.

This server provides Roslyn-backed semantic information for navigation, diagnostics, impact analysis, safe refactor previews, and opt-in advanced analysis. It supports repos with multiple .sln files. Use solutionId when known. If a file belongs to multiple solutions, resolve ambiguity with cs_solution_list or cs_solution_select. No default plugin config applies edits; cs_apply_workspace_edit is for explicit prompt-approved opt-in use only and is server-disabled unless --enable-apply or CODEX_ROSLYN_ENABLE_APPLY=1 is set.
""";
}
