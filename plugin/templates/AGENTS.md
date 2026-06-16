# C#/.NET Semantic Tooling

Use the Roslyn MCP server before broad text search when working in C# or .NET code.

- Start with `cs_repo_overview` to identify solutions and index state.
- Use `cs_solution_list` and `cs_solution_select` when a repository has multiple solutions.
- Use `cs_symbol_search`, `cs_symbol_at`, `cs_find_references`, `cs_find_implementations`, `cs_type_hierarchy`, `cs_callers`, and `cs_diagnostics` before broad grep or raw file reads.
- Use `cs_document_outline` before reading a whole C# file for structure.
- Keep semantic requests compact first with tiny or normal detail.
- Use `cs_refactor_preview` before semantic rename/refactor edits.
- Use `cs_change_impact` and `cs_test_impact` to select targeted validation.
- Use daemon mode for large repositories when available: `dotnet-roslyn-mcp serve --http --port 38777`.
- Do not load every solution unless the task explicitly requires all-solution analysis.
- Do not request source text unless an edit needs exact surrounding code.
- Do not apply workspace edits through MCP unless an apply tool is explicitly enabled and approved.
