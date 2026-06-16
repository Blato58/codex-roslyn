---
name: csharp-semantic-navigation
description: Use for C#/.NET code navigation, symbol lookup, go-to-definition, references, implementations, type hierarchy, callers, diagnostics, and compact repository overview. Prefer Roslyn MCP tools before grep or raw file reads.
---

# C# semantic navigation

When working in a C# or .NET repository:

- Call `cs_repo_overview` first unless the active solution is already known.
- Use `cs_solution_list` or `cs_solution_select` when multiple `.sln` files exist.
- Use `cs_symbol_search` for names, types, methods, properties, interfaces, records, and namespaces.
- Use `cs_document_outline` for file-level structure before reading a whole file.
- Use `cs_symbol_at` for file/line/column questions.
- Use `cs_find_references`, `cs_find_implementations`, `cs_type_hierarchy`, or `cs_callers` instead of broad text search.
- Use `cs_diagnostics` for compiler diagnostics in the selected solution.
- Use `cs_diagnostics_summary` and `cs_context_pack` when a compact planning summary is enough.
- Use `cs_change_impact` and `cs_test_impact` when changed files or symbols are known.
- Prefer validation commands returned by `cs_context_pack` or `cs_test_impact` before writing broad `dotnet test` commands.
- Request `detailLevel: "tiny"` or `"normal"` first.
- Do not request source text unless an edit requires exact surrounding code.
- Do not load every solution unless the user explicitly asks for all-solution analysis.
- Prefer HTTP daemon mode for large repositories when it is already running.
