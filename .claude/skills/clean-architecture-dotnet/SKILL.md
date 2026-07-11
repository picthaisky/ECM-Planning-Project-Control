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
