# FunkyORM Documentation

This folder consolidates all project documentation, implementation plans, and AI-facing instructions.

## Structure

```
docs/
├── architecture/          # High-level architecture & design philosophy
│   └── AI_ARCHITECTURE_AND_DESIGN.md
├── plans/                 # Active implementation plans (used in AI prompts)
│   ├── JSON_ATTRIBUTES_IMPLEMENTATION_PLAN.md
│   ├── POSTGRESQL_PROVIDER_IMPLEMENTATION_PLAN.md
│   ├── SQLITE_PROVIDER_IMPLEMENTATION_PLAN.md
│   ├── STORED_PROCEDURE_EXECUTION_PLAN.md
│   └── _completed/       # Shipped/historical plans (retained for reference)
│       ├── 3.0.0-final-refactoring-plan.md
│       ├── 3.0.0-refactor.md
│       └── RemoteKeyFeatureSpec.md
├── ai-instructions/       # Detailed AI context docs (referenced from .github/copilot-instructions.md)
│   ├── FUNKYORM_AI_INSTRUCTIONS.md
│   ├── FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md
│   └── FUNKYORM_AI_INSTRUCTIONS_SQLITE.md
```

## Conventions

- **Active plans** go in `docs/plans/`. Name them `FEATURE_NAME_IMPLEMENTATION_PLAN.md`.
- **Completed plans** move to `docs/plans/_completed/` when the feature ships.
- **AI instructions** that are too large for `.github/copilot-instructions.md` go in `docs/ai-instructions/`.
- **Architecture docs** describing overall design go in `docs/architecture/`.
- The root `README.md`, `Usage.md`, and `Changelog.md` stay at repo root (GitHub convention).
- `.github/copilot-instructions.md` and `.github/prompts/` stay in `.github/` (Copilot convention).

## Migration Notes (v3.5.1)

Old locations have been marked with deprecation notices and retained for backward compatibility:
- `SolutionItems/` → `docs/architecture/`
- `Funcular.Data.Orm.SqlServer/_agent/plans/` → `docs/plans/`
- `Funcular.Data.Orm.*/FUNKYORM_AI_INSTRUCTIONS*.md` → `docs/ai-instructions/`
- `Prompts/` → `docs/plans/_completed/`
- Root `RemoteKeyFeatureSpec.md` → `docs/plans/_completed/`

These old copies can be deleted once all team members and tooling have updated their references.
