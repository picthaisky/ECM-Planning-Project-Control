# CM+ Project Control — Detailed Sprint Backlog (Master Plan Expansion)

**Status:** Execution-grade backlog · **Author:** po-analyst · **Date:** 2026-07-27
**Expands:** `docs/9. แผนพัฒนาระบบฉบับสมบูรณ์ (Master Development Plan).md` §7 (Phase 0–4, Sprint 1–16)
**Grounded in:** `docs/1.`, `docs/2.`, `docs/6.`, `docs/9.`, `docs/ECM Planning Prototype.dc.html`,
`.claude/knowledge/domain/evm-formulas.md`, `.claude/knowledge/domain/cpm-method.md`,
`.claude/knowledge/architecture/decisions.md` (ADR-0001–0006), `.claude/skills/cm-domain/SKILL.md`

**Scope of this document:** translate 4 human decisions (2026-07-27) plus a definitive ruling on
the two lingering `[ต้องยืนยัน]` items from docs/9 §11 into concrete, sprint-by-sprint user
stories with testable acceptance criteria. The 16-sprint cadence and 4-phase structure from
docs/9 §7 are unchanged; this document adds depth and places the one net-new capability (Tenant
Admin / RBAC approval-matrix settings) inside that existing cadence. It does **not** re-litigate
CPM/EVM/payment formulas already canonicalized in the knowledge base — those are referenced, not
re-derived.

Conventions used throughout (per `CLAUDE.md`): money `decimal(18,2)`, percent `decimal(5,2)`,
dates `DateTimeOffset`, IDs `Guid`. UI copy examples are Thai-first with English technical terms,
matching the prototype. Every story lists **Role**, **MoSCoW**, **Size (S/M/L)**, and
**Depends on**.

---

## 1. The 4 Human Decisions — How They Land in the Backlog

| # | Decision | Primary module(s) | Data model impact | Landing sprint(s) |
| :-- | :-- | :-- | :-- | :-- |
| 1 | EAC formula is **user-selectable** on Dashboard/EVM screen (not fixed default) | EVM S-Curve (#5), Executive Dashboard (#1) | `Project.DefaultEacVariant` (new) | Sprint 7 (engine + selector UI), consumed Sprint 8 (dashboard tiles) |
| 2 | Retention rate & Advance rate are **configurable per-project** | Project Info (#2), Payment Certificate (#7) | `Project.RetentionRate`, `Project.AdvanceRate` (already modeled in docs/9 §4 — this decision makes them **editable**, not display-only) | Sprint 4 (Project Info edit UI), consumed Sprint 9 (Payment engine) |
| 3 | VO / Payment Certificate approval authority is a **configurable per-tenant permission matrix**, not a hardcoded role check | **New:** Tenant Admin / RBAC Settings (net-new, beyond the 13-screen prototype) + Payment Certificate (#7) + Variation Order (#9) | **New** `ApprovalMatrixEntry` entity | Data model + enforcement hook: Sprint 2 (alongside RBAC/JWT foundation). Admin UI + first real consumer (Payment approve): Sprint 9. VO approve wired to the same matrix: Sprint 10. |
| 4 | Cloud provider (AWS vs Azure) deferred | DevOps/infra | — | No story in this backlog — explicitly out of scope here, tracked separately for Phase 4 infra planning (Sprint 16) |

**Rationale for Decision 3's placement:** the approval matrix is a hard dependency of Phase 3's
VO and Payment workflows (Sprint 9–10) — those sprints cannot ship a real "approve" button
without something to check permission against. But building a full Tenant Admin UI before any
RBAC foundation exists (Sprint 1–2) would be premature — there'd be no roles, no JWT claims, and
no consumer to validate against. The split below avoids both a late-Phase-3 blocker and
premature Phase-1 scope:
- **Sprint 2** (Phase 1, "JWT/RBAC auth" sprint in docs/9 §7): add the `ApprovalMatrixEntry` data
  model, seed sensible tenant defaults on tenant creation, and expose a read-only
  `GET /api/v1/tenants/{id}/approval-matrix` — no admin UI yet. This means by the time Sprint 9/10
  need it, the plumbing and defaults already exist and can simply be checked, not designed from
  scratch under schedule pressure.
- **Sprint 9** (Phase 3, Payment Certificate sprint): build the actual Tenant Admin editing UI
  (minimal but functional) because Payment Certificate approval is the first real consumer, and
  wire the Payment approve endpoint to check the matrix instead of a fixed role.
- **Sprint 10** (Phase 3, VO sprint): wire VO approve/reject to the same matrix (reusing Sprint 9's
  infrastructure); extend the admin UI only if VO needs matrix rows the Payment sprint didn't
  already create (it does — `Module = VariationOrder` rows).
- Full "tenant-admin polish" (bulk role templates, per-project override, matrix change history
  UI) is deliberately deferred to Phase 4 (Sprint 15, alongside the security-audit sprint) since
  it is hardening, not a Phase 3 blocker.

---

## 2. Resolving docs/9 §11 Item 1 — Executive Summary & Daily/Weekly Progress

The human asked for a definitive call, not another open flag. Here it is:

### 2.1 "สรุป (Executive Summary)" — default stands, **with one addition**
**Call:** No separate top-nav screen. The documented default (content lives on Executive
Dashboard) is correct for on-screen use. **However**, "folded into the dashboard" alone does not
close the actual pain point in `docs/1.` §2 (executives need something they can print/email to a
steering committee who won't log into CM+). Real practice needs a static, dated,
narrative-capable artifact, not just a live screen. **Add:** a "Export Executive Summary" action
on the Executive Dashboard producing a PDF snapshot (KPI tiles + S-Curve + rollups as they render
at export time) with an optional free-text PM commentary field, entered at export time. This is
a small, self-contained addition — see Sprint 8, US-8.4.

### 2.2 "ก้าวหน้า (Daily/Weekly Progress)" — default does **not** hold up; real risk flagged
**Call:** The documented default ("progress capture via direct WBS row edits") is rejected as
insufficient, for two concrete reasons grounded in the domain:
1. **Practice at scale:** site/planning engineers update progress for dozens–hundreds of
   activities at the end of each week, not one WBS tree row at a time. A tree-row-edit-only UX is
   unusable for a real weekly cycle, let alone the 10,000+-activity networks this platform targets.
2. **EVM/S-Curve historical accuracy:** the current docs/9 §4 data model stores only
   `Activity.ProgressPercentage` (current value, overwritten in place). EVM's PV/EV time series
   (S-Curve, §5 of docs/9) requires knowing progress **as of each past data date**, not just "now."
   Overwriting a single field with no dated history means historical S-Curve points can only be
   reconstructed by parsing `AuditLog.BeforeJson/AfterJson` — fragile, slow, and not something a
   reporting/EVM engine should depend on.

**Resolution:** keep the top-nav screen count at 13 (no new nav item — this is not a reversal of
the screen-count default), but add, inside the existing WBS & Activity screen (#3):
- A batch **"อัปเดตความคืบหน้า" (Update Progress)** mode — a filterable grid (by WBS branch/zone/
  responsible team), not one-row-at-a-time tree editing.
- A new **`ActivityProgressLog`** entity (dated, append-only, same immutability pattern as
  Weather Log) that becomes the system of record for progress-over-time; `Activity.ProgressPercentage`
  becomes a denormalized "latest value" cache updated whenever a new log entry is recorded.

This is flagged explicitly to **domain-expert**: confirm whether EVM's PV/EV computation at an
arbitrary historical data date should read from `ActivityProgressLog` snapshots (recommended) or
whether a different reconstruction rule is intended (see Open Questions §7, item D1).

---

## 3. Data Model Deltas vs docs/9 §4

| Entity | Change | Reason | Landing sprint |
| :-- | :-- | :-- | :-- |
| `Project` | Add `DefaultEacVariant` (string enum: `BacOverCpi` \| `Atypical` \| `Combined`, default `BacOverCpi`) | Decision 1 | Sprint 7 |
| `Project` | `RetentionRate decimal(5,2)`, `AdvanceRate decimal(5,2)` — already modeled; becomes editable via UI with validation + audit trail | Decision 2 | Sprint 4 |
| `ApprovalMatrixEntry` **(new)** | `Id (Guid)`, `TenantId (Guid)`, `Module` (enum: `VariationOrder` \| `PaymentCertificate`), `Role` (enum: PM/Planning/Site/QS/Executive/Admin — matches `User.Role`), `CanApprove (bit)`, `UpdatedAt (DateTimeOffset)`, `UpdatedByUserId (Guid)`. Unique composite index `(TenantId, Module, Role)` | Decision 3 | Sprint 2 (model), Sprint 9/10 (UI + enforcement) |
| `ActivityProgressLog` **(new)** | `Id (Guid)`, `TenantId (Guid)`, `ActivityId (Guid)`, `PeriodEndDate (DateTimeOffset)`, `ProgressPercentage decimal(5,2)`, `ActualQuantity (decimal(18,2), nullable)`, `RecordedByUserId (Guid)`, `RecordedAt (DateTimeOffset)`, `Source` (enum: `Manual` \| `Import` \| `PhotoLinked`) | §2.2 resolution | Sprint 1 (model), Sprint 4 (API + UI) |

All new tables carry `TenantId` per ADR-0002; a missing tenant filter on any of the above is a
release-blocking bug, same as every other table.

---

## Phase 0 — Setup (pre-Sprint 1)

No user-facing stories (infra/scaffolding only, per docs/9 §7). Two additions driven by this
backlog, to avoid later rework:
- Scaffold the `ApprovableModule` (`VariationOrder`/`PaymentCertificate`) and `EacVariant`
  (`BacOverCpi`/`Atypical`/`Combined`) enums in `CMPlus.Domain` shared kernel now, so Sprint 2 and
  Sprint 7 don't redefine them independently.
- Add empty EF Core migration checkpoints reserved for `ApprovalMatrixEntry` and
  `ActivityProgressLog` so `database-engineer` can slot them into Sprint 1/2 migrations cleanly.

---

## Phase 1 — Foundation & Data Sync (Sprint 1–4)

### Sprint 1 — Core domain entities, EF Core, tenant isolation

**US-1.1 — Core entities exist with tenant isolation**
As a backend developer (infrastructure story, no end-user role), I want `Project`, `WBSNode`,
`Activity`, `ActivityRelation`, `Tenant`, `User` entities modeled with EF Core and a global tenant
query filter, so that every later module has a safe, tested foundation.
- Given a `Project` entity, when created, then `RetentionRate` and `AdvanceRate` fields exist as
  `decimal(5,2)`, nullable at creation, defaulting to `null` (not silently 0 — a project without
  a rate set must be visually distinguishable from one deliberately set to 0%).
- Given any tenant-owned table, when queried without an explicit tenant filter override, then EF
  Core's global query filter restricts results to `ITenantProvider.CurrentTenantId`.
- Given two tenants A and B with projects, when tenant A's context queries `Project`, then zero
  rows from tenant B are returned (integration test, xUnit).
- Given a `WBSNode` cycle (node is its own ancestor), when saved, then the operation is rejected
  with a domain validation error, not a stack overflow.
Size: L · MoSCoW: Must · Depends on: none (first sprint)

**US-1.2 — `ActivityProgressLog` entity (§2.2 resolution)**
As a Planning Engineer, I want the system to store a dated history of progress entries per
activity (not just a single overwritten percentage), so that later EVM/S-Curve calculations can
reconstruct progress as of any past data date.
- Given an `Activity`, when a progress entry is recorded, then a new `ActivityProgressLog` row is
  appended with `PeriodEndDate (DateTimeOffset)`, `ProgressPercentage decimal(5,2)` (0.00–100.00),
  and `RecordedByUserId`; existing rows are never edited or deleted (immutable, same pattern as
  Weather Log).
- Given a new log entry with a later `PeriodEndDate` than any existing entry for that activity,
  when saved, then `Activity.ProgressPercentage` (denormalized cache) is updated to match.
- Given a log entry with a `PeriodEndDate` earlier than the latest existing entry (a late/backdated
  correction), when saved, then it is still appended and does **not** overwrite the cached
  "latest" value — flagged to QA as an explicit test case (out-of-order backfill).
- Given `ActualQuantity` is supplied, when persisted, then it stores as `decimal(18,2)`; when
  omitted, then it is `null`, not `0`.
Size: M · MoSCoW: Must · Depends on: US-1.1

**US-1.3 — Migration applies cleanly**
As a database engineer, I want the first EF Core migration (including the two new tables above)
to apply cleanly on a fresh MSSQL instance, so that Phase 1 is unblocked.
- Given a fresh database, when `dotnet ef database update` runs, then it completes with no manual
  intervention and all tenant-owned tables include an indexed `TenantId` column.
Size: S · MoSCoW: Must · Depends on: US-1.1, US-1.2

---

### Sprint 2 — JWT/RBAC auth, Audit interceptor, ProblemDetails middleware, **Approval Matrix data model**

> **NEW SCOPE CALLOUT:** the Approval Matrix data model and read endpoint below are net-new
> capability beyond the original 13-screen prototype (Decision 3). No admin UI ships this
> sprint — that lands in Sprint 9. This sprint only ensures Sprint 9/10 aren't blocked.

**US-2.1 — JWT authentication issues tenant + role claims**
As any authenticated user, I want to log in and receive a JWT containing my `TenantId`, `UserId`,
and `Role`, so that every subsequent request can be authorized without re-querying the database
for identity.
- Given valid credentials, when I log in, then the JWT includes `tenantId` (Guid), `role` (one of
  PM/Planning/Site/QS/Executive/Admin), and expires within the configured lifetime.
- Given an expired or tampered JWT, when any API call is made, then the response is `401` with a
  `ProblemDetails` body (no stack trace).
- Given a request body or query string that includes a `tenantId` parameter, when the request is
  processed, then the server-side tenant context is taken **only** from the JWT claim, never from
  client input (ADR-0002).
Size: M · MoSCoW: Must · Depends on: US-1.1

**US-2.2 — Audit interceptor on every mutation**
As a QS/Cost Engineer (or any role performing a write), I want every mutating operation to write
an audit log entry, so that changes to contract/payment-sensitive data are traceable.
- Given any `Create`/`Update`/`Delete` command handler executes successfully, when it commits,
  then an `AuditLog` row is written with `EntityName`, `EntityId`, `Action`, `UserId`,
  `BeforeJson`, `AfterJson`, `Timestamp (DateTimeOffset)`.
- Given a failed command (validation error), when it does not commit, then no audit row is
  written.
Size: M · MoSCoW: Must · Depends on: US-1.1

**US-2.3 — `ApprovalMatrixEntry` data model + seeded defaults**
As a Tenant Admin, I want a per-tenant table defining which roles may approve Variation Orders
and Payment Certificates, so that approval authority is configurable rather than hardcoded, ahead
of Phase 3 needing to enforce it.
- Given a new `Tenant` is created, when provisioning completes, then `ApprovalMatrixEntry` rows
  are seeded for both `Module` values (`VariationOrder`, `PaymentCertificate`) × all 6 roles, with
  a sensible default (`CanApprove = true` for PM and Executive, `false` for Planning/Site/QS/Admin)
  — **default values themselves must be confirmed by a human/domain-expert before Sprint 9**
  (see Open Questions §7, item D2); the seed exists so the schema and enforcement hook are testable
  now.
- Given a tenant's matrix, when queried via `GET /api/v1/tenants/{id}/approval-matrix`, then the
  response returns all 12 rows (2 modules × 6 roles) with `CanApprove` booleans; the endpoint
  requires `Role = Admin` on the caller's JWT.
- Given a request for another tenant's matrix (cross-tenant), when called, then the response is
  `403`/`404` (never leak existence of another tenant's configuration).
- No write endpoint exists yet this sprint — `POST/PUT` for the matrix ships in Sprint 9. Any
  attempt to call one this sprint should 404 (route doesn't exist), which is expected and not a bug.
Size: M · MoSCoW: Must (schema) · Depends on: US-1.1, US-2.1 · Enables: Sprint 9 US-9.4, Sprint 10 US-10.2

**US-2.4 — ProblemDetails middleware**
As any API consumer (frontend), I want all unhandled errors to return RFC 7807 ProblemDetails, so
that the frontend can render consistent error states.
- Given an unhandled exception in any controller, when it propagates, then the response is a
  ProblemDetails JSON body with a stable `type`/`title`/`status`, and no SQL or stack trace leaks
  to the client.
Size: S · MoSCoW: Must · Depends on: none

---

### Sprint 3 — XER / MSPDI / Excel parsers

**US-3.1 — Import Primavera P6 `.XER`**
As a Planning Engineer, I want to import a `.XER` file exported from Primavera P6, so that
existing schedules don't need to be rebuilt by hand in CM+.
- Given a valid `.XER` file with activities and relations, when imported, then WBS nodes,
  activities (with `PlannedStart/Finish`, `DurationDays`, `BudgetCost decimal(18,2)`), and
  relations (FS/SS/FF/SF with lag) are created matching a golden-file reference export.
- Given a `.XER` file with a relation cycle, when imported, then the import is rejected with a
  clear error identifying the offending activity chain, and no partial data is committed
  (all-or-nothing transaction).
- Given a `.XER` file larger than the configured size cap, when uploaded, then it is rejected
  before parsing (defense against oversized/malicious uploads).
Size: L · MoSCoW: Must · Depends on: US-1.1

**US-3.2 — Import MS Project via MSPDI (MPXJ), never binary `.MPP`**
As a Planning Engineer, I want to import an MSPDI XML export from MS Project, so that MPP-based
schedules can be brought into CM+ without unstable binary interop (ADR-0003).
- Given a valid MSPDI XML file, when imported, then the resulting activity network matches a
  golden-file reference (dates, durations, relations).
- Given an MSPDI file with an external entity reference (XXE attempt), when parsed, then the
  parser rejects it (XML hardening is mandatory on this path per ADR-0003 consequence).
Size: L · MoSCoW: Must · Depends on: US-1.1

**US-3.3 — Excel import/export (EPPlus) for progress templates**
As a Site Engineer, I want to import a progress-update Excel template and export a blank one, so
that field teams without CM+ access can still contribute data offline via spreadsheet.
- Given an exported template, when re-imported with valid % values filled in, then each row
  creates an `ActivityProgressLog` entry (per US-1.2) rather than only updating the cached field.
- Given a cell value starting with `=`, `+`, `-`, or `@` in an imported file, when parsed, then it
  is treated as literal text, never executed as a formula (formula-injection defense, per
  `.claude/knowledge/patterns/conventions.md`).
Size: M · MoSCoW: Must · Depends on: US-1.2

---

### Sprint 4 — WBS Tree API (< 100 ms), React shell, **Project Info screen (Decision 2)**, **Progress batch-update UI (§2.2)**

**US-4.1 — WBS Tree API performance**
As a Project Manager, I want the WBS tree to load in under 100 ms, so that navigating a large
project stays responsive.
- Given a project with 5,000 WBS nodes, when `GET /api/v1/projects/{id}/wbs-tree` is called, then
  the P95 response time is **< 100 ms** under a defined load-test profile (documented in the perf
  test suite).
- Given a project with 10,000+ activities across the tree, when the tree is requested, then the
  payload supports lazy/paginated child-loading so the initial response does not scale linearly
  with total activity count.
Size: L · MoSCoW: Must · Depends on: US-1.1

**US-4.2 — React shell matches prototype navigation**
As any user, I want the sidebar/topbar and 13-screen navigation to visually match the confirmed
prototype, so that the product looks and behaves like the agreed design.
- Given the app loads, when compared against `docs/ECM Planning Prototype.dc.html`, then the nav
  order, active-state gold highlight, and navy sidebar match pixel-equivalent per ADR-0006.
Size: M · MoSCoW: Must · Depends on: none (design tokens from `/cmplus-ui`)

**US-4.3 — Project Info screen: view and edit project master data**
As a Project Manager, I want to view and edit the project master record (name, code, owner,
contract dates, BAC), so that project setup doesn't require a database admin.
- Given the Project Info screen, when loaded, then it displays name, code, owner,
  `ContractStart`/`ContractFinish` (`DateTimeOffset`), and `BAC decimal(18,2)` as shown in the
  prototype layout.
- Given a PM edits `ContractFinish` to a date before `ContractStart`, when saving, then the save
  is rejected with a validation message (no silent acceptance of an invalid date range).
- Given a successful edit, when saved, then an `AuditLog` entry is written (per US-2.2).
Size: M · MoSCoW: Must · Depends on: US-1.1, US-2.2, US-4.2

**US-4.4 — Project Info screen: configurable Retention & Advance rate (Decision 2)**
As a QS/Cost Engineer, I want to set the project's Retention rate and Advance rate as
project-specific values (not a system-wide constant), so that each contract's actual commercial
terms are reflected in Payment Certificate calculations.
- Given the Project Info screen, when I open the edit form, then `RetentionRate` and
  `AdvanceRate` are editable numeric fields, each `decimal(5,2)`, displayed as a percentage
  (e.g., `5.00` renders as "5%").
- Given I enter a value outside `0.00`–`100.00`, when I attempt to save, then the field is
  rejected client-side and server-side (defense in depth) with a clear Thai-language message
  (e.g., "อัตราต้องอยู่ระหว่าง 0–100%").
- Given I enter a `RetentionRate` above a soft ceiling (prototype example shows 10% as a typical
  contractual cap), when saving, then the UI shows a non-blocking warning
  ("เกินเพดานปกติของสัญญา (10%) — ยืนยันหรือไม่?") but does not hard-block the save — **the exact
  ceiling policy (soft warning vs hard block, and whether it's configurable) is a contract-law
  question flagged to domain-expert**, see Open Questions §7 item D3.
- Given `RetentionRate`/`AdvanceRate` are `null` (unset), when the Payment Certificate module
  (Sprint 9) attempts to calculate Net Payment, then it blocks certificate creation with an error
  directing the user to set rates on Project Info first (no silent default substitution).
- Given a rate is changed after payment certificates already exist, when saved, then existing
  (already-issued) certificates are **not** retroactively recalculated — only certificates created
  after the change use the new rate (audit integrity, consistent with the S-Curve
  never-rewrite-history rule in evm-formulas.md). This is flagged to domain-expert to confirm
  matches contractual practice (Open Questions §7, item D3).
- Given the edit is saved, when committed, then an `AuditLog` entry records old and new rate
  values, `UserId`, and `Timestamp`.
Size: M · MoSCoW: Must · Depends on: US-1.1, US-2.2, US-4.3 · Enables: Sprint 9 US-9.1

**US-4.5 — WBS & Activity screen: batch "Update Progress" mode (§2.2 resolution)**
As a Site Engineer, I want to update progress percentage for many activities at once in a
filterable grid, so that a weekly progress-update cycle across dozens of activities is practical.
- Given the WBS & Activity screen, when I switch to "อัปเดตความคืบหน้า" (Update Progress) mode,
  then I see a grid of activities filterable by WBS branch, zone, or responsible team, each row
  showing current % and an editable "new %" input.
- Given I enter new percentages for 20 activities and submit, when saved, then 20 new
  `ActivityProgressLog` rows are created (per US-1.2), each stamped with the same
  `PeriodEndDate` (the reporting cutoff I selected) but individual `RecordedAt` timestamps.
- Given I enter a new percentage lower than the current cached value, when I attempt to submit,
  then the UI requires an explicit confirmation ("ยืนยันการปรับลดความคืบหน้า") before accepting —
  regression corrections must be deliberate, not accidental.
- Given the grid has 500+ activities, when rendered, then it uses the same virtualization pattern
  as the Gantt/DataTable components (ADR-0004) — no unvirtualized 500-row DOM render.
Size: L · MoSCoW: Must · Depends on: US-1.2, US-4.1

---

## Phase 2 — Core Planning & Control Engine (Sprint 5–8)

### Sprint 5 — CPM engine

**US-5.1 — Forward/backward pass + float calculation**
As a Planning Engineer, I want the CPM engine to compute ES/EF/LS/LF and total/free float for
every activity, so that the critical path is identified automatically instead of manually in P6.
- Given the fixture network in `.claude/knowledge/domain/cpm-method.md` (A(5)→B(3)→D(4),
  A(5)→C(6)→D(4)), when the engine runs, then it reproduces exactly: `ES_A=0,EF_A=5;
  ES_B=5,EF_B=8; ES_C=5,EF_C=11; ES_D=11,EF_D=15`, `TF_A=0, TF_B=3, TF_C=0, TF_D=0`, critical path
  **A→C→D**, `FF_B=3`.
- Given a relation cycle (A→B→A), when the engine validates the graph, then it rejects before
  attempting a forward pass, returning the offending relation chain.
- Given SS relation with lag 2 and FF relation with lag 1 fixtures, when computed, then results
  match the worked fixtures in cpm-method.md exactly (unit test per relation type, not just FS).
- Given an isolated activity with no relations, when computed, then it does not crash the pass
  and is scheduled at project start with float equal to total project slack.
Size: L · MoSCoW: Must · Depends on: US-1.1, US-3.1/US-3.2 (network data to compute over)

**US-5.2 — P6 reconciliation golden test**
As a Planning Engineer, I want CPM results to match Primavera P6 for an equivalent network, so
that migrating from P6 doesn't produce silently different dates.
- Given a `.XER` import of a real reference project, when CPM runs, then computed activity
  dates/floats match the P6-exported reference values (golden-file test, Critical per docs/2 risk #2).
Size: M · MoSCoW: Must · Depends on: US-5.1, US-3.1

---

### Sprint 6 — Gantt UI (virtualized, 10,000+ activities)

**US-6.1 — Gantt renders critical/non-critical/baseline/data-date**
As a Project Manager, I want to see critical activities in red, non-critical in slate-blue, an
active baseline in gold, and a data-date line, so that schedule status is visually unambiguous.
- Given an activity with `IsCritical = true`, when rendered, then its bar uses the critical color
  token; non-critical uses the secondary slate-blue token (per design tokens in `/cmplus-ui`).
- Given an active `Baseline` exists, when the Gantt renders, then a gold baseline marker appears
  alongside each activity's current bar.
- Given `Project.DataDate`, when the Gantt renders, then a vertical data-date line is drawn at the
  correct x-position.
Size: M · MoSCoW: Must · Depends on: US-5.1

**US-6.2 — Gantt performance at 10,000+ activities**
As a Planning Engineer, I want the Gantt to stay responsive on very large schedules, so that
enterprise-scale projects (not just demo-sized ones) are usable.
- Given a project with 10,000 activities, when the Gantt is scrolled continuously for 30 seconds,
  then frame rate does not drop below the documented threshold in the perf test suite (ADR-0004:
  virtualized rows, canvas/single-SVG bar layer, no DOM-per-bar).
- Given the same dataset, when a row's props haven't changed, then it does not re-render
  (memoization check via React DevTools profiler in the perf test).
Size: L · MoSCoW: Must · Depends on: US-6.1

---

### Sprint 7 — EVM engine, S-Curve, **EAC variant selector (Decision 1)**

**US-7.1 — EVM engine computes PV/EV/AC/SV/CV/SPI/CPI**
As a QS/Cost Engineer, I want the core EVM metrics computed at any data date, so that project
financial-schedule health is quantified, not estimated by feel.
- Given Fixture A in `.claude/knowledge/domain/evm-formulas.md` (BAC=1,000,000.00; PV=400,000.00;
  EV=300,000.00; AC=350,000.00), when computed, then SV=−100,000.00, CV=−50,000.00, SPI=0.75,
  CPI=0.857142857… (displayed rounded to `decimal(5,2)` as 0.86 where percent-like, full precision
  kept in intermediate calc).
- Given Fixture B (PV=0, EV=0, AC=0 — not started), when computed, then SPI and CPI return `null`
  (displayed as "—"), never `0` or `NaN`.
- Given Fixture C (a zero-budget activity at 50% progress), when rolled into EV, then its
  contribution is `0.00`, no division-by-zero error.
- Given the resolution in §2.2, when EVM computes PV/EV at a historical data date, then it reads
  from `ActivityProgressLog` entries with `PeriodEndDate <= t` (latest entry per activity as of
  that date) rather than only the current cached `Activity.ProgressPercentage` — **confirm this
  reconstruction rule with domain-expert**, Open Questions §7 item D1.
Size: L · MoSCoW: Must · Depends on: US-1.2, US-4.1

**US-7.2 — EAC variant is user-selectable, with a project default (Decision 1)**
As a Project Manager or Executive, I want to select which EAC formula variant I'm viewing on the
EVM screen, so that I can apply the estimation approach that matches how my project is actually
trending, instead of being locked to one formula.
- Given the EVM S-Curve screen, when it loads, then an EAC variant dropdown is visible with at
  least the three PMI variants already canonicalized in `evm-formulas.md`: "BAC ÷ CPI (ค่าเริ่มต้น)"
  [default], "AC + (BAC − EV) (Atypical)", "AC + (BAC − EV)/(CPI×SPI) (Combined)".
- Given Fixture A values, when I select "AC + (BAC − EV)", then EAC recomputes to
  350,000.00 + (1,000,000.00 − 300,000.00) = **1,050,000.00** and VAC recomputes to
  1,000,000.00 − 1,050,000.00 = **−50,000.00**, both to `decimal(18,2)`.
- Given I switch variants, when the value updates, then BAC/EAC/VAC/ETC tiles and the S-Curve
  forecast (dashed) line all recompute consistently — no stale tile showing the old variant's number.
- Given the dropdown has no explicit selection yet for a project, when the screen loads, then it
  defaults to `Project.DefaultEacVariant` (seeded `BacOverCpi` unless changed).
- Given I click "ตั้งเป็นค่าเริ่มต้นของโครงการ" (Set as project default), when confirmed, then
  `Project.DefaultEacVariant` is updated and an `AuditLog` entry is written; every other user
  opening this project's EVM screen thereafter sees the new default (though any user may still
  transiently switch the dropdown for their own viewing session without changing the project default).
- Given `CPI` or `SPI` is `null` (Fixture B, not-started project), when any variant requiring
  division by CPI/SPI is selected, then EAC displays "—", never a crash or `Infinity`.
- Who may set the project default (any project member vs restricted to PM/QS) is flagged to
  domain-expert, Open Questions §7 item D4.
Size: M · MoSCoW: Must · Depends on: US-7.1, US-4.4 (Project entity edit pattern) · Enables: Sprint 8 US-8.1

**US-7.3 — S-Curve chart (PV/EV/AC + EAC forecast)**
As an Executive, I want a cumulative S-Curve showing plan vs actual vs forecast, so that I can
see the trend at a glance without reading a table of numbers.
- Given historical `ActivityProgressLog` data, when the S-Curve renders, then it plots three solid
  cumulative series (PV, EV, AC) plus a dashed forecast-blue EAC extension from the data date
  forward, matching the visual pattern in the prototype (lines 240–268).
- Given a VO is approved (Sprint 10 dependency), when BAC changes, then the S-Curve rebaselines
  from the approval date forward; points before the approval date are never rewritten (audit
  integrity, per evm-formulas.md).
Size: M · MoSCoW: Must · Depends on: US-7.1, US-7.2

---

### Sprint 8 — Cash Flow module, Executive Dashboard, **Executive Summary export (§2.1 resolution)**

**US-8.1 — Executive Dashboard KPI tiles**
As an Executive, I want a single dashboard with KPI tiles (progress %, SPI/CPI, EAC, EOT days,
open issues), so that I can assess project health in under a minute.
- Given the dashboard loads, when EVM data is available, then EAC/ETC/VAC tiles reflect the
  project's current `DefaultEacVariant` (per US-7.2), each showing its formula subtitle (e.g.,
  "BAC / CPI (MB)") as in the prototype (lines 964–966).
- Given the dashboard loads, when compared to the prototype layout, then tile arrangement,
  S-Curve preview, critical-path preview, WBS rollup, and recent-photos strip match.
Size: M · MoSCoW: Must · Depends on: US-7.2

**US-8.2 — Cash Flow module**
As a QS/Cost Engineer, I want a periodic cash-flow bar chart plus cumulative financial summary,
so that funding needs are forecastable.
- Given PV/EV/AC series exist, when the Cash Flow screen loads, then it renders period bars and a
  cumulative summary consistent with the EVM engine's numbers (no independent/duplicate
  calculation — single source of truth is the EVM engine's output).
Size: M · MoSCoW: Must · Depends on: US-7.1

**US-8.3 — WBS progress rollup on dashboard**
As a Project Manager, I want the dashboard's overall progress % to be the weight-based rollup of
all WBS nodes, so that the headline number is mathematically consistent with the detail screens.
- Given child nodes with weights summing to 100, when rolled up, then
  $Pct_{parent} = \frac{\sum_c Pct_c \cdot W_c}{\sum_c W_c}$ matches the WBS screen's own rollup
  to two decimal places.
- Given weights at a level do not sum to 100, when saved (from Sprint 4's WBS editing), then a
  warning is shown but the save is not blocked (per evm-formulas.md rollup rule).
Size: S · MoSCoW: Must · Depends on: US-7.1

**US-8.4 — Export Executive Summary as PDF (§2.1 resolution)**
As an Executive or Project Manager, I want to export the current dashboard state as a dated PDF
with an optional commentary field, so that I can circulate a report to stakeholders who don't log
into CM+.
- Given the Executive Dashboard, when I click "ส่งออกรายงานสรุป" (Export Summary), then a modal
  offers a free-text "ความเห็นผู้จัดการโครงการ" (PM Commentary) field before generating the PDF.
- Given I generate the PDF, when it downloads, then it includes: project name/code, data date,
  KPI tiles (progress %, SPI/CPI, EAC per current `DefaultEacVariant`, EOT days, open issue
  count), the S-Curve chart image, and my commentary text if entered.
- Given I do not enter commentary, when I export, then the PDF still generates with commentary
  section simply omitted (not blocking).
- **Out of scope for this story:** persisted history of past exported summaries, scheduled/
  automatic emailing, and multi-language PDF output — these are explicit non-goals for Sprint 8;
  may become their own future story if requested.
Size: M · MoSCoW: Should · Depends on: US-8.1

---

## Phase 3 — Site Management Extensions (Sprint 9–12)

### Sprint 9 — Payment Certificate, **Tenant Admin / RBAC Settings UI (Decision 3)**

> **NEW SCOPE CALLOUT:** US-9.4 and US-9.5 below deliver the Tenant Admin / RBAC Settings area —
> a capability with **no equivalent screen in the 13-screen prototype**. It is scoped minimally
> here (functional, not polished) because Payment Certificate approval is its first real
> consumer; broader admin UX polish is deferred to Sprint 15.

**US-9.1 — Payment Certificate uses project-configured Retention/Advance (Decision 2)**
As a QS/Cost Engineer, I want Net Payment calculated using this project's own Retention and
Advance rates (set in Sprint 4), so that certificates reflect the actual contract terms instead of
a hardcoded system-wide percentage.
- Given `Project.RetentionRate = 5.00` and `Project.AdvanceRate = 10.00`, and a milestone value
  `M = 20,000,000.00` at `progress% = 100.00`, when a certificate is generated, then Retention
  withheld = `M × progress%/100 × RetentionRate/100` = **1,000,000.00**, matching the formula in
  `docs/9.` §5 and `.claude/skills/cm-domain/SKILL.md`.
- Given `Project.RetentionRate` or `AdvanceRate` is `null` (never configured), when certificate
  creation is attempted, then it is blocked with an error directing the user to Project Info
  (per US-4.4's rule — no silent default substitution).
- Given the advance-recovery installment amount, when computed, **defer the exact recovery-schedule
  mechanics to domain-expert** — flagged explicitly, Open Questions §7 item D5 (this story can
  ship with a placeholder recovery rule behind a clearly labeled TODO only if domain-expert has
  not yet answered by sprint start; do not invent the mechanics).
- Given a certificate is generated, when displayed, then `MilestoneValue`, `RetentionAmount`,
  `NetPayment` all use `decimal(18,2)`; `ClaimPct`/`ApprovePct` use `decimal(5,2)`.
- Given a project's `RetentionRate` changes after certificates already exist, when a new
  certificate is created afterward, then it uses the new rate; previously issued certificates are
  untouched (consistent with US-4.4's audit-integrity rule).
Size: L · MoSCoW: Must · Depends on: US-4.4, US-7.1

**US-9.2 — Payment Certificate screen matches prototype**
As a QS/Cost Engineer, I want the milestone table and certificate detail panel to match the
prototype's layout (claim%/approve% columns, retention/advance summary), so the screen is
consistent with the agreed design.
- Given the Payment Certificate screen loads, when compared to the prototype (lines 303–352),
  then the milestone table columns, the "⚙ ตั้งค่า Retention X% / Advance Y%" shortcut, and the
  certificate summary panel are present and functionally wired (the gear-icon shortcut opens the
  same edit surface as Project Info's Retention/Advance fields from US-4.4 — one source of truth,
  not a duplicate stored value).
Size: M · MoSCoW: Must · Depends on: US-9.1, US-4.4

**US-9.3 — Payment Certificate approval workflow uses the matrix, not a hardcoded role (Decision 3)**
As a Project Manager (or whichever role the tenant's matrix designates), I want the "approve"
action on a Payment Certificate to check the tenant's configured approval matrix, so that
approval authority reflects this organization's actual delegation of authority, not a fixed
assumption baked into the code.
- Given a certificate with `Status = Pending`, when a user with `Role = X` clicks "ส่งอนุมัติ"/
  approve, then the API checks `ApprovalMatrixEntry` for `(TenantId, Module = PaymentCertificate,
  Role = X).CanApprove`; if `true`, the certificate transitions to `Approved` and
  `ApprovedAt (DateTimeOffset)` is stamped; if `false`, the API returns `403` with a
  ProblemDetails explaining the role lacks approval authority for this tenant.
- Given the matrix has `CanApprove = false` for every role for this module (misconfiguration),
  when any user attempts to approve, then all are correctly blocked — no fallback to a hardcoded
  "PM can always approve" escape hatch (that would defeat Decision 3's purpose).
- Given an approval succeeds, when committed, then an `AuditLog` entry captures the approver's
  `UserId` and `Role` at time of approval (role is captured at approval time, not looked up
  retroactively, so later matrix changes don't rewrite history).
Size: M · MoSCoW: Must · Depends on: US-2.3, US-9.1

**US-9.4 — Tenant Admin: view and edit the approval matrix (Decision 3, new scope)**
As a Tenant Admin, I want a settings screen listing every role's approve/deny permission for VO
and Payment Certificate, so that I can configure approval authority to match my organization's
actual delegation without a code change.
- Given I am logged in with `Role = Admin`, when I navigate to the (new) "การตั้งค่าสิทธิ์อนุมัติ"
  (Approval Settings) area, then I see a 6-row × 2-column matrix (roles × VO/Payment) of toggle
  switches reflecting current `ApprovalMatrixEntry` values.
- Given I am logged in with any role other than `Admin`, when I attempt to navigate to this area
  (direct URL), then I am redirected/blocked with a `403`-equivalent UI state — this area is not
  reachable from the standard 13-screen nav for non-admins.
- Given I toggle a role's permission for a module and save, when committed, then
  `PUT /api/v1/tenants/{id}/approval-matrix` persists the change, writes an `AuditLog` entry
  (old value → new value, `UpdatedByUserId`, `UpdatedAt`), and the change takes effect
  immediately for subsequent approval attempts (no caching lag beyond normal request latency).
- Given at least one role must retain approval authority per module (safety rail against locking
  everyone out), when I attempt to save a configuration where **zero** roles can approve a given
  module, then the UI warns ("ไม่มีสิทธิ์ใดที่จะอนุมัติได้ — ยืนยันหรือไม่?") but does not hard-block
  — **whether a hard block is required is a policy call flagged to domain-expert/human**, Open
  Questions §7 item D6.
- Given this is new scope, when built, then it does not need to fit inside the 13-screen
  project-level nav — it is reachable via a separate tenant-level settings entry point (e.g. a
  gear icon distinct from the per-project sidebar), consistent with being tenant-wide rather than
  per-project configuration.
Size: M · MoSCoW: Must · Depends on: US-2.3, US-2.1 (Admin role claim)

**US-9.5 — Approval matrix is tenant-wide, not per-project (documented scope boundary)**
As a Tenant Admin, I want to understand that the approval matrix I configure applies to every
project under my tenant, so that I don't mistakenly expect per-project overrides.
- Given a tenant has multiple projects, when the matrix is edited, then the change applies
  identically to VO/Payment approvals across all of that tenant's projects (single matrix per
  tenant, no per-project row).
- **Out of scope for MVP:** per-project override of the matrix. Flagged as a possible future
  enhancement only if real usage shows a need (see Open Questions §7 item D7) — not built now, to
  prevent scope creep.
Size: S (documentation/guardrail, minimal code — mainly a negative test that no per-project
override exists) · MoSCoW: Won't (for now, explicit non-goal) · Depends on: US-9.4

---

### Sprint 10 — Variation Order (approval workflow, BAC rebaseline)

**US-10.1 — VO submission and BAC impact**
As a Site Engineer, I want to submit a Variation Order (Add/Deduct) with an amount, so that scope
changes are tracked formally instead of via informal site instructions.
- Given a new VO with `Type = Add`, `Amount = 3,200,000.00 decimal(18,2)`, when submitted, then
  `Status = Pending` and `Project.BAC` is **not** yet changed (only an approved VO affects BAC).
- Given a VO with `Type = Deduct`, when approved (per US-10.2), then `Project.BAC` decreases by
  `Amount`; given `Type = Add`, `Project.BAC` increases.
Size: M · MoSCoW: Must · Depends on: US-1.1

**US-10.2 — VO approval/rejection uses the same tenant approval matrix (Decision 3)**
As a Project Manager (or whichever role the matrix designates for `Module = VariationOrder`), I
want the VO approve/reject action to check the same tenant approval matrix used for Payment
Certificates, so that approval authority is configured once, consistently, per tenant.
- Given a VO with `Status = Pending`, when a user with `Role = X` clicks "อนุมัติ", then the API
  checks `ApprovalMatrixEntry` for `(TenantId, Module = VariationOrder, Role = X).CanApprove`,
  reusing the exact mechanism built in US-9.3/US-9.4 (no VO-specific reimplementation).
- Given approval succeeds, when committed, then `VariationOrder.Status = Approved`,
  `ApprovedAt (DateTimeOffset)` stamped, `AuditLog` entry written, and `Project.BAC` updated per
  US-10.1.
- Given a VO is rejected ("ตีกลับ"), when committed, then `Status = Rejected`, no BAC change, and
  the submitter can see the rejection (UI state per prototype lines 391–407).
Size: M · MoSCoW: Must · Depends on: US-9.4, US-10.1

**US-10.3 — S-Curve/EVM rebaseline on VO approval**
As a QS/Cost Engineer, I want the S-Curve and EVM tiles to reflect the new BAC immediately after a
VO is approved, so that forecasts stay accurate without a manual recalculation step.
- Given a VO approval changes `Project.BAC`, when the EVM/S-Curve screens are next loaded, then
  BAC/EAC/VAC recompute using the new BAC; historical S-Curve points before the approval date are
  unchanged (never rewritten), per evm-formulas.md.
Size: M · MoSCoW: Must · Depends on: US-10.2, US-7.3

---

### Sprint 11 — Weather Log (EOT), Issue/Action Log

**US-11.1 — Weather Log entries are immutable legal evidence**
As a Site Engineer, I want daily weather log entries (rainfall, work-stoppage flag) to be
permanent once submitted, so that EOT claims are backed by tamper-evident records.
- Given a weather log is submitted, when any user attempts to edit or delete it, then the
  operation is rejected — corrections are new, separately audited entries, never in-place edits
  (per `.claude/knowledge/patterns/conventions.md`).
- Given a stoppage day flagged on a **critical-path activity** (per Sprint 5/6's `IsCritical`),
  when EOT-eligibility is evaluated, then it is marked EOT-eligible; a stoppage on a non-critical
  activity only consumes float, not EOT (per `.claude/skills/cm-domain/SKILL.md`).
Size: M · MoSCoW: Must · Depends on: US-5.1 (critical flag), US-1.1

**US-11.2 — Issue/Action Log status flow**
As a Site Engineer, I want to log an issue and advance its status (Open → Doing → Closed), so
that site problems are tracked to resolution.
- Given a new issue, when created, then `Status = Open` and appears in the "เปิดอยู่" tile count.
- Given an issue's status is advanced, when saved, then `ClosedAt (DateTimeOffset)` is stamped
  only on transition to `Closed`, and the tile counts (open/doing/closed) update accordingly.
Size: S · MoSCoW: Must · Depends on: US-1.1

---

### Sprint 12 — Photo Progress (offline-first), Man/Equipment

**US-12.1 — Photo capture works offline and syncs later**
As a Site Engineer, I want to take and tag progress photos even without connectivity, so that
poor site signal doesn't block documentation.
- Given no network connection, when a photo is captured and tagged to an Activity/Zone, then it
  is queued in the IndexedDB outbox (ADR-0005), visibly marked "รอซิงค์" (pending sync) in the UI.
- Given connectivity returns, when Background Sync fires, then queued photos upload and the UI
  status updates to "ซิงค์แล้ว" (synced) without user intervention.
Size: L · MoSCoW: Must · Depends on: none new (ADR-0005 pattern)

**US-12.2 — Man/Equipment daily log and productivity index**
As a Site Engineer, I want to log daily manpower/equipment counts by work category, so that
productivity trends are visible without a separate spreadsheet.
- Given `PlannedManCount` and actual `ManCount` for a category/day, when the histogram renders,
  then a Productivity Index (actual/planned) is computed and displayed per the formula agreed by
  domain-expert (flagged if not yet defined — do not invent, Open Questions §7 item D8).
Size: M · MoSCoW: Must · Depends on: US-1.1

---

## Phase 4 — Optimization, PWA, Security & Launch (Sprint 13–16)

### Sprint 13 — Service Worker + IndexedDB outbox + Background Sync (all site modules)

**US-13.1 — Offline outbox covers Photo, Weather, and Progress-update writes**
As a Site Engineer, I want Weather Log entries and batch Progress updates (US-4.5), not just
photos, to queue offline and sync later, so that the whole field workflow is resilient to bad
connectivity, not just photos.
- Given no network, when I submit a weather log or a batch progress update, then it queues in the
  same IndexedDB outbox pattern as US-12.1, with a visible per-item sync status.
- Given a connectivity drop mid-sync, when the network returns, then Background Sync resumes
  without duplicating already-synced items (idempotency key per outbox entry).
Size: L · MoSCoW: Must · Depends on: US-4.5, US-11.1, US-12.1

---

### Sprint 14 — Baseline module + comparison engine

**US-14.1 — Save and activate a Baseline**
As a Planning Engineer, I want to save the current schedule as a named Baseline and mark one
Baseline active, so that I can compare current progress against a locked target.
- Given the current schedule state, when I save a Baseline, then a full snapshot of all
  Activities (dates, durations, BAC) is stored under that `Baseline.Id`, and only one Baseline per
  project can have `IsActive = true` at a time.
Size: M · MoSCoW: Must · Depends on: US-1.1, US-5.1

**US-14.2 — Baseline delta comparison**
As a Project Manager, I want to see the delta between the active Baseline and current schedule
per activity, so that slippage is quantified, not just visually implied.
- Given an active Baseline and current activity dates, when the comparison view renders, then each
  activity shows delta days (current − baseline), matching the example in the prototype's
  Baseline screen.
Size: M · MoSCoW: Must · Depends on: US-14.1

---

### Sprint 15 — Security audit (OWASP, tenant isolation), perf tuning, **Tenant Admin polish (Decision 3 hardening)**

**US-15.1 — Full tenant-isolation audit**
As a security-auditor, I want every query across all 15+ modules re-verified against ADR-0002, so
that no unfiltered query slipped through during Phase 1–3 delivery.
- Given every CQRS query handler, when statically and dynamically reviewed, then each is confirmed
  to apply the tenant filter (or documents an explicit, audited bypass reason for admin tooling).
- Given the OWASP Top 10 checklist, when applied to auth, upload surfaces (XER/MPP/XLSX/photo),
  and export paths, then no Critical/High findings remain open.
Size: L · MoSCoW: Must · Depends on: all prior sprints

**US-15.2 — Approval matrix hardening (deferred polish from Decision 3)**
As a Tenant Admin, I want to see a change history for the approval matrix and be warned more
robustly about risky configurations, so that the Sprint 9 MVP admin UI matures into something
safe for production use.
- Given the matrix has been edited multiple times, when I view "ประวัติการเปลี่ยนแปลงสิทธิ์อนุมัติ"
  (Approval Change History), then I see a chronological list sourced from `AuditLog` entries for
  `ApprovalMatrixEntry` (no new storage — reuse existing audit trail).
- Given the zero-approver risk noted in US-9.4, when domain-expert/human has ruled on item D6 by
  this sprint, then the resolved policy (hard block vs warn) is implemented as specified.
Size: S · MoSCoW: Should · Depends on: US-9.4

---

### Sprint 16 — Docker + CI/CD + staging deploy + UAT

**US-16.1 — Staging deployment with human sign-off gate**
As the Orchestrator/human stakeholder, I want a staging environment with a required manual
approval gate before production promotion, so that nothing ships without explicit human review.
- Given a build passes CI (build+test+lint+security scan), when promoted to staging, then
  production promotion requires a separate manual approval step (no auto-promotion).
- Given UAT is run on staging with real site users, when issues are found, then they are logged
  (Issue/Action Log, dogfooding the product) and triaged before the sign-off gate is passed.
Size: L · MoSCoW: Must · Depends on: US-15.1
Note: cloud provider selection (AWS vs Azure, Decision 4) is explicitly out of scope for this
backlog — devops-engineer plans that separately; this story is written provider-agnostically.

---

## 4. Cross-Sprint Dependency Map (the four decisions' threads)

```
Decision 1 (EAC selectable):
  Sprint 7 US-7.2 (engine + selector, needs US-7.1 + US-4.4 pattern)
      └──► Sprint 8 US-8.1 (dashboard tiles consume DefaultEacVariant)
      └──► Sprint 8 US-8.4 (PDF export shows selected variant)
      └──► Sprint 10 US-10.3 (VO-triggered rebaseline respects variant)

Decision 2 (Retention/Advance per-project):
  Sprint 1 US-1.1 (fields modeled, nullable)
      └──► Sprint 4 US-4.4 (editable UI + validation, Project Info)
              └──► Sprint 9 US-9.1 (Payment engine consumes rates)
              └──► Sprint 9 US-9.2 (Payment screen shortcut reuses same edit surface)

Decision 3 (Approval matrix, new scope):
  Sprint 2 US-2.3 (data model + seed + read-only endpoint)
      └──► Sprint 9 US-9.3 (Payment approve enforces matrix)
      └──► Sprint 9 US-9.4/US-9.5 (Tenant Admin UI, first write endpoint)
              └──► Sprint 10 US-10.2 (VO approve reuses same matrix + UI)
              └──► Sprint 15 US-15.2 (change-history polish, zero-approver policy resolved)

Decision 4 (Cloud provider): no thread — deliberately absent from this backlog.

§2.2 resolution (ActivityProgressLog):
  Sprint 1 US-1.2 (entity)
      └──► Sprint 3 US-3.3 (Excel import writes log entries, not raw overwrite)
      └──► Sprint 4 US-4.5 (batch Update Progress UI)
              └──► Sprint 7 US-7.1 (EVM historical PV/EV reads from log)
              └──► Sprint 13 US-13.1 (offline queueing extends to progress updates)

§2.1 resolution (Executive Summary export):
  Sprint 8 US-8.1 (dashboard) ──► US-8.4 (PDF export, self-contained, no downstream dependents)
```

---

## 5. Open Questions

Flagged explicitly to **domain-expert** (EVM/CPM/payment/contract logic — do not resolve without
that agent, per CLAUDE.md and po-analyst standards):

- **D1 (EVM):** Should historical PV/EV at a past data date be reconstructed strictly from
  `ActivityProgressLog` snapshots (recommended in §2.2), or is a different historical-reconstruction
  rule intended (e.g., interpolation between log dates, or a different treatment when a data date
  predates the log's introduction during migration from legacy data)?
- **D2 (RBAC/contract policy, borderline domain):** Confirm the actual default approval matrix
  seed values per role (Sprint 2, US-2.3) — the placeholder (PM+Executive can approve both
  modules, others cannot) is an assumption, not a confirmed business rule.
- **D3 (payment/contract):** Confirm the Retention-rate soft-ceiling policy (is 10% a universal
  soft warning, a hard system cap, or itself tenant-configurable?), and confirm that changing
  Retention/Advance rate must never retroactively recalculate already-issued certificates
  (assumed in US-4.4/US-9.1 by analogy with the S-Curve audit-integrity rule — needs explicit
  confirmation for payment/contract law correctness).
- **D4 (EVM/RBAC borderline):** Who may set a project's default EAC variant (US-7.2) — any
  project member, or restricted to PM/QS/Executive? This affects whether it needs its own
  approval-matrix-style entry or is a simple open action.
- **D5 (payment, urgent — blocks Sprint 9):** The advance-recovery installment mechanics are not
  yet fully specified anywhere in the docs (`docs/9.` §5 and `cm-domain` SKILL.md both state the
  formula shape — `Net Payment = ... − advance recovery installment` — but not how the
  installment amount itself is derived per period: flat %, proportional to claim, capped at total
  advance, starts immediately or after a grace period). This must be resolved before Sprint 9
  implementation, not worked around.
- **D6 (RBAC policy):** Should the Tenant Admin UI hard-block saving a configuration where zero
  roles can approve a module (US-9.4), or is a warn-and-allow acceptable? This is as much a
  security/product policy question as a domain one — flag to domain-expert and human jointly.
- **D7 (future scope, non-blocking):** Is a per-project override of the approval matrix a real
  near-term need, or safely deferred indefinitely (US-9.5)? No sprint currently depends on the
  answer; only affects backlog beyond Sprint 16.
- **D8 (formula, non-blocking for Sprint 12 start but needed before completion):** Confirm the
  exact Productivity Index formula for Man/Equipment (US-12.2) — not yet defined in any doc/knowledge file.

Non-domain open item for human/product decision:
- Confirm whether the Tenant Admin / RBAC Settings area (Decision 3) should be exposed via a
  visible icon in the standard app chrome (discoverable) or a hidden/direct-URL-only route for
  Admins in the MVP — affects a small UI-placement decision in US-9.4, not a domain question.
