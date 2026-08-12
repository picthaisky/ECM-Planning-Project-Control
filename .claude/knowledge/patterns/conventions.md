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

## UI visual patterns (promoted from the working prototype, 2026-07-12)

Source: `docs/ECM Planning Prototype.dc.html` (ADR-0006). Full token table and screen
inventory live in the `/cmplus-ui` skill — this entry is the pointer/reminder.
- Navy `#0F2542` / gold `#C9A227` theme, IBM Plex Sans Thai + Bai Jamjuree fonts — not the
  orange-brown/Inter combination described in `docs/3.` (superseded).
- Reusable pieces: StatTile, StatusPill, DataTable (header `#F7F8FA`), progress-bar table
  cell, Gantt bar layering (critical/non-critical/baseline/data-date line), bottom-right toast.
- 13-screen nav is canonical; see `.claude/knowledge/domain/modules-map.md` for the two
  open reconciliation questions against the 15-module doc list.

<!-- knowledge-curator: append promoted patterns below -->

## Structural append-only enforcement (promoted 2026-08-11, used across 12+ entities)

A doc comment saying "append-only, no mutator" is not enforced — a raw `DbContext` can still track
an entity as `Modified`/`Deleted` regardless of what its public API exposes (proven by execution,
Sprint 9 M-01 and later). Use the marker interfaces + `SavingChanges` interceptor instead of a new
convention each time:
- `IAppendOnly` (`CMPlus.Domain.Common`) + `AppendOnlyGuardInterceptor` — rejects `Modified`/
  `Deleted` outright. Use for legal-evidence rows: `ApprovalAction`, ledgers, `AuditLog`.
- `INeverModified` — blocks only `Modified`, leaves `Added`/`Deleted` legal. Use for
  snapshot-shaped children that are legitimately cleared-and-rebuilt as a set but never field-edited,
  e.g. `PaymentCertificateApprovalStep`/`VariationOrderApprovalStep` on `ReturnForRevision`.
Full detail: `.claude/knowledge/lessons/lessons-learned.md` (2026-08-11). Also in
`/clean-architecture-dotnet`.

## Resolve authority once, snapshot it — never re-derive from mutable state

Approval-step authority must be computed once (at `Submit`) and stored on the document (chain
snapshot + `ApprovalPolicyVersionId`/`ApprovalPolicyId`), never recomputed from the live policy at
approve/reject time. `PaymentCertificate`'s Sprint 9 defect (H-01: re-deriving authority from a
policy's *entire* rule set, not the amount band that actually routed the document, let a QS clear a
step reserved for a PM on a real payment) was fixed exactly this way, and `VariationOrder` was built
with the snapshot from day one, avoiding the bug class entirely. Applies to any workflow where "who
is currently authorized" must equal "who was authorized when this document was routed" — re-deriving
from mutable configuration is the recurring root cause of privilege-escalation-shaped defects here.
See ADR-0008, sprint-09.md H-01.

## Test-fixture isolation for pipeline/transaction behaviour

Never modify the shared `CustomWebApplicationFactory`/`CmPlusDbContext` registration to satisfy one
test's needs — it is reused by the entire Integration suite and a scoped-looking change (e.g. one
more `ConfigureWarnings` ignore) can break unrelated test classes non-deterministically (verified:
188/544 tests, see lessons-learned.md 2026-08-11). Instead: (a) guard the production code path on
`Database.IsRelational()` so EF Core InMemory skips transaction/ordering logic it cannot support
rather than opening a transaction just to suppress a warning about it doing nothing
(`BaselineRepository.TryActivateAsync`); (b) if a test genuinely needs different factory
configuration, call `WithWebHostBuilder(...)` from inside that test class to layer config onto a
cloned factory instance (`LoginRateLimiterTests`, `FallbackPolicyTests`) rather than editing the
shared one. Pair with the InMemory-is-not-the-database rule: any assertion about a unique index,
rollback, or NULL-column uniqueness must run on SQLite/relational, not InMemory
(lessons-learned.md 2026-08-10, 2026-08-11; ADR-0021).
