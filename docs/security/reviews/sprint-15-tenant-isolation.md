# Sprint 15 Security Review — Full-System Tenant-Isolation Audit (S15-SEC-01)

**Reviewer:** `security-auditor` · **Date:** 2026-08-11
**Scope:** The deferred-since-Sprint-3 full-system tenant-isolation sweep, now carrying seven
never-security-reviewed sprints (4, 5, 6, 7, 8, 11) plus everything since.
**DoD (verbatim):** *"ตรวจ CQRS handler ทุกตัวทั้ง static + dynamic; ทุกตัวมี tenant filter หรือมีเหตุผล
bypass ที่บันทึกและ audit ไว้; รายการ handler ที่ตรวจแล้วครบถ้วนเทียบกับรายชื่อ handler ทั้งหมด (ไม่ใช่การสุ่ม)."*
Read against **ADR-0002** (tenant isolation via `TenantId` + EF global query filters), **ADR-0018**
(per-project authorization — the standing M-02/M-04 gap), **ADR-0019** (append-only `CpmRun`
history + its unimplemented retention policy), **ADR-0021** (the NULL-`ProjectId` single-active
filtered-index defect, recorded today), and the prior reviews `sprint-09.md` §10, `sprint-10.md`
§9/§10, `sprint-12.md` §6/§10.

---

## Verdict

> **PASS — tenant isolation is intact. 0 Critical, 0 High.**
> No CQRS handler, reader, repository, background sweep or export path leaks data across the
> `TenantId` boundary. Every handler is either tenant-scoped by the ADR-0002 ambient global query
> filter or is the single, documented, pre-tenant login path. The reflection-driven
> `TenantIsolationTests` genuinely covers **every** `ITenantOwned` type (39 of them), proven by
> execution (81/81). The standing **M-02/M-04** (no project-scoped authorization) and **ADR-0021**
> (NULL-`ProjectId` index) items remain **open** — both are *within-tenant* concerns, neither is a
> cross-tenant breach. One new **Low** defense-in-depth finding (L-01) is raised.

This audit is a **completeness** exercise, not a spot-check: §1 enumerates every `IRequestHandler<>`
in `backend/src` and reconciles the checked list against the full list so a reviewer can see
`count(checked) == count(exists)` and that nothing was skipped.

---

## Method, and its limits — read before trusting anything below

Docker Desktop cannot start on this machine (requires Administrator; `docs/perf/gantt-frontend-s6.md`
§3), so **there is no SQL Server and no running API**. Nothing here is a live penetration test.

- **Code-verified** — read the production source directly.
- **Execution-verified** — ran against the **real** `CMPlus.Domain`/`CMPlus.Application`/
  `CMPlus.Infrastructure` assemblies on EF Core InMemory. Two things were executed this pass:
  1. the repo's own reflection-driven `TenantIsolationTests` (81/81);
  2. a throwaway cross-tenant probe in the session scratchpad, **outside** the repository,
     referencing the last-good compiled `CMPlus.Infrastructure.dll` and driving the real
     `EvmDataReader`/`WbsTreeReader`/`GanttActivityReader`/`CpmRunHistoryReader`/
     `VariationOrderRepository` against a two-tenant InMemory store. `git status --porcelain` was
     identical before and after (169 pre-existing working-tree entries; no repository file created,
     modified or deleted by this audit).
- **InMemory caveats (the two lessons this project has already been burned by):** InMemory ignores
  unique indexes entirely and does not roll back a failed `SaveChanges`. Treat every InMemory result
  as evidence about the **C# query logic**, never about the storage engine's guarantees. See §6.

**Working-tree state, stated for honesty.** The tree is mid-implementation of Sprint 15
approval-policy hardening (an agent is writing it concurrently with this review). At one point during
the audit `CMPlus.Application` did **not compile** (`SimulateApprovalRoutingQuery.cs` referenced a
then-absent `ApprovalRoutingSimulationDto`, since added). The dynamic runs therefore executed against
the **last-good compiled assemblies**, not necessarily the instantaneous source. The two WIP handlers
(§1, rows 58–59) are **static-verified only**.

---

## 1. The enumeration (DoD item: complete, reconciled, not a sample)

Every `IRequestHandler<>` in `backend/src`, discovered by `grep -rl "IRequestHandler<"`:
**59 handlers.** Each row lists its data collaborator(s) and its tenant-scoping basis.

Scoping legend:
- **A** = tenant-scoped by the ADR-0002 ambient global query filter (the collaborator issues only
  LINQ over `ITenantOwned` DbSets; no `IgnoreQueryFilters`, no raw SQL).
- **A+P** = A, plus an *additional* explicit `projectId`/tenant cross-check in the handler/reader
  (defense in depth against a route-trusting bug; closes same-tenant-wrong-project confusion).
- **X** = deliberately cross-tenant with a recorded reason.

| # | Handler | Collaborator(s) | Scoping |
|---:|---|---|:--:|
| 1 | `LoginCommandHandler` | `UserReader.FindByEmailAsync` (`IgnoreQueryFilters`, projection-only) | **X — pre-tenant login; ADR-0002's anticipated exemption; single caller** |
| 2 | `GetProjectsQueryHandler` | `ProjectReader.GetAllAsync` (tenant's projects only — no cross-tenant aggregation) | A |
| 3 | `UpdateProjectCommandHandler` | `ProjectRepository.FindAsync`/`TrySaveChangesAsync` | A |
| 4 | `SetEacVariantDefaultCommandHandler` | `ProjectRepository` | A |
| 5 | `SetEacAdvancedInputsCommandHandler` | `ProjectRepository` | A |
| 6 | `GetWbsTreeQueryHandler` | `WbsTreeReader` (S6 bulk read) | A |
| 7 | `GetNodeActivitiesQueryHandler` | `WbsNodeActivitiesReader` (`NodeExistsInProject` check) | A+P |
| 8 | `GetGanttQueryHandler` | `GanttActivityReader` (S6 bulk read) | A |
| 9 | `RecalculateCpmCommandHandler` | `CpmScheduleRepository` (S5; LINQ reads + raw-SQL write re-asserting `TenantId`) | A |
| 10 | `GetEvmQueryHandler` | `EvmComputationService` → `EvmDataReader`/`ActualCostReader` (S7/S8) | A |
| 11 | `CloseEvmPeriodCommandHandler` | `EvmComputationService` + `EvmSnapshotRepository` | A |
| 12 | `ListEvmSnapshotsQueryHandler` | `EvmSnapshotRepository` | A |
| 13 | `GetCashFlowQueryHandler` | `EvmComputationService` + `EvmSnapshotRepository` (S8 cross-screen aggregate; no direct DbContext) | A |
| 14 | `GetDashboardQueryHandler` | `EvmComputationService` + `WbsTreeReader` + `WbsProgressReader` (S8 aggregate) | A |
| 15 | `BatchRecordProgressCommandHandler` | `BatchProgressRepository` | A |
| 16 | `GetProgressAsOfQueryHandler` | `ActivityProgressReader` | A |
| 17 | `ImportScheduleFileCommandHandler` | `ImportRepository` (bulk; `TenantId` stamped server-side on `SaveChanges`) | A |
| 18 | `ImportExcelProgressCommandHandler` | `ImportRepository` | A |
| 19 | `GetImportJobQueryHandler` | `ImportRepository.FindJob` + `job.ProjectId == request.ProjectId` | A+P |
| 20 | `GetImportJobHistoryQueryHandler` | `ImportRepository` (`ProjectId`-filtered) | A |
| 21 | `ExportProgressTemplateQueryHandler` | `ImportRepository.ProjectExists`/`GetActivityTemplateRows` (export is project + tenant scoped — no cross-tenant aggregation) | A+P |
| 22 | `RecordActualCostCommandHandler` | `ActualCostRepository` | A |
| 23 | `RecordWeatherLogCommandHandler` | `DailyWeatherLogRepository` (S11) | A |
| 24 | `RecordWeatherLogCorrectionCommandHandler` | `DailyWeatherLogRepository.GetByIdAsync(projectId, logId)` | A+P |
| 25 | `ListWeatherLogsQueryHandler` | `DailyWeatherLogRepository.ListByProjectAsync` | A |
| 26 | `EvaluateEotCommandHandler` | `EotEvaluationRepository` + `DailyWeatherLogRepository` + `CpmRunHistoryReader` (S11) | A |
| 27 | `CreateIssueCommandHandler` | `IssueLogRepository` | A |
| 28 | `AdvanceIssueStatusCommandHandler` | `IssueLogRepository.FindAsync(issueId)` (id-only + ambient; see M-04) | A |
| 29 | `ListIssuesQueryHandler` | `IssueLogRepository.ListByProjectAsync` | A |
| 30 | `RecordManpowerLogCommandHandler` | `ManpowerEquipmentLogRepository` | A |
| 31 | `RecordManpowerLogCorrectionCommandHandler` | `…GetByIdAsync(projectId, logId)` | A+P |
| 32 | `GetProductivityIndexQueryHandler` | `ProductivityIndexReader` (S12; subtree/step-function aggregates) | A |
| 33 | `UploadPhotoCommandHandler` | `PhotoRepository` (`ProjectExists`/`ActivityExists` project-scoped) | A+P |
| 34 | `GetPhotoContentQueryHandler` | `PhotoRepository.FindAsync` + `photo.ProjectId == request.ProjectId` | A+P |
| 35 | `CaptureBaselineCommandHandler` | `BaselineRepository` (S14) | A |
| 36 | `ActivateBaselineCommandHandler` | `BaselineRepository.TryActivateAsync` | A |
| 37 | `CompareBaselineQueryHandler` | `BaselineComparisonReader` (`FindBaseline(projectId, baselineId)`) | A+P |
| 38 | `SubmitPaymentCertificateCommandHandler` | `PaymentCertificateRepository.FindAsync(id)` (flat route; see M-02) | A |
| 39 | `ApprovePaymentCertificateCommandHandler` | `…FindAsync(id)` + ledger writes | A |
| 40 | `RejectPaymentCertificateCommandHandler` | `…FindAsync(id)` | A |
| 41 | `ReturnPaymentCertificateForRevisionCommandHandler` | `…FindAsync(id)` | A |
| 42 | `RecordPaymentForPaymentCertificateCommandHandler` | `…FindAsync(id)` | A |
| 43 | `GetPaymentCertificateQueryHandler` | `…GetByIdAsync(id)` (flat route; see M-02) | A |
| 44 | `ListPaymentCertificatesQueryHandler` | `…ListByProjectAsync` (`projects/{projectId}` route) | A |
| 45 | `CreateVariationOrderCommandHandler` | `VariationOrderRepository` (`projects/{projectId}` route) | A |
| 46 | `SubmitVariationOrderCommandHandler` | `…FindAsync(id)` (flat route; see M-04) | A |
| 47 | `ApproveVariationOrderCommandHandler` | `…FindAsync(id)` + `Project`/`Activity` on same scoped context | A |
| 48 | `RejectVariationOrderCommandHandler` | `…FindAsync(id)` | A |
| 49 | `ReturnVariationOrderForRevisionCommandHandler` | `…FindAsync(id)` | A |
| 50 | `WithdrawVariationOrderCommandHandler` | `…FindAsync(id)` | A |
| 51 | `CancelVariationOrderCommandHandler` | `…FindAsync(id)` | A |
| 52 | `UpdateVariationOrderContentCommandHandler` | `…FindAsync(id)` | A |
| 53 | `GetVariationOrderQueryHandler` | `…GetByIdAsync(id)` (flat route; see M-04) | A |
| 54 | `ListVariationOrdersQueryHandler` | `…ListByProjectAsync` (`projects/{projectId}` route) | A |
| 55 | `GetApprovalPolicyQueryHandler` | `ApprovalPolicyReader` (`tenants/{tenantId}` route, JWT cross-check) | A |
| 56 | `UpdateApprovalPolicyCommandHandler` | `ApprovalPolicyRepository` (tenant from JWT, never route) | A |
| 57 | `GetApprovalActionHistoryQueryHandler` | validates document existence via tenant-filtered repo (Payment+VO shared) | A+P |
| 58 | `GetApprovalPolicyVersionHistoryQueryHandler` **[WIP, uncommitted]** | `ApprovalPolicyHistoryReader` (`ApprovalPolicies` + `AuditLogs`, both ambient) | A |
| 59 | `SimulateApprovalRoutingQueryHandler` **[WIP, uncommitted]** | `ProjectRepository.FindAsync` + candidate policies + VO total | A+P |

**Reconciliation:** 59 exist, 59 checked. **58 tenant-scoped via the ambient filter** (nine of them
with an additional explicit `projectId`/tenant cross-check). **1 deliberately cross-tenant with a
recorded reason** (row 1, login). **0 handlers touch data while being neither scoped nor justified.**

There is exactly **one** MediatR pipeline behavior (`ValidationBehavior`) — FluentValidation, no data
access. `GlobalExceptionHandler` is not an `IRequestHandler`. No `INotificationHandler`/
`IStreamRequestHandler` exists.

### 1.1 The three filter-bypass / raw-SQL sites, individually justified

A repo-wide grep for `IgnoreQueryFilters`/`FromSql*`/`ExecuteSql*`/`ExecuteUpdate`/`ExecuteDelete`
across `backend/src` returns **exactly** these production sites:

1. `UserReader.FindByEmailAsync` — `IgnoreQueryFilters`. **Justified:** login happens before any
   tenant is known; returns a projection (`UserAuthRecord`), never the full `User`; the only caller
   is `LoginCommandHandler`; the returned `TenantId` becomes the JWT claim that scopes every
   subsequent request.
2. `EfIdempotencyStore.PurgeExpiredAsync` — `IgnoreQueryFilters`. **Justified:** a background
   retention sweep has no ambient per-request tenant (`HttpContextTenantProvider` throws outside an
   authenticated request), and its purpose is to reclaim expired *dedup* rows across every tenant.
   Grep-able, explicit, logs the deleted counts. Deletes transient idempotency records only — no
   business/legal data — so a structured-log "audit" is proportionate; it writes no `AuditLog` row.
3. `CpmScheduleRepository.SaveResultsAsync` — the **only** raw SQL. `ExecuteSqlInterpolatedAsync`
   (parameterized — no injection surface) with an explicit `WHERE a.TenantId = {tenantProvider.TenantId}`
   because raw SQL bypasses the LINQ global filter. Correct defense-in-depth. **Cannot be
   execution-verified here** (InMemory cannot run `ExecuteSqlInterpolatedAsync`) — code-verified.
   Note `EfIdempotencyStore.ReserveAsync` filters `TenantId == tenantId` explicitly where `tenantId`
   is a method parameter sourced from the JWT-backed provider — correct, not a bypass.

### 1.2 The CpmRun-retention sweep does **not** exist

The task anticipated an "idempotency **and** CpmRun-retention background sweep." Only the idempotency
sweep exists. A grep for CpmRun deletion/pruning across `backend/src` returns **zero** production
hits (the only `CpmRuns.Remove` calls are in `AppendOnlyGuardInterceptorTests`, proving the
interceptor *blocks* deletion). ADR-0019 records CpmRun retention as a *future* need with a hard
constraint — pruning must key on **citation, not age**, or it silently invalidates an `EotEvaluation`
that cites the run. When it is built, it will be a second cross-tenant sweep and must carry the same
`IgnoreQueryFilters` discipline as the idempotency sweep, plus the citation-aware rule.

---

## 2. Dynamic verification (executed this pass)

### 2.1 `TenantIsolationTests` — 81/81, covers every `ITenantOwned` type

`dotnet test …TenantIsolationTests` → **Passed! Failed: 0, Passed: 81, Skipped: 0**.

That is 3 fixed `Project` tests + 2 `[Theory]` × **39 `ITenantOwned` CLR types**. The theory source
`TenantOwnedEntityTypes()` reflects over `CmPlusDbContext`'s built model — the *same* reflection
`ApplyTenantQueryFilters` uses — so a newly-added tenant-owned entity becomes a new case
automatically, and `CreateFixture` **throws loudly** for any type without a registered fixture. This
is the exact mechanism the DoD calls for ("a type added without a fixture is the gap it is designed to
catch"). All 39 types have fixtures, including every never-reviewed addition: `CpmRun`,
`CpmRunActivity`, `CpmRunRelation`, `EotEvaluation` + `Run`/`Source`/`Driver`, `DailyWeatherLog` +
`Activity`, `IssueLog`, `ManpowerEquipmentLog`, `ManpowerPlan`, `WorkCategory`, `Photo`,
`IdempotencyKey`, `Baseline` + `BaselineActivitySnapshot`.

Each theory proves both DoD halves per type: the global filter returns **zero** of tenant B's rows
(with an `Assert.NotEmpty` sanity guard so a filter that hides *everything* cannot pass vacuously),
and a payload-supplied `TenantId` is **overwritten** server-side on insert. `Tenant` itself is
correctly **not** `ITenantOwned` (the multi-tenancy root; exempt by design).

### 2.2 Cross-tenant join-reader probe — the never-reviewed bulk/aggregate readers

The DoD asks to probe by execution the readers with explicit `Where`/join logic and cross-aggregate
reads, "since those are where a join can cross the boundary." A throwaway probe seeded a full
tenant-A graph (Project, WBSNode, Activity + progress log, CpmRun) and then, as tenant B, called the
real Sprint 5/6/7/8/11 readers with tenant A's `projectId`:

```text
[TenantB attacker  ] settings=null bac=0 activityInputs=0 projectExists=False wbsNodes=0(actCount=-1) ganttRows=0 governingRun=null earliestRun=null approvedVoSum=0
[TenantA owner ctrl] settings=SET  bac=100000 activityInputs=1 projectExists=True  wbsNodes=1(actCount=1) ganttRows=1 governingRun=SET  earliestRun=SET  approvedVoSum=0
```

Every cross-tenant read returns empty/zero/null; the positive control shows the data is present (so
the filter is **hiding**, not the data being absent). This execution-proves the load-bearing pattern
`dbContext.WBSNodes.Any(w => w.Id == a.WbsNodeId && w.ProjectId == projectId)` — used by
`WbsTreeReader`, `GanttActivityReader`, `EvmDataReader` (incl. `GetBacAsOfAsync`, which also sums the
tenant-filtered `VariationOrders`), `WbsProgressReader`, `ProductivityIndexReader`,
`DailyWeatherLogRepository`, `ManpowerEquipmentLogRepository` and `VariationOrderRepository` — applies
the tenant filter **inside the subquery**: a valid-but-foreign `projectId` matches no rows in tenant
B's filtered `WBSNodes`, so nothing joins. `CpmRunHistoryReader`'s two-collection `.Include` +
`.AsSplitQuery()` shape is likewise tenant-safe. (`approvedVoSum=0` for both tenants because no
*Approved* VO was seeded; VO tenant isolation is covered by the `TenantIsolationTests` VO fixture and
by `sprint-10.md` §6's execution probe.)

---

## 3. Standing items — current status (folded in, not re-discovered)

### 3.1 M-02 / M-04 — no project-scoped authorization (ADR-0018) — OPEN, Medium

Carried from `sprint-09.md` M-02 and `sprint-10.md` M-04, accepted as a documented limitation pending
a product decision + a `ProjectMember`/`UserProject` entity that does not yet exist anywhere in
`backend/src`. **This is not a tenant-isolation breach** — nothing crosses a tenant boundary, which is
why it stays Medium.

What has changed since Sprint 10 is the **blast radius**. Every project- and document-scoped handler
authorizes by *tenant + role* only, never by project assignment, so any authenticated tenant user
holding the right role can act on **any** project in that tenant:

- **Sharpest instances — flat id-only routes, no `projectId` even present to cross-check:**
  `/api/v1/payment-certificates/{id}/…` (handlers #38–43) and `/api/v1/variation-orders/{id}/…`
  (handlers #46–53). A QS can certify Project B's interim payment certificate or approve Project B's
  VO — both **move money** (`Project.BAC`/`ContractValue`).
- **Broader surface — `{projectId}` routes that still don't check membership:** Baseline (#35–37),
  Weather (#23–25), Photo (#33–34), Manpower (#30–32), Issues (#27–29), Progress (#15–16),
  ActualCosts (#22), CPM (#9), EOT (#26), EVM (#10–12), CashFlow (#13), Dashboard (#14), Gantt (#8),
  WBS (#6–7), Projects (#3–5). The `projectId` in the route only prevents *cross-project confusion of
  a specific document* (e.g. #19/#34's `job.ProjectId == request.ProjectId`); it does **not** restrict
  which projects a user may touch.

Net: **~55 handlers** are within M-02/M-04's reach — up from the ~15 (Payment+VO) at Sprint 10.
`GetImportJobQueryHandler` (#19) remains the lone handler that closes the same-tenant-wrong-project
case, and only because its route carries a `projectId` to compare. This must keep appearing in every
review until ADR-0018 lands.

### 3.2 ADR-0021 — NULL-`ProjectId` single-active filtered index — OPEN, Medium

Recorded today. The filtered unique index `(TenantId, ProjectId, DocumentType) WHERE IsActive = 1`
never fires for the `ProjectId IS NULL` group (tenant-wide default) under ANSI NULL semantics, so two
concurrent `UpdateApprovalPolicy` requests can leave **two simultaneously-active policy versions**,
after which a fresh Payment Certificate/VO resolves "the active policy" nondeterministically. This is
**within-tenant** (in-flight documents pin a specific `ApprovalPolicyId` and are unaffected) — not a
cross-tenant breach. Fix is an index redesign + migration (two filtered indexes, or a computed
non-null discriminator) pending human/`database-engineer`/`system-architect` sign-off; it cannot be
validated in this environment (InMemory ignores unique indexes).

Note the mitigation now in flight: the concurrently-built `SimulateApprovalRoutingQueryHandler` (#59)
deliberately **surfaces** this corruption via `MultipleActivePoliciesDetected`/
`AmbiguousActivePolicies` rather than hiding it — a good detection aid, but not the fix.
`ApprovalPolicyReader.GetActiveTenantDefaultPolicyAsync` masks it for its own callers by
`OrderByDescending(Version).FirstOrDefault()`, but `GetCandidatePoliciesAsync` (the routing path)
returns all active rows, which is where the nondeterminism bites.

---

## 4. Findings

### 4.1 Critical — none.
### 4.2 High — none.

Unambiguously: **there are zero Critical and zero High tenant-isolation findings.** Tenant scoping is
architecturally sound; the ambient filter holds under execution; the escape hatches are minimal and
justified.

### 4.3 Low (defense-in-depth)

**L-01 · The tenant-isolation invariant is enforced by manual grep, not by an architecture test.**
`backend/src` · `tests/CMPlus.Architecture.Tests/`

The entire cross-tenant guarantee rests on the ambient global query filter. The only constructs that
disable it are `IgnoreQueryFilters` (2 sanctioned production sites) and raw SQL (`FromSql*`/
`ExecuteSql*`, 1 sanctioned site that re-asserts `TenantId`). There is **no** architecture test
pinning "these constructs appear only in the sanctioned types" — each sprint's review re-establishes
it by grep. A future handler/reader that calls `IgnoreQueryFilters` or introduces raw SQL without
re-asserting the tenant boundary would silently leak, and no test would fail. The layering tests
(`LayeringTests`) already keep `CMPlus.Application` free of any `Microsoft.EntityFrameworkCore`
reference — a strong structural reason a *handler* cannot bypass the filter directly — but they do not
constrain **Infrastructure**, which is where all three bypass sites live.

**Fix.** Add an architecture test (mirroring `TenantIsolationTests`' self-enforcing philosophy) that
asserts, by scanning IL/Roslyn or a source grep in CI, that `IgnoreQueryFilters` appears only in
`UserReader` and `EfIdempotencyStore`, and that `FromSql*`/`ExecuteSql*` appears only in
`CpmScheduleRepository` — each new site a deliberate, reviewed addition rather than an accident. This
converts a per-sprint manual check into a permanent guard, exactly as the reflection-driven fixture
check converts "did anyone add an `ITenantOwned` type without coverage" into a loud test failure.

### 4.4 Informational (no severity)

- **The idempotency retention sweep writes no `AuditLog` row** for its cross-tenant deletion — only
  structured-log counts. Acceptable: it deletes transient dedup records, not business/legal data, and
  ADR-0002's "with audit" is proportionately satisfied by logging for housekeeping. Recorded so the
  distinction (logged, not `AuditLog`-audited) is explicit rather than assumed.
- **Two Sprint 15 handlers (#58, #59) are uncommitted WIP** and were non-compiling mid-audit. Both
  are tenant-scoped by static review (#59 explicitly checks `ProjectRepository.FindAsync`, tenant-
  filtered; #58 reads only ambient-filtered `ApprovalPolicies` + `AuditLogs`; both endpoints are
  `[Authorize(Roles=Admin)]` on `tenants/{tenantId}/…` with a `tenantId != tenantProvider.TenantId`
  bare-404 cross-check). They must go through the full pipeline (build green + qa) before close; this
  review cannot substitute for that.

---

## 5. Areas explicitly checked and found sound

- **Global filter application (ADR-0002).** `CmPlusDbContext.ApplyTenantQueryFilters` reflects over
  the built model and applies `HasQueryFilter(e => e.TenantId == _tenantProvider.TenantId)` to every
  `ITenantOwned` type — no per-entity wiring to forget. `StampTenantId` (in all four `SaveChanges`
  overrides) forces `TenantId` on every added `ITenantOwned` entity from `ITenantProvider`,
  overwriting any payload value. Both proven per-type by `TenantIsolationTests` (§2.1).
- **Tenant source is JWT-only.** `HttpContextTenantProvider` reads `TenantId` solely from the JWT
  claim and **throws** if absent (fail-closed) — never from route/query/body. `TenantApprovalPolicies`
  and (WIP) `Simulate`/`GetHistory` re-check the route `tenantId` against the claim → bare 404.
- **Bulk / aggregate / reporting reads — the classic leak spot — are clean.** WBS tree (S6), Gantt
  (S6), EVM/EAC/S-Curve (S7/8) incl. `EvmDataReader.GetBacAsOfAsync`, CashFlow & Dashboard cross-
  screen aggregates (S8), Productivity Index (S12), Baseline comparison (S14), CPM-run history (S11)
  — all scope by `projectId` over ambient-filtered DbSets; §2.2 execution-proves the join subqueries
  do not cross the boundary. `ProjectReader.GetAllAsync` and every export path return only the
  current tenant's rows — no report aggregates across tenants.
- **Raw SQL is safe.** The single raw-SQL statement re-asserts `WHERE a.TenantId = {tenant}` and is
  parameterized (`ExecuteSqlInterpolatedAsync`).
- **Bulk import stamps server-side.** `ImportRepository` `AddRange` + `SaveChanges` → `StampTenantId`
  applies; its reads use the ambient-filtered `WBSNodes.Any(...)` join.
- **Append-only / audit interceptors** are unaffected by tenant scoping and continue to write one
  audit row per mutation (or one summarizing row for bulk paths via `SuppressPerEntityAudit`).

---

## 6. What could not be verified without a running system

1. **The raw-SQL tenant re-assertion** in `CpmScheduleRepository.SaveResultsAsync` — runs only on SQL
   Server (`ExecuteSqlInterpolatedAsync` cannot execute on InMemory). **Code-verified only.**
2. **ADR-0021's index behaviour** and the single-active filtered-unique-index enforcement — InMemory
   ignores unique indexes entirely.
3. **The two WIP S15 handlers** (#58, #59) — static-verified only; the tree's compile state was in
   flux and they are uncommitted.
4. **Anything HTTP-transport-level** — TLS/HSTS, CORS, cookie/token handling, response compression vs
   `application/problem+json`. No running API.
5. **Live probing** — no timing analysis, no fuzzing, no concurrency racing against a real database.

Everything in §2 is execution-verified against the real assemblies on InMemory (treat as evidence
about C# query logic only — the 2026-08-10 lesson). §1, §3, §4.3, §5 items 1/4 are code-verified.

---

## 7. Required before this item can close

Tenant isolation is **PASS** — nothing here blocks on a cross-tenant defect. To retire the review's
open threads:

1. **L-01** — add the `IgnoreQueryFilters`/raw-SQL architecture guard (defense-in-depth, small).
2. **M-02/M-04 (ADR-0018)** — land per-project assignment enforcement in the Application layer for
   every document command/query, with negative tests. Product-sized; keep it appearing here until done.
3. **ADR-0021** — index redesign + migration; validate the single-active guarantee against real SQL
   Server once a database exists (also revisit §6 items 1–2 then).
4. When the **CpmRun-retention sweep** (ADR-0019) is built, it must reuse the sanctioned cross-tenant
   `IgnoreQueryFilters` pattern and key retention on **citation, not age**.
5. Re-verify #58/#59 by execution once they compile and are committed.
