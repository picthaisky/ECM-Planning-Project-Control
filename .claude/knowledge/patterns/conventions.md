# Coding Patterns & Conventions

Enforced by code-reviewer. knowledge-curator promotes patterns here once they've proven out
(used ≥ 2 times), and folds mature ones into the relevant skill.

## Data types (everywhere, both stacks)
- IDs `Guid` · dates `DateTimeOffset` (UTC transport, Thai locale display) ·
  money `decimal(18,2)` · percent `decimal(5,2)` · no floats for money, ever.
- API JSON: camelCase; dates ISO 8601 with offset; money as string-safe decimal (no float rounding).

## Naming
- Backend: `<Verb><Entity>Command/Query` + `Handler` + `Validator`, feature-foldered
  (`Features/Planning/Commands/CreateActivity/`).
- Frontend: feature folders kebab-case (`site-logs`), components PascalCase, hooks `useX`.
- Spec artifacts: `docs/specs/<feature-slug>/{story,domain-rules,design}.md`.

## Error handling
- Application returns `Result<T>` for expected failures; exceptions reserved for bugs/infrastructure.
- API errors: ProblemDetails only; never leak stack traces or SQL.
- UI: every async surface has loading / empty / error / offline states.

## Mandatory cross-cutting
- Tenant scoping on every query (ADR-0002); audit log on every mutation.
- Weather logs and submitted payment certificates are immutable — corrections are new entries.
- Excel/CSV export escapes cells starting with `=`, `+`, `-`, `@`.

## Git
- Branch from `main`; PR per feature slug; commits imperative English; never merge without human approval.

<!-- knowledge-curator: append promoted patterns below -->
