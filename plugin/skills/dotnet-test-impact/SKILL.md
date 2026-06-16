---
name: dotnet-test-impact
description: Use for selecting impacted .NET/C# tests from changed files, changed symbols, references, callers, diagnostics, project shape, and naming conventions.
---

# .NET test impact

For targeted test selection:

- Start with `cs_repo_overview` and select the relevant solution when needed.
- Use `cs_symbol_search` or `cs_symbol_at` to identify changed symbols.
- Use `cs_find_references` and `cs_callers` to find affected production and test code.
- Use `cs_change_impact` and `cs_test_impact` when symbol IDs or changed files are known.
- Use `cs_diagnostics` to check the current compiler state before and after edits.
- Prefer targeted tests for the affected project and nearby test project before a full solution test run.
- Explain test choices from impact output, references, callers, diagnostics, and project naming.
