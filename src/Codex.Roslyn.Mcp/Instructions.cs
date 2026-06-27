namespace Codex.Roslyn.Mcp;

public static class Instructions
{
    public const string Text = """
For C#/.NET code tasks, use this Roslyn server before broad grep. Start with cs_repo_overview. If the cold index is missing or stale, call cs_index_build before symbol or outline lookup; cold index tools also rebuild automatically when they need to recover. Then use semantic tools for find usages, find references, go to definition, implementations, diagnostics, compile errors, test impact, rename, and refactor planning. Return compact results first.

Prefer cs_index_build, cs_symbol_search, cs_symbol_at, cs_find_references, cs_find_implementations, cs_type_hierarchy, cs_callers, cs_diagnostics, cs_diagnostics_summary, cs_context_pack, cs_change_impact, cs_test_impact, and cs_refactor_preview before raw file reads. Do not load every solution unless asked. Use cs_solution_list/cs_solution_select for ambiguity. No default plugin config applies edits; cs_apply_workspace_edit is explicit opt-in only and remains server-disabled unless --enable-apply or CODEX_ROSLYN_ENABLE_APPLY=1 is set.
""";
}
