---
name: clean-architecture-dotnet
description: Backend layering and CQRS patterns for the CM+ .NET 10 solution — project structure, layer rules, MediatR command/query conventions, EF Core configuration, and audit/tenant cross-cutting patterns. Load before writing or reviewing any backend code.
---

# CM+ Backend: Clean Architecture + DDD (.NET 10)

## Solution layout

```
src/
├── CMPlus.Domain/            # Entities, ValueObjects, DomainEvents, Exceptions — NO external deps
├── CMPlus.Application/       # UseCases (CQRS), DTOs, Validators, Interfaces, CPM/EVM engines
│   ├── Common/               #   Behaviors (validation, logging, audit), Interfaces
│   └── Features/<Module>/    #   Commands/ Queries/ per module (Planning, Evm, SiteLogs, ...)
├── CMPlus.Infrastructure/    # EF Core DbContext, Configurations/, Parsers/ (Xer, Mspdi, Excel),
│                             #   Storage/, Auth/, Migrations/
└── CMPlus.WebApi/            # Controllers (versioned), Middleware, ProblemDetails mapping
tests/
├── CMPlus.Domain.Tests / CMPlus.Application.Tests / CMPlus.Integration.Tests
```

## Layer rules (violations are review-blocking)

1. Dependencies point inward: WebApi → Application → Domain; Infrastructure → Application interfaces.
2. Domain never references EF, MediatR, or any package; invariants enforced in entity methods
   (`Activity.RecordProgress(...)` validates 0–100, emits `ActivityProgressUpdated` event).
3. Application defines interfaces (`IAppDbContext`, `IFileStorage`, `IXerParser`); Infrastructure implements.
4. Controllers: parse request → `ISender.Send()` → map result. No logic, no EF types.

## CQRS conventions (MediatR)

- Command: `CreateActivityCommand : IRequest<Result<Guid>>` + `CreateActivityCommandValidator`
  (FluentValidation) + handler in the same feature folder.
- Query: `GetWbsTreeQuery : IRequest<WbsTreeDto>`; handlers use `AsNoTracking()` + projection
  (`Select` into DTOs) — never return entities.
- Pipeline behaviors (registered order): Validation → Authorization/Tenant → Audit → Handler.
- Results: use a `Result<T>` type for expected failures; exceptions only for the unexpected.

## Cross-cutting patterns

- **Tenant:** `ITenantProvider` injected into DbContext; global query filter
  `entity.HasQueryFilter(e => e.TenantId == _tenant.Id)` on every tenant-owned entity.
- **Audit:** SaveChanges interceptor writes `AuditLog(entity, action, userId, tenantId, before/after JSON, timestamp)` for every mutation.
- **Types:** `Guid` keys, `DateTimeOffset` dates, `decimal(18,2)` money, `decimal(5,2)` percent —
  configured via Fluent API value conversions/column types, never data annotations in Domain.
- **Engines:** CPM and EVM calculators are pure, stateless Application services taking in-memory
  graphs/collections — fully unit-testable without a database.
- **Performance:** WBS tree in one query (materialized path or recursive CTE per ADR), paginate
  activity lists, `AsSplitQuery` for wide includes.
- **Append-only enforcement:** a doc comment claiming "append-only" is not enforcement — an ordinary
  `DbContext` can still track the entity as `Modified`/`Deleted`. Implement `IAppendOnly` (rejects
  `Modified`/`Deleted`) or the narrower `INeverModified` (blocks only `Modified`, for
  clear-and-rebuild snapshot children) and let the registered `AppendOnlyGuardInterceptor` enforce it
  at `SavingChanges`. Reuse these two markers for any new legal-evidence or snapshot entity rather
  than re-deriving the pattern — see `.claude/knowledge/patterns/conventions.md`.
- **Authority resolution:** compute "who may act on this document" once, at submission, and persist
  the resolved chain/policy version on the document. Never re-derive authority from the *current*
  policy at approve/reject time — that re-derivation is this codebase's recurring
  privilege-escalation bug class (ADR-0008; sprint-09.md H-01).

## Testing patterns

- **EF Core InMemory does not prove a storage-engine guarantee.** It ignores unique indexes
  entirely and has no transaction support, so it cannot validate rollback-on-failure, unique-index
  enforcement (including the NULL-discriminator gap — ADR-0021), or SQL Server statement-ordering
  behaviour. Verify those specifically on SQLite (or a real SQL Server run) and label
  InMemory-only results as "logic verified, storage guarantee not verified."
- **Guard transactional/ordering-sensitive repository code on `Database.IsRelational()`** so
  InMemory skips logic it cannot support (`BaselineRepository.TryActivateAsync`), rather than
  opening a transaction/suppressing a warning just to keep InMemory quiet.
- **Never edit the shared `CustomWebApplicationFactory`/`CmPlusDbContext` registration for one
  test's needs** — it is reused by the whole Integration suite and a "small" change to it can break
  unrelated test classes non-deterministically. Use `WithWebHostBuilder(...)` inside the specific
  test class to layer config onto a cloned factory instead (`LoginRateLimiterTests`,
  `FallbackPolicyTests`).
- **Never trust a passing suite as proof a guard works.** Mutate the implementation (delete/invert
  the guarded branch) and confirm a test fails; assert on the durable artifact (stored bytes, row
  counts) rather than an in-memory return value; include at least one fixture where the "obvious"
  shortcut (same role, same tenant, same tier) does not hold.
