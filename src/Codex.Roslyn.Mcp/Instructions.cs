namespace Codex.Roslyn.Mcp;

public static class Instructions
{
    public const string Text = """
Use this server first for C#/.NET repositories. Prefer cs_repo_overview, cs_symbol_search, cs_symbol_at, cs_find_references, cs_find_implementations, cs_type_hierarchy, cs_callers, cs_diagnostics, and cs_refactor_preview before grep or broad file reads. Return compact results first. Do not load every solution unless asked.

This server provides Roslyn-backed semantic information for navigation, diagnostics, impact analysis, and safe refactor previews. It supports repos with multiple .sln files. Use solutionId when known. If a file belongs to multiple solutions, resolve ambiguity with cs_solution_list or cs_solution_select. No default tool applies edits.
""";
}
