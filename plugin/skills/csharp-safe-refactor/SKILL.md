---
name: csharp-safe-refactor
description: Use for safe C#/.NET rename, move type, extract interface, change signature planning, and refactor impact review. Always call Roslyn refactor preview before editing files.
---

# C# safe refactoring

For semantic refactor planning:

- Identify the target symbol with `cs_symbol_search` or `cs_symbol_at`.
- Use `cs_find_references` to estimate blast radius.
- Use `cs_find_implementations`, `cs_type_hierarchy`, and `cs_callers` when inheritance or call flow matters.
- Use `cs_diagnostics` before editing to capture the current compiler state.
- Call `cs_change_impact` or `cs_test_impact` when planning validation.
- Prefer the project-specific commands returned by `cs_test_impact`; only run broad solution tests when the impact output is insufficient or full-suite validation is intentional.
- Call `cs_refactor_preview` before editing files.
- Use `cs_apply_workspace_edit` only when it is explicitly enabled with `--enable-apply` or `CODEX_ROSLYN_ENABLE_APPLY=1`, and the user approves applying the cached preview.
- Keep results compact first and request source only for files that need edits.
- Review changed files, compact diff preview, risk reasons, and diagnostics before making edits.
- Do not assume MCP apply is available; apply tools are not enabled by default.
