# Master Plan — Architecture & Contract Design

**Author:** `system-architect` · **Date:** 2026-07-27 · **Status:** Accepted (ADR-0007–0010)
**Upstream:** `docs/specs/master-plan/backlog-detailed.md` (po-analyst),
`docs/specs/master-plan/domain-decisions.md` (domain-expert), `docs/9.` §4–§7
**Downstream scheduling:** `docs/10. แผนพัฒนารายเฟสโดยละเอียด (Detailed Phase Execution Plan).md`

This document is the **contract** layer: who owns what, what the wire looks like, what the tables
look like. It deliberately does **not** repeat the sprint plan (see `docs/10.`), the stories
(see `backlog-detailed.md`), or the formulas (see `.claude/knowledge/domain/*`).

Scope = the three reconciled areas plus the cross-cutting rules they depend on:
EAC engine (ADR-0007), approval policy engine (ADR-0008), progress history (ADR-0009).

## 0. Repository structure and ownership

The workspace is intentionally split by concern so each layer has a clear home and the solution
remains easy to scale.

| Area | Path | Owns | Must not contain |
| :-- | :-- | :-- | :-- |
| Domain | `backend/src/CMPlus.Domain/` | entities, value objects, enums, domain rules, domain events | EF Core, HTTP, UI, file I/O |
| Application | `backend/src/CMPlus.Application/` | CQRS handlers, validators, DTOs, use-case orchestration, abstractions | infrastructure implementations, UI components |
| Infrastructure | `backend/src/CMPlus.Infrastructure/` | EF Core, repositories, migrations, external services, file storage, background workers | domain rule decisions, UI code |
| Web API | `backend/src/CMPlus.WebApi/` | API host, DI composition root, controllers, middleware, auth, OpenAPI | business logic that belongs in Application/Domain |
| Frontend | `web/` | React screens, module slices, client state, charts, PWA shell, styling | backend code, EF Core, deployment scripts |
| Infra | `infra/` | Dockerfiles, local environment composition, deployment scaffolding, future IaC | feature code or API code |
| Tests | `backend/tests/` | .NET unit, architecture, and integration tests (xUnit) | production logic |
| Cross-stack tests | `tests/` (top-level, sibling of `backend/`/`web/`) | tooling that belongs to neither stack alone: k6 perf scripts (`tests/perf/`, e.g. S4-QA-01's `wbs-tree.k6.js`), shared large-dataset generators (S4-DB-02's `seed-large-project.sql`) consumed by both the API perf test and the Gantt frontend | anything owned by a single stack — that belongs in `backend/tests/` or `web/` (e.g. `web/e2e/` for Playwright) instead |
| Docs | `docs/` | product specs, architecture contracts, phased plans, prototype references | source code |

Structure rules:

- New backend code starts in `backend/src/CMPlus.Domain` or `backend/src/CMPlus.Application` and only moves
  outward when a framework concern requires it.
- UI features belong in `web/src/features/<module>/` and should stay module-based rather than
  becoming one large shared screen folder.
- Infrastructure concerns must not leak business rules back into the domain layer.
- Any new top-level folder needs an explicit ownership decision before it is added — `backend/`
  (this reconciliation pass) and the cross-stack `tests/` row above are the two such decisions
  made so far; both are recorded here rather than left implicit in a sprint task row.

---

## 1. Component design — layer ownership

Layering per ADR-0001; dependencies point inward only.

### 1.1 EAC / EVM (ADR-0007, ADR-0009)

| Layer | Type | Responsibility |
| :-- | :-- | :-- |
| Domain | `Project` (`EacVariantDefault`, `EacCustomPerformanceFactor`, `EacManualEtc`) | invariants: `PF_c > 0`, `ETC_manual >= 0`, variant requires its input |
| Domain | `ActivityProgressLog`, `EvmPeriodSnapshot` | append-only; no update/delete methods exist |
| Application | `EacCalculator` (pure, static-friendly) | unified $ETC = PF\times(BAC-EV)$; returns every variant with value-or-null+reason |
| Application | `EvmEngine` (pure) | PV/EV/AC/SV/CV/SPI/CPI at a data date; consumes `IProgressHistoryReader` |
| Application | `GetEvmQueryHandler` | orchestrates reader → engine → calculator → DTO |
| Application | `SetEacVariantDefaultCommandHandler`, `CloseEvmPeriodCommandHandler` | mutations (audited) |
| Infrastructure | `ProgressHistoryReader : IProgressHistoryReader` | the step-function SQL of ADR-0009 |
| WebApi | `EvmController` (v1) | routing, ProblemDetails, no logic |

**Call sequence — `GET /api/v1/projects/{id}/evm?dataDate=&eacVariant=`**

```
EvmController
  └─> GetEvmQuery(projectId, dataDate, eacVariantOverride?)
        ├─> IProgressHistoryReader.GetProgressAsOf(projectId, t)      // ADR-0009 step function
        ├─> IActualCostReader.GetAcAsOf(projectId, t)
        ├─> EvmEngine.Compute(budgets, progressAsOf, plannedCurve, ac) -> EvmCore
        ├─> EacCalculator.ComputeAll(EvmCore, project.EacCustomPerformanceFactor,
        │                            project.EacManualEtc)            -> EacVariantResult[]
        └─> map -> EvmResponse { core, variants[], selectedVariant }
```

`eacVariantOverride` only changes `selectedVariant` in the response — **all** computable variants
are returned regardless, so the client switches with no round trip.

### 1.2 Approval (ADR-0008)

| Layer | Type | Responsibility |
| :-- | :-- | :-- |
| Domain | `ApprovalPolicy`, `ApprovalPolicyRule` | band validation (non-overlapping, gap-free per `StepNo`); `Version+1` on edit, never mutate |
| Domain | `ApprovalAction` | append-only ledger of human acts |
| Domain | `PaymentCertificate`, `VariationOrder` | own their state machines + `RowVersion` concurrency |
| Application | `IApprovalRoutingService` / `ApprovalRoutingService` (pure) | the 9-step algorithm of approval-workflow §5.3; fail-closed |
| Application | `SubmitXCommandHandler`, `ApproveXCommandHandler`, `ReturnForRevisionCommandHandler`, `RejectCommandHandler` | transition + effects + audit |
| Application | `SimulateRoutingQueryHandler` (Sprint 15) | dry-run chain for the admin UI |
| Infrastructure | EF configurations, seeders | `TH-Default-VO` / `TH-Default-IPC` seeds |
| WebApi | `TenantApprovalPoliciesController`, `PaymentCertificatesController`, `VariationOrdersController` | |

**Call sequence — submit a document**

```
SubmitCommand
  ├─> document.AssertSubmittable()                       // Domain invariants, money fields valid
  ├─> routingAmount = document.RoutingAmount()           // VO: |Amount| · IPC: G_k
  ├─> ApprovalRoutingService.Resolve(tenantId, projectId, documentType, routingAmount, submittedAt)
  │       ├─ project-scoped active policy, else tenant-scoped
  │       ├─ rules where MinAmount <= A < MaxAmount (or MaxAmount IS NULL), ordered by StepNo
  │       ├─ VO only: append CumulativeVoEscalationRole if cumulative % exceeded
  │       └─ empty chain -> Result.Fail(ApprovalPolicyGap)   // 422, never auto-approve
  ├─> document.AttachChain(chain, policyId, policyVersion)   // snapshot pinned to the document
  └─> AuditLog + ApprovalAction(Submit)
```

`Approve` re-checks: actor holds the current step's role, actor ≠ creator/submitter unless
`AllowSelfApproval`, actor has not already satisfied another step of this chain, `RowVersion`
unchanged (else `409`).

### 1.3 Progress history (ADR-0009)

```
BatchRecordProgressCommand (one PeriodEndDate, N activities)
  └─ per activity: Activity.RecordProgress(periodEndDate, pct, qty, userId, source)
        ├─ append ActivityProgressLog row (immutable)
        └─ if periodEndDate is the new maximum -> update Activity.ProgressPercentage cache
           else -> leave cache untouched (backdated correction)
  └─ single transaction · single AuditLog row summarising the batch
```

Write paths that MUST go through `Activity.RecordProgress`: batch grid, Excel import,
offline outbox replay, photo-linked update. No handler may assign `ProgressPercentage` directly.

---

## 2. API contract (REST v1)

All endpoints: tenant from JWT claim only (ADR-0002), `ProblemDetails` on error, `camelCase` JSON,
money as decimal-safe strings, dates ISO 8601 with offset.

### 2.1 EVM

```
GET /api/v1/projects/{projectId}/evm?dataDate=2026-06-30T00:00:00%2B07:00&eacVariant=Atypical
200 →
{
  "projectId": "…", "dataDate": "2026-06-30T00:00:00+07:00",
  "bac": "1000000.00", "pv": "400000.00", "ev": "300000.00", "ac": "350000.00",
  "sv": "-100000.00", "cv": "-50000.00", "spi": "0.75", "cpi": "0.857143",
  "tcpiBac": "1.0769", "tcpiEac": "0.8571",
  "selectedVariant": "Atypical",
  "variants": [
    { "variant": "CpiBased",    "performanceFactor": "1.166667", "etc": "816666.67",
      "eac": "1166666.67", "vac": "-166666.67", "computable": true,  "reason": null },
    { "variant": "Atypical",    "performanceFactor": "1.000000", "etc": "700000.00",
      "eac": "1050000.00", "vac": "-50000.00",  "computable": true,  "reason": null },
    { "variant": "CpiSpiBased", "performanceFactor": "1.555556", "etc": "1088888.89",
      "eac": "1438888.89", "vac": "-438888.89", "computable": true,  "reason": null },
    { "variant": "BottomUpEtc", "performanceFactor": null, "etc": null, "eac": null,
      "vac": null, "computable": false, "reason": "ManualEtcNotSet" },
    { "variant": "CustomPf",    "performanceFactor": null, "etc": null, "eac": null,
      "vac": null, "computable": false, "reason": "CustomPfNotSet" }
  ],
  "warnings": []
}
```

`reason` enum: `NotStarted` · `NoActualCost` · `NoPlannedValue` · `ZeroCpi` · `ManualEtcNotSet` ·
`CustomPfNotSet`. `warnings` may contain `EarnedValueExceedsBudget`.
Unknown `eacVariant` → `400` `type=".../problems/invalid-eac-variant"` (never a silent fallback).

```
PUT  /api/v1/projects/{projectId}/eac-default        { "variant": "CpiSpiBased" }   PM|QS|Executive → 200 | 403
PUT  /api/v1/projects/{projectId}/eac-inputs         { "manualEtc": "760000.00", "customPerformanceFactor": "1.2000" }  (Sprint 14)
POST /api/v1/projects/{projectId}/evm/snapshots      { "dataDate": "…" } → 201 {snapshotId} | 409 duplicate
GET  /api/v1/projects/{projectId}/evm/snapshots?from=&to=
```

### 2.2 Approval policies

```
GET /api/v1/tenants/{tenantId}/approval-policies?documentType=VariationOrder      Admin → 200 | 403 | 404
200 → { "documentType": "VariationOrder", "version": 3, "isActive": true,
        "allowSelfApproval": false, "cumulativeVoEscalationPct": "10.00",
        "cumulativeVoEscalationRole": "Executive",
        "rules": [ { "stepNo": 1, "minAmount": "0.00", "maxAmount": "500000.00",
                     "requiredRole": "ProjectManager", "quorumCount": 1 }, … ] }

PUT  /api/v1/tenants/{tenantId}/approval-policies/{documentType}   Admin → 200 (creates Version+1) | 400
     400 body carries { "invalidStepNo": 2, "problem": "BandOverlap" | "BandGap" }

POST /api/v1/tenants/{tenantId}/approval-policies/{documentType}/simulate  (Sprint 15)
     { "amount": "3200000.00", "cumulativeApprovedVo": "46000000.00" }
     200 → { "chain": ["ProjectManager","ProjectDirector","Executive"], "escalationApplied": true }
```

Cross-tenant request → `404` (never `403` — do not confirm another tenant exists).

### 2.3 Document workflow (identical shape for both document types)

```
POST /api/v1/{payment-certificates|variation-orders}/{id}/submit               → 200 | 422 ApprovalPolicyGap
POST /api/v1/{…}/{id}/approve               { "comment": "…" }                 → 200 | 403 | 409 | 422
POST /api/v1/{…}/{id}/return-for-revision   { "comment": "…" }  (required)     → 200 | 403
POST /api/v1/{…}/{id}/reject                { "comment": "…" }  (final step)   → 200 | 403
POST /api/v1/payment-certificates/{id}/record-payment { "reference": "…", "paidAt": "…" } → 200
GET  /api/v1/{…}/{id}/approval-actions      → append-only history
```

Error `type` values that the frontend must handle explicitly:
`approval-policy-gap` (422) · `self-approval-not-permitted` (403) · `not-current-step` (403) ·
`concurrent-transition` (409) · `document-immutable` (409).

### 2.4 Progress

```
POST /api/v1/projects/{projectId}/progress-entries
     { "periodEndDate": "…", "entries": [ { "activityId": "…", "progressPercentage": "42.50",
                                            "actualQuantity": null } ] }
     → 201 { "created": 20, "cacheUpdated": 18, "backdated": 2 }
GET  /api/v1/projects/{projectId}/progress-entries?activityId=&asOf=
```

Site-facing writes accept `Idempotency-Key` (Sprint 13): same key + same payload → replayed
response; same key + different payload → `409`.

---

## 3. Data model notes (hand-off to `database-engineer`)

Full column list and landing sprint: `docs/10.` §3. Rules that are **not** negotiable:

- `TenantId` on every tenant-owned table, and it is the **leading column** of every composite index
  (ADR-0002). A missing filter is release-blocking.
- Money `decimal(18,2)`, percent `decimal(5,2)`, PF `decimal(9,4)` (stored) / `decimal(9,6)`
  (snapshot, to keep the audit reproducible), ids `Guid`, dates `DateTimeOffset`.
- Mandatory indexes:
  - `ActivityProgressLog (TenantId, ActivityId, PeriodEndDate DESC)` — drives the ADR-0009 step
    function; must produce an index **seek**, and `(TenantId, PeriodEndDate)` for period rollups.
  - `EvmPeriodSnapshot` unique `(TenantId, ProjectId, DataDate)`.
  - `ApprovalPolicy` unique **filtered** index on `(TenantId, ProjectId, DocumentType)
    WHERE IsActive = 1` — two active policies for one scope is a data-integrity bug, not a
    runtime tie-break.
  - `ApprovalAction (TenantId, DocumentType, DocumentId, RevisionNo, StepNo)`.
  - `ProjectFinanceLedger (TenantId, ProjectId, Category)` — `SUM()` of retention/advance must seek.
  - `VariationOrder (TenantId, ProjectId, Status)` — cumulative approved-VO sum runs on every submit.
- Append-only tables (`ActivityProgressLog`, `ApprovalAction`, `ProjectFinanceLedger`,
  `DailyWeatherLog`, `EvmPeriodSnapshot`) get **no** update/delete path in EF configuration or API.
  Corrections are compensating rows.
- `RowVersion` (`rowversion`) on `PaymentCertificate` and `VariationOrder` for optimistic concurrency.
- EF migrations must be exportable as an idempotent SQL script (ADR-0010).

---

## 4. Frontend contract

Feature folders (`web/src/features/<module>/`), Zustand for UI state only, React Query for server
cache — never mirror server data into Zustand.

| Module | New/changed surface | State |
| :-- | :-- | :-- |
| `evm/` | `EacVariantSelect` (3 options in v1, 5 in Sprint 14), 12-metric table, `SCurveChart` | React Query holds the whole `EvmResponse`; the selector is **local UI state** switching between already-fetched variants — no refetch |
| `info/` | Retention / Advance / `RetentionCapPercentage` / `AdvanceRecoveryMethod` fields; Sprint 14 adds `manualEtc` / `customPerformanceFactor` | form state local; mutation invalidates the project query |
| `payment/` | certificate panel **with an advance-recovery line and a retention-cap indicator** (the prototype lacks both — domain-decisions §2.6), `ApprovalChainBar` | server state via React Query |
| `vo/` | reuses `ApprovalChainBar`; "ตีกลับ" maps to `return-for-revision`, **not** `reject` | |
| `tenant-admin/` | policy editor (steps × amount bands × roles), inline band validation, Sprint 15 adds version history + routing simulator | reachable only with `role = Admin`, from a tenant-level entry point outside the 13-screen project nav |
| `wbs/` | batch "อัปเดตความคืบหน้า" grid (virtualized, ADR-0004), confirm-on-decrease | writes go to the outbox when offline (Sprint 13) |

Offline behaviour (ADR-0005) applies to progress entries, weather logs and photos: enqueue in the
IndexedDB outbox with an idempotency key, show per-item sync status, flush via Background Sync,
and never remove an item from the queue before the server confirms.

Rendering rules: no DOM-per-bar Gantt, virtualize any list that can exceed ~200 rows,
and **never colour-code EAC variants by assumed ordering** — the order reverses when CPI > 1
(evm-formulas.md, Fixture D).

---

## 5. Task breakdown

The full, sprint-by-sprint, per-discipline breakdown with Definition of Done, dependencies and
artifact paths lives in **`docs/10.`** §5–§9. Interfaces between the parallel tracks:

| Producer | Consumer | Interface frozen at |
| :-- | :-- | :-- |
| `database-engineer` (schema, §3) | backend-developer | Sprint 1 migration (Project/EAC/finance columns + `ActivityProgressLog`), Sprint 2 (approval tables) — **schema freeze after Sprint 2**, changes need an ADR |
| `backend-developer` (`EvmResponse`, §2.1) | frontend-developer | Sprint 7; the variant array shape is additive-only afterwards |
| `backend-developer` (`IApprovalRoutingService`, §1.2) | backend-developer (S9/S10 state machines) | Sprint 2 |
| `backend-developer` (workflow endpoints, §2.3) | frontend-developer (`ApprovalChainBar`) | Sprint 9, reused unchanged by VO in Sprint 10 |
| `qa-engineer` (fixtures) | everyone | Phase 0 — fixtures are transcribed before any engine is written |

---

## 6. Risks & alternatives considered

| Decision | Alternative rejected | Why |
| :-- | :-- | :-- |
| 5-variant engine, 3-variant v1 UI (ADR-0007) | Build only 3 variants | The 4th/5th cost nothing in the engine but need project inputs, validation and guidance in the UI. Splitting engine surface from UI surface gets P6/MSP reconciliation parity now and defers only the UX. |
| — | Build all 5 in the UI at Sprint 7 | Adds two persisted inputs, permissioning and help content to the sprint that already carries the EVM engine and S-Curve. No consumer needs them before a re-forecast/close-out cycle. |
| Amount-tiered policy from Sprint 2 (ADR-0008) | Boolean matrix first, migrate later | The boolean check is a different call shape from a chain, so the hook, API, admin UI and tests would be rewritten — and the data to migrate is approval history (legal evidence). The rich model has no design work left (algorithm + fixtures already written). |
| — | Amount-tiered but starting at Sprint 9 | Sprint 9 and 10 would then design the routing engine under schedule pressure, with two consumers landing at once. |
| Step function from `ActivityProgressLog` (ADR-0009) | Reconstruct from `AuditLog` JSON | Fragile, slow, and couples the reporting engine to an audit format. |
| — | Interpolate between log dates | Invents progress that was never reported; a certified S-curve point must be traceable to a submitted entry. |
| `EvmPeriodSnapshot` for closed periods | Always recompute | A backdated correction would silently change an already-issued report. Snapshots keep "never rewrite history" true where it legally matters while still allowing corrections to flow into live series. |
| Cloud-agnostic + gate (ADR-0010) | Pick a provider now | The human deferred it; guessing risks provider-specific rework. |
| — | Defer all infra work to Sprint 16 | Would leave containerization, CI and security scanning unbuilt for 15 sprints and concentrate all deployment risk in the final two weeks. |

Live risks (owner, mitigation and the sprint that re-checks each) are tracked in `docs/10.` §11 —
notably R-01 (P6 EAC setting), R-02 (retention-cap prototype inconsistency), R-03 (VAT/WHT),
R-08 (Sprint 2 scope from ADR-0008), R-11 (`ActivityProgressLog` volume) and R-12 (banker's rounding).
