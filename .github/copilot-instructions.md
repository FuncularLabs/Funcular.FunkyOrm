# Copilot Instructions

## General Guidelines
- Verbosity and completeness of examples is preferred over succinctness, especially in agent-focused documentation (e.g., FUNKYORM_AI_INSTRUCTIONS, AI_ARCHITECTURE_AND_DESIGN).
- For human-facing documentation (e.g., README, Usage.md, Changelog), a moderate level of verbosity is also preferred.

## Code Style
- Use specific formatting rules.
- Follow naming conventions.

## Project-Specific Rules
- There is no provider-specific NuGet package; `Funcular.Data.Orm` is the only published package, and it bundles all providers (SQL Server, PostgreSQL, SQLite). Adopt and maintain this pattern for all future providers.
- Provider-specific methods that work with one provider but throw `NotImplementedException` or `NotSupportedException` on another provider must have XML documentation comments explicitly stating which providers do not support the method and that it will throw.
- Provider-specific methods that work with SQL Server but throw `NotImplementedException` or `NotSupportedException` on another provider must have XML documentation comments explicitly stating which providers do not support the method and that it will throw.
- Do NOT use `.Value` or `.HasValue` on nullable properties in LINQ expressions. The ORM automatically unwraps nullable types.
- When using `list.Contains()` with a nullable entity property, cast the list to match the nullable type (e.g., `list.Cast<int?>().ToList()`) rather than unwrapping the property.
