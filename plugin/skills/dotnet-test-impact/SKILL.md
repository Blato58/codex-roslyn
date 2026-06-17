---
name: dotnet-test-impact
description: Use after C#/.NET code changes or when choosing targeted dotnet test commands, test impact, affected tests, changed symbols, references, callers, diagnostics, and project shape.
---

# .NET test impact

For targeted test selection:

- Start with `cs_repo_overview` and select the relevant solution when needed.
- Use `cs_symbol_search` or `cs_symbol_at` to identify changed symbols.
- Use `cs_find_references` and `cs_callers` to find affected production and test code.
- Use `cs_change_impact` and `cs_test_impact` when symbol IDs or changed files are known.
- Use `cs_diagnostics` to check the current compiler state before and after edits.
- Prefer the `command` returned by `cs_test_impact` before a full solution test run.
- Explain test choices from impact output, references, callers, diagnostics, and project naming.
