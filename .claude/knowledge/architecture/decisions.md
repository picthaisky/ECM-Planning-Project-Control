# Architecture Decision Records (ADR)

Append-only log. Format per entry:

```
## ADR-NNNN: <title>
Date: YYYY-MM-DD · Status: Accepted | Superseded by ADR-XXXX
Context: <the problem/forces>
Decision: <what we chose>
Consequences: <what this binds us to>
```

Agents must not contradict an Accepted ADR; to change one, write a new ADR that supersedes it.

---

## ADR-0001: Clean Architecture + DDD with CQRS/MediatR on .NET 10
Date: 2026-07-11 · Status: Accepted
Context: docs/3. mandates strict layering for an 8-month, 4-phase build with parallel AI agents; parsers/engines must stay testable and swappable.
Decision: Four projects (Domain / Application / Infrastructure / WebApi); CQRS via MediatR; FluentValidation in Application; EF Core 10 + MSSQL in Infrastructure only. CPM/EVM engines are pure Application services.
Consequences: More boilerplate per feature, but agents can work in parallel without layer conflicts; engines unit-test without DB.

## ADR-0002: Multi-tenant isolation via TenantId + EF global query filters
Date: 2026-07-11 · Status: Accepted
Context: docs/4. specifies multi-tenant SaaS; cross-tenant leakage of contract/payment data is the worst-case security failure.
Decision: Every tenant-owned table carries `TenantId` (indexed, leading column of composite indexes); a global EF query filter bound to `ITenantProvider` applies on all reads; writes stamp TenantId server-side, never from client input.
Consequences: Single database, simple ops; security-auditor treats any unfiltered query as release-blocking; admin cross-tenant tooling needs explicit filter bypass with audit.

## ADR-0003: MS Project integration via MSPDI XML (MPXJ), never binary .MPP or interop
Date: 2026-07-11 · Status: Accepted
Context: docs/วิเคราะห์ฯ §2 — binary .MPP parsing/interop is unstable and slow in cloud containers.
Decision: Import/export MS Project data through MSPDI XML using MPXJ.Net; XER parsed natively (tab-delimited %T tables); Excel via EPPlus.
Consequences: Users export .mpp → MSPDI when needed; XML hardening (XXE, size caps) is mandatory on the import path.

## ADR-0004: Virtualized rendering for Gantt and large tables
Date: 2026-07-11 · Status: Accepted
Context: docs/2. risk #1 — browser performance with 10,000+ activities is a High risk.
Decision: react-window-style row virtualization for all large lists and the Gantt; bars drawn on a canvas/single-SVG layer; heavy series computation in Web Workers.
Consequences: Gantt components must keep row props stable/memoized; no DOM-per-bar designs will pass review.

## ADR-0005: Offline-first site modules via IndexedDB outbox + Background Sync
Date: 2026-07-11 · Status: Accepted
Context: docs/1. §5 and docs/2. risk #3 — site connectivity is unreliable; photo/weather/progress capture must not block on network.
Decision: Site-facing writes enqueue into an IndexedDB outbox (client-compressed photos included) and flush via Service Worker Background Sync; server wins on schedule data, last-write-wins + audit on site logs.
Consequences: Every site-module feature ships with offline tests; sync status must be visible in the UI.
