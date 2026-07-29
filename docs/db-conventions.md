# Database Conventions — CM+ Project Control

**Author:** `database-engineer` · **Task:** P0-DB-01 (`docs/10.` §5) · **Status:** Accepted baseline for Sprint 1+

**Upstream:** `docs/4.` §1 (schema), `docs/specs/master-plan/design.md` §3, ADR-0002 (tenant isolation),
ADR-0007/ADR-0009 (EAC/progress fields), ADR-0008 (RowVersion), ADR-0010 (cloud-agnostic containers).

This is the canonical DB-level reference. It does not repeat formulas (`.claude/knowledge/domain/*`)
or the API contract (`design.md` §2) — only schema/index/migration/operational rules that every
EF Core configuration and migration must follow. Every accepted rule here binds
`database-engineer` (later sprints) and `backend-developer` (EF configurations) equally; do not
re-derive these decisions per feature.

---

## 1. Naming conventions

EF Core 10 Code-First, so C# naming *is* the source of truth; SQL names follow by convention.

| Element | Convention | Example |
| --- | --- | --- |
| Table | PascalCase, pluralized entity name (EF Core default pluralization) | `Projects`, `WBSNodes`, `Activities`, `ActivityRelations` |
| Column | PascalCase, exact C# property name, no abbreviations | `TenantId`, `PlannedFinishDate`, `BudgetAtCompletion` |
| Primary key | `Id` (`Guid`), single surrogate key per table | `Id` |
| Foreign key | `<ReferencedEntitySingular>Id`; self-referencing FKs get a descriptive prefix, never bare `Id` | `ProjectId`, `WbsNodeId`, `ParentWbsNodeId` |
| FK constraint | EF Core default (`FK_<Table>_<ReferencedTable>_<Column>`) — do not override without a naming collision | `FK_WBSNodes_WBSNodes_ParentWbsNodeId` |
| Index | EF Core default (`IX_<Table>_<Col1>_<Col2>…`); append `_Active`/`_Unique` only when the default name would collide across a filtered vs. non-filtered index on the same columns | `IX_ActivityProgressLog_TenantId_ActivityId_PeriodEndDate` |
| Enums | Native `int` mapping (EF Core default); never free-text `nvarchar` for an enum | `Activity.RelationType` → `int` |
| Append-only tables | Keep the name already fixed by `docs/10.` §3 (`Log`/`Action` suffix) — do not rename | `ActivityProgressLog`, `ApprovalAction`, `AuditLog` |
| Schema | Single `dbo` schema for all application tables — no per-module schemas | — |

**Convention decision (not fully dictated upstream):** `ActivityRelation` column names are not
spelled out in `docs/4.` beyond the Predecessor/Successor concept in the CPM formulas. Adopt
`PredecessorActivityId`, `SuccessorActivityId`, `RelationType` (`FS`/`SS`/`FF`/`SF` enum),
`LagDays` (`int`, signed — negative = lead). Flag for `backend-developer`/`domain-expert` to
confirm at Sprint 1 entity design; not a blocker, but should be confirmed rather than silently
assumed by whoever writes the entity first.

---

## 2. Multi-tenant isolation (ADR-0002) — hard rules

These are release-blocking if violated, not style preferences.

1. Every tenant-owned table has `TenantId Guid NOT NULL`.
2. **`TenantId` is the leading (leftmost) column of every composite index on every tenant-owned
   table.** No exceptions — SQL Server's leftmost-prefix rule means this also serves any
   single-column `TenantId` lookup, so a table only needs a *separate* single-column `TenantId`
   index if it has no natural composite index otherwise.
3. A global EF Core query filter (`HasQueryFilter`, bound to `ITenantProvider`) applies to every
   entity implementing `ITenantOwned`. Any `IgnoreQueryFilters()` call must be grep-able and is a
   mandatory `security-auditor` review item — never introduced casually for convenience.
4. `TenantId` is stamped server-side only (from the authenticated user's JWT claim via
   `ITenantProvider`), enforced in the `SaveChanges` pipeline / command handler — a `TenantId` in
   a request payload is always ignored, never trusted.
5. Non-tenant-owned tables (e.g. `Tenant` itself, `__EFMigrationsHistory`) are explicitly exempt;
   note the exemption in the entity configuration with a comment so it reads as a deliberate
   decision, not an oversight.

---

## 3. Data type conventions (canonical DB-level reference)

Restated here so no agent has to re-derive them from CLAUDE.md/`docs/9.`/`design.md` per feature:

| Domain concept | C# type | SQL type | Notes |
| --- | --- | --- | --- |
| Id / FK | `Guid` | `uniqueidentifier` | see ID-generation note below |
| Money | `decimal` | `decimal(18,2)` | never `float`/`double`; never SQL `money` (4-decimal rounding quirks) |
| Percent | `decimal` | `decimal(5,2)` | domain layer clamps `[0,100]`; DB adds `CHECK` as defense-in-depth (§3.1) |
| Performance Factor (stored, e.g. `Project.EacCustomPerformanceFactor`) | `decimal` | `decimal(9,4)` | ADR-0007 |
| Performance Factor (snapshot, `EvmPeriodSnapshot.PerformanceFactor`) | `decimal` | `decimal(9,6)` | ADR-0007 — higher precision so a closed-period audit trail is reproducible |
| Date/time (any business timestamp) | `DateTimeOffset` | `datetimeoffset(7)` (SQL Server default precision — do not narrow) | never bare `datetime`/`datetime2`; offset matters even though the platform is currently single-timezone (Thailand), so timestamps stay unambiguous if that changes |
| Boolean | `bool` | `bit` | e.g. `Activity.IsCritical`, `ApprovalPolicy.IsActive` |
| Short text (codes/names) | `string` | `nvarchar(50)` / `nvarchar(250)` per `docs/4.` field list | always `nvarchar`, never `varchar` — Thai text must round-trip everywhere |
| Long/unbounded text | `string` | `nvarchar(1000)` or `nvarchar(max)` only when genuinely unbounded | e.g. approval `Comment` |
| Enum | C# `enum` | `int` | see naming table |
| Optimistic concurrency token | `byte[]` | `rowversion` | §4 |

### 3.1 Convention decision — CHECK constraints as defense-in-depth

Domain invariants (percent clamped `[0,100]`, money non-negative where applicable) are enforced
in the Domain layer per `docs/10.` (S1-BE-01). This document adds a DB-level recommendation, not
yet mandated: add a `CHECK` constraint mirroring the domain invariant on any percent/money column
that is safety- or finance-critical (e.g. `ProgressPercentage`, `WeightPercentage`,
`RetentionCapPercentage`). Rationale: the Domain layer protects the API path; a `CHECK` protects
against any future direct-SQL fix, bulk import staging bug, or bad backfill script bypassing the
Domain layer. Not retrofitted onto Phase 0 (no entities exist yet) — apply per-table starting
Sprint 1 as each table's EF configuration is written; does not block Sprint 1 if a specific
column is missed, but should be treated as part of "done" for finance/percent columns going
forward.

### 3.2 Convention decision — ID generation should favor time-ordered GUIDs

`docs/4.`/CLAUDE.md fix the **logical** key type as `Guid`; they do not specify **how** the GUID
is generated. This matters for performance at the scale this system targets (10,000+ activities,
~350k `ActivityProgressLog` rows/project per ADR-0009 risk R-11): a `Guid` primary key is, by EF
Core default, also the table's **clustered index** key. If IDs are generated with
`Guid.NewGuid()` (random, RFC 4122 v4), every insert lands at a random point in the clustered
index, causing page splits and index fragmentation that gets worse exactly as tables grow —
directly working against the "WBS tree < 100 ms" and "10,000+ activities" performance budgets in
CLAUDE.md.

**Recommendation (flag for `backend-developer` confirmation at Sprint 1 domain-entity design,
since IDs are generated in the Domain layer, not the DB):** generate entity IDs with
`Guid.CreateVersion7()` (RFC 9562 UUIDv7, time-ordered, available in .NET 9+/10) instead of
`Guid.NewGuid()`. This keeps IDs globally unique and still generatable in the Domain layer before
`SaveChanges()` (needed for aggregate construction, e.g. a `WBSNode` referencing its own `Id`
before insert), while making inserts append-mostly-sequential in the clustered index — no DB-side
`NEWSEQUENTIALID()` is usable here precisely because the application, not the database, assigns
the ID. This is not yet enforced anywhere (no entities exist in Phase 0); it is a recommendation
this document is flagging now so Sprint 1 doesn't have to rediscover the fragmentation problem
after `ActivityProgressLog` already has hundreds of thousands of rows.

---

## 4. RowVersion / optimistic concurrency policy

- ADR-0008/`design.md` §3: `RowVersion` (SQL `rowversion`, EF Core `IsRowVersion()`) is mandatory
  on `PaymentCertificate` and `VariationOrder` — both are read-modify-write targets of a
  multi-step, multi-user approval chain, so last-writer-wins would silently drop an approval
  action.
- Policy for future entities: add `RowVersion` from the migration that introduces any entity that
  is (a) mutated by more than one role/user across its lifecycle **and** (b) has a multi-step
  workflow or realistic concurrent-edit window. Don't retrofit reactively after a concurrency bug
  is reported.
- Explicitly *not* requiring `RowVersion`: `ApprovalPolicy` — its own `Version+1`-on-edit pattern
  (ADR-0008) already is the concurrency control (never mutate a policy row in place); adding
  `RowVersion` on top would be redundant.
- Conflict handling: EF Core raises `DbUpdateConcurrencyException` on a `RowVersion` mismatch →
  Application layer maps this to HTTP `409` (`concurrent-transition` / `document-immutable`,
  already specified in `design.md` §2.3). The DB-layer contract is simply "the column exists and
  is mapped correctly on every write path" — handling is `backend-developer`'s responsibility.

---

## 5. Migration policy

- EF Core Code-First; migrations live in `backend/src/CMPlus.Infrastructure/Migrations/`.
- **Additive and reversible.** Every `Up()` ships a working, tested `Down()`. No destructive
  column drops or type-narrowing against a database that may hold real data without an explicit
  human-approved plan (CLAUDE.md non-negotiable). Once real user data exists, a rename/type
  change is create-new-column → dual-write/backfill → cut over → drop-old, spread across
  releases — never a single in-place `RenameColumn`/`AlterColumn` that risks data loss.
- **Idempotent SQL export is mandatory (ADR-0010).** Every migration must be producible as a raw,
  idempotent SQL script:
  ```
  dotnet ef migrations script --idempotent --output artifacts/migrations/<YYYYMMDD>_<MigrationName>.sql
  ```
  `--idempotent` wraps each migration step in a check against `__EFMigrationsHistory`, so applying
  the same script twice — or applying it to a database that already has some of the migrations —
  is a safe no-op. This script, not `dotnet ef database update` run ad hoc, is what:
  - CI's migration smoke test (`docs/10.` S1-QA-03) applies against a fresh MSSQL container on
    every PR touching `Migrations/` or entity configurations;
  - the human-approved production/staging apply job (ADR-0010(c)) executes.
  - **Filtered indexes require `QUOTED_IDENTIFIER ON` (Sprint 2 finding).** Plain `sqlcmd -i
    script.sql` runs with it OFF by default, which SQL Server rejects for any `CREATE INDEX ...
    WHERE` statement — the first migration with a filtered index (Sprint 2's unique index on
    `ApprovalPolicy`, S2-DB-02) is where this first bites. The production/staging apply job must
    use `sqlcmd -I` (capital I) or an ADO.NET-based runner (which defaults this setting ON), never
    bare `sqlcmd -i`.
- **Exported scripts land in `artifacts/migrations/`** (git-tracked), one file per migration by
  default — matches the granularity CI checks (S1-QA-03 applies "the migration" singular per PR).
  This directory does not exist yet (no migrations exist until Sprint 1); `S1-DB-01` creates both
  the first migration and its exported script (`artifacts/migrations/initial.sql`, per `docs/10.`
  §6 Artifacts column).
- **Seed data must be idempotent.** Check-before-insert, or `HasData`/`MERGE` keyed on stable,
  fixed `Guid`s — never a bare `INSERT` when the seeder can plausibly run more than once (local
  container restart, CI re-run, `S1-DB-03` dev seed).
- **Schema freeze checkpoints** (`design.md` §5): the Sprint 1 migration (Project/EAC/finance
  columns + `ActivityProgressLog`) and the Sprint 2 migration (approval tables) are the frozen
  core — after Sprint 2, altering an already-shipped table requires a new ADR, not just a
  migration. Net-new additive tables/columns for not-yet-built modules (`EvmPeriodSnapshot` at
  Sprint 7, `ProjectFinanceLedger` at Sprint 9, etc., per `docs/10.` §3) are expected and are not
  a schema-freeze violation.

---

## 6. Mandatory indexes (restated as a DB-layer checklist from `design.md` §3 / ADR-0002 / ADR-0009)

| Table | Index | Purpose |
| --- | --- | --- |
| every tenant-owned table | `TenantId` leading column of a composite index, or its own index if no composite exists | ADR-0002 |
| `ActivityProgressLog` | `(TenantId, ActivityId, PeriodEndDate DESC)` | drives the ADR-0009 step-function read (`GetProgressAsOf`) — must be an index **seek** |
| `ActivityProgressLog` | `(TenantId, PeriodEndDate)` | period rollups |
| `WBSNode` | `(TenantId, ProjectId, ParentWbsNodeId)` | hierarchy reads, WBS tree < 100 ms budget |
| `ActivityRelation` | index on `(TenantId, PredecessorActivityId)` and `(TenantId, SuccessorActivityId)` | CPM forward/backward pass over 10,000+ activities / 15,000+ relations must not scan |
| `EvmPeriodSnapshot` | unique `(TenantId, ProjectId, DataDate)` | one snapshot per project per data date |
| `ApprovalPolicy` | unique **filtered** `(TenantId, ProjectId, DocumentType) WHERE IsActive = 1` | two active policies for one scope is a data-integrity bug, not a runtime tie-break |
| `ApprovalAction` | `(TenantId, DocumentType, DocumentId, RevisionNo, StepNo)` | approval history lookups |
| `ProjectFinanceLedger` | `(TenantId, ProjectId, Category)` | `SUM()` of retention/advance must seek |
| `VariationOrder` | `(TenantId, ProjectId, Status)` | cumulative approved-VO sum runs on every VO submit |

Additional rules:
- **Never `SELECT *`** — every query projects only the columns it needs, especially on
  high-row-count tables (`ActivityProgressLog`, `ApprovalAction`).
- **Page everything that can exceed ~200 rows** (`OFFSET/FETCH` or keyset pagination), matching
  the frontend's virtualization contract (ADR-0004) and any server-side aggregate.
- **"Index seek" DoDs are literal, not aspirational.** Any composite/covering index introduced by
  any sprint must be verified with an actual execution plan (`SET STATISTICS XML ON` or
  `sys.dm_exec_query_stats`) showing **Seek**, not **Scan**, at representative volume (10,000+
  activities, 350,000+ progress-log rows per project) before merge — this is the concrete meaning
  behind the repeated "index seek" Definition of Done language throughout `docs/10.` §6+.

---

## 7. Append-only tables — no update/delete path

`ActivityProgressLog`, `ApprovalAction`, `ProjectFinanceLedger`, `DailyWeatherLog`,
`EvmPeriodSnapshot`, `AuditLog` are insert-only for the life of the system. Corrections are always
compensating rows (new `INSERT`), never `UPDATE`/`DELETE` — including by a human/DBA out-of-band;
if that ever happens it is an incident, not routine maintenance.

**Application-layer enforcement** (no update/delete method exposed on the entity, no
API surface for it) is the primary control and is `backend-developer`'s responsibility per
`docs/10.` (e.g. S1-BE-02 for `ActivityProgressLog`).

**Convention decision — DB-layer defense in depth (recommended, not yet wired up):** once these
tables exist (Sprint 1+), grant `DENY UPDATE, DELETE ON <table> TO cmplus_app` (or an equivalent
role-based grant) for the application's runtime SQL login — the same `cmplus_app` login created
by `infra/docker/mssql/init/02-create-app-login.sql`, which currently has blanket `db_owner` for
Phase 0 simplicity (no schema exists yet to scope a narrower grant to). This is flagged as a
Sprint 1/2 `database-engineer` follow-up alongside the migration that creates each table, not
implemented now because there is nothing to grant against yet.

---

## 8. Bulk-operation guidance (flagged now for Sprint 3 file import, Sprint 5 CPM recalculation)

Row-by-row `SaveChanges()` for 10,000+ activities/relations will not meet performance budgets and
thrashes the EF Core change tracker. Flagging this now so those sprints don't reinvent it under
schedule pressure:

- **Anti-pattern to avoid:** `foreach (var x in items) { x.Prop = ...; await ctx.SaveChangesAsync(); }`
  for any `N` beyond roughly 100 rows.
- **Bulk insert** (Sprint 3 XER/MSPDI/Excel import staging thousands of activities/relations):
  `AddRange` + a single `SaveChangesAsync()` with `ChangeTracker.AutoDetectChangesEnabled = false`
  during the bulk add, or raw `SqlBulkCopy` for pure staging-table loads. Whether to adopt a bulk
  library (e.g. EFCore.BulkExtensions) is a NuGet dependency decision for
  `backend-developer`/`system-architect` at Sprint 3, not pre-selected here.
- **Bulk update** (Sprint 5 CPM engine writing `EarlyStart/EarlyFinish/LateStart/LateFinish/
  TotalFloat/IsCritical` back to thousands of `Activity` rows after a recalculation): prefer
  EF Core 10's `ExecuteUpdateAsync` (set-based, translated to a single `UPDATE` statement) over
  per-entity tracked updates, when the update expression is uniform across the row set.
- **Tenant isolation still applies to bulk paths.** Build the target `IQueryable` through the
  normal `DbSet<T>` (which carries the global tenant query filter) and only then call
  `ExecuteUpdateAsync`/`ExecuteDeleteAsync` — never hand-write raw SQL without an explicit
  `WHERE TenantId = @tenantId`. A bulk operation must never span more than one tenant's rows in a
  single statement.
- **Audit still applies to bulk paths.** One business operation (e.g. "recalculate CPM for
  project X", "import 8,000 activities from file Y") still produces exactly one summarizing
  `AuditLog` entry (CLAUDE.md: every mutating domain operation writes an audit log entry) — bulk
  performance must not be achieved by bypassing audit.

---

## 9. Ownership / change control

Maintained by `database-engineer`. Changes to an already-accepted rule here need
`system-architect` review (cross-cutting impact) and, for anything touching tenant isolation or
the append-only `DENY` grants, `security-auditor` review. Superseding an ADR referenced here
(ADR-0002, ADR-0007, ADR-0008, ADR-0009, ADR-0010) requires a new ADR — never a silent edit to
this file.
