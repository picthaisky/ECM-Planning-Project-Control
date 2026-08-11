# Sprint 9 Security Review (S9-SEC-01)

**Reviewer:** `security-auditor` · **Date:** 2026-08-09 · **Scope per `docs/10.` §8 Sprint 9:**
new attack surface = Tenant Admin policy-write endpoint + amount-tiered routing + Payment
Certificate approval chain + certificate/policy id IDOR. Read against ADR-0002 (tenant
isolation), ADR-0008 (amount-tiered, version-pinned approval policy engine),
`.claude/knowledge/domain/approval-workflow.md` §3.4/§5.3/§6, and
`docs/specs/master-plan/design.md` §2.2/§2.3. Continues `sprint-02.md` and `sprint-03.md`.

## Verdict

> **CURRENT STATUS (2026-08-09, after the fix cycle): PASS — Sprint 9 can close.**
> H-01 and H-02 were both fixed and independently re-verified by execution; see **§9**, which is
> the authoritative current verdict. Everything from here to §8 is the *original* review that
> found them, kept verbatim as the record of what was wrong and why — do not read §1's original
> verdict line below as the present state.

**Original verdict (first pass): FAIL — 2 High findings open (H-01, H-02). Zero Critical.**
`docs/10.` §8's security-auditor row states "Critical/High = ปิด sprint ไม่ได้", so Sprint 9
could not close until H-01 and H-02 were fixed and re-verified. Both were in code that
**Sprint 10 (Variation Order) reuses unchanged**, so fixing them then was strictly cheaper than
fixing them after VO inherited them.

## Method and its limits — read this before trusting any statement below

Docker Desktop cannot start on this machine (requires Administrator; see
`docs/perf/gantt-frontend-s6.md` §3), so **there is no SQL Server and no running API**. Nothing
in this review is a live penetration test.

- **Code-verified** — read the production source directly.
- **Execution-verified** — eight throwaway probe programs in the session scratchpad referencing
  the *real* `CMPlus.Application`/`CMPlus.Infrastructure`/`CMPlus.Domain` assemblies, driving the
  *real* command handlers, repositories and `CmPlusDbContext` (EF Core InMemory). **No repository
  file was created, modified or deleted** — confirmed by `git status`.
- Re-ran the suite: `dotnet test backend/CMPlus.sln -c Release` → **822/822 passing**
  (Domain 207 + Application 345 + Architecture 10 + Integration 260).
- `dotnet list package --vulnerable --include-transitive` → **zero** across all 8 backend
  projects. `npm audit --omit=dev` in `web/` → **0 vulnerabilities**.

What could **not** be checked is in §7.

---

## 1. DoD checklist

| # | DoD item | Verdict |
| :-- | :-- | :-- |
| 1 | Policy write is Admin-only | **MET** |
| 2 | Approver resolution 100% tenant-scoped | **MET** |
| 3 | IDOR closed on certificate and policy ids (incl. same-tenant-wrong-project) | **PARTIAL** — cross-tenant closed; policy ids not client-addressable; **same-tenant-wrong-project not closed** (M-02) |
| 4 | 403/404 never leak another tenant's existence | **MET** |
| 5 | `ApprovalAction` cannot be altered retroactively | **PARTIAL** — met at the API surface, **not structurally enforced** at the persistence layer (M-01) |

### 1.1 Policy write is Admin-only — MET

`TenantApprovalPoliciesController.cs:24` applies `[Authorize(Roles = nameof(UserRole.Admin))]` at
**class** level, covering the new `PUT {documentType}` (line 46) as well as the Sprint 2 `GET`. A
route `tenantId` not matching the caller's JWT claim returns a bare `NotFound()` before any I/O
(lines 31-34 GET, 53-56 PUT). `UpdateApprovalPolicyCommand` carries no tenant id; the tenant comes
from `ITenantProvider` (handler line 32), which reads the JWT claim only.

Ordering note, checked deliberately: for a **non-Admin** caller the framework role check fires
before the controller body, so a non-Admin probing another tenant gets 403 rather than 404. Not an
existence leak — the 403 is a pure function of the caller's own role, identical for any tenant id.

`RequiredUserId` is hard-coded `null` in `UpdateApprovalPolicyRequest.cs:11`, so the named-person
override is not mass-assignable. `ApprovalPolicy.ProjectId` is likewise not settable from the
request (handler passes `projectId: null`), so a tenant Admin cannot create a project-scoped
override that would out-rank the tenant default.

### 1.2 Approver resolution is tenant-scoped — MET

Every read on the approval path goes through the ADR-0002 global query filter
(`CmPlusDbContext.cs:104-131`, applied by reflection to every `ITenantOwned` type — `ApprovalPolicy`,
`ApprovalPolicyRule`, `ApprovalAction`, `PaymentCertificate`, `ProjectFinanceLedger` all qualify).
`ApprovalPolicyReader` (lines 16-48), `PaymentCertificateRepository.FindAsync` (10-11) and
`ApprovalActionRepository.GetHistoryAsync` (15-23) carry no explicit `TenantId` predicate — correct,
the filter is ambient and cannot be forgotten. `ApprovalRoutingService` is pure and only sees
policies the tenant-filtered reader handed it.

A repo-wide grep for `IgnoreQueryFilters`/`FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlRaw`/
`ExecuteUpdate`/`ExecuteDelete` across `backend/src` returns **one** production hit, unchanged
since Sprint 2: `UserReader.cs:20` (login, which by definition has no tenant context yet). No raw
SQL, therefore no injection surface on this sprint's code.

`GetByIdAsync` deliberately omits the `IsActive` filter (lines 41-47) so a superseded version stays
readable for in-flight documents. It does **not** omit the tenant filter — a cross-tenant pinned id
resolves to `null`.

**Execution-verified (probe 6):** two tenants sharing one database; tenant B calling `approve` and
`submit` on tenant A's certificate id both returned `PaymentCertificateNotFound`.

### 1.3 IDOR on certificate and policy ids — PARTIAL

- **Policy ids:** structurally absent. No endpoint addresses a policy by `Id`; the write API is
  keyed on `{documentType}` and scoped to the JWT tenant. Nothing to enumerate.
- **Certificate ids, cross-tenant:** **closed**, verified by execution. `ResultProblemMapper.cs:100`
  maps to a 404 `not-found` with a generic title — identical to every other 404, so "wrong tenant"
  and "does not exist" are indistinguishable.
- **Certificate ids, same-tenant-wrong-project:** **not closed.** See M-02. No
  `ProjectMember`/`UserProject` concept exists anywhere in `backend/src`. Any authenticated tenant
  user holding the current step's role can approve a certificate belonging to **any** project of
  that tenant. Contrast Sprint 3, where `GetImportJobQueryHandler` does close this — but only
  because that route carries a `projectId` to compare against; these routes are flat.

### 1.4 403/404 do not leak tenant existence — MET

`TenantApprovalPoliciesControllerTests.Cross_Tenant_Request_Returns_A_Bare_404...` and
`...Cross_Tenant_Put_Returns_A_Bare_404` (read, not trusted): the first asserts the body contains
neither `Executive` (tenant B's real rule content) nor `cumulativeVoEscalationPct`. An Admin
requesting a document type their own tenant has no policy for also gets 404, so "wrong tenant" and
"not configured" are the same response. `GlobalExceptionHandler.cs:39-41` returns a fixed string
for 500s, never echoing `exception.Message`, SQL or stack traces.

### 1.5 `ApprovalAction` cannot be altered retroactively — PARTIAL

**True at the API surface.** No mutator, no public setter (`ApprovalAction.cs:17-46`);
`IApprovalActionRepository` exposes only `Add` and `GetHistoryAsync`; no controller, handler or
repository updates or deletes one.

**Not true structurally.** Execution-verified (probe 7), with an ordinary `CmPlusDbContext`:

```
context.Entry(action).Property("Comment").CurrentValue = "TAMPERED";
await context.SaveChangesAsync();          // -> persisted; re-read Comment = "TAMPERED"

context.ApprovalActions.Remove(action);
await context.SaveChangesAsync();          // -> row gone; remaining rows = 0
```

Both succeeded. `ApprovalActionConfiguration.cs:21-22` is a *comment* saying append-only; there is
no interceptor and no database grant/trigger. Same for `ProjectFinanceLedger`, `ActualCostEntry`,
`EvmPeriodSnapshot`, `AuditLog`, and `PaymentCertificate`'s money-field freeze. See M-01.

---

## 2. Findings — High (block sprint close)

### H-01 · Approve/Reject/ReturnForRevision resolve step authority from the pinned policy's **entire** rule set, ignoring the amount band that routed the document — cross-tier privilege escalation on a money-moving action

`backend/src/CMPlus.Application/Features/Payment/PinnedApprovalChainResolver.cs:52-55`

```csharp
var steps = policy.Rules
    .OrderBy(r => r.StepNo)
    .Select(r => new ApprovalChainStep(r.StepNo, r.RequiredRole, r.QuorumCount))
    .ToList();
```

Compare the routing algorithm it is supposed to reproduce (`ApprovalRoutingService.cs:55-59`,
approval-workflow.md §5.3 step 3):

```csharp
List<ApprovalChainStep> chain = policy.Rules
    .Where(r => r.MinAmount <= routingAmount && (r.MaxAmount is null || routingAmount < r.MaxAmount))
    .OrderBy(r => r.StepNo)
    ...
```

The `Where` clause — the entire amount-tiering mechanism, and the reason ADR-0008 exists — is
**missing**. `PaymentCertificate` deliberately stores only `CurrentStepNo`/`TotalSteps` + the policy
pin (lines 33-47), so the chain is re-derived on every decision, and `RequireStep`
(`PinnedApprovalChainResolver.cs:66-70`) does `FirstOrDefault(s => s.StepNo == stepNo)` over that
unfiltered set. When two rules share a `StepNo` across different bands — which the write API
explicitly permits, since `FindOverlappingStepNo` rejects only *overlapping* same-`StepNo` bands —
the wrong band's rule wins.

Affected: `ApprovePaymentCertificateCommandHandler.cs:31-32,40-43`,
`RejectPaymentCertificateCommandHandler.cs:38-39,47-51`,
`ReturnPaymentCertificateForRevisionCommandHandler.cs:32,40`.

**Attack scenario (execution-verified, probes 1 and 4).** A tenant Admin configures an ordinary
tiered DoA through the new Sprint 9 editor:

| StepNo | MinAmount | MaxAmount | RequiredRole |
| --: | --: | --: | :-- |
| 1 | 0.00 | 1,000,000.00 | QS |
| 1 | 1,000,000.00 | *null* | PM |
| 2 | 1,000,000.00 | *null* | ProjectDirector |

The write API accepts it (no band overlap, contiguous StepNo sequence per covered interval). A
฿5,000,000 certificate routes correctly — `submit` returns `totalSteps=2`, chain
`[PM(1), ProjectDirector(2)]`. But at approve time:

```
QS  (band [0,1M) role - NOT in this certificate's chain) approve => success=True  newStep=2
PM  (the policy's real step-1 role for 5,000,000)        approve => success=False
                                                    error=PaymentCertificateNotAuthorizedForApprovalStep
```

A **QS cleared a step the tenant's own DoA reserves for a PM**, and the legitimate PM is locked
out. On a single-step high-value band the same defect certifies the document outright — posting the
retention accrual and advance-recovery ledger rows (handler lines 86-89) and making the certificate
payable. The `ApprovalAction` evidence row records the QS as a valid step-1 approver, so the audit
trail attests to an approval that never had authority behind it.

**Two further manifestations of the same root cause:**

1. `ReturnForRevision` (probe 3): an out-of-band QS returned the same ฿5,000,000 certificate to
   `Draft`, bumping `RevisionNo` to 2 and voiding the chain — a denial vector against a payment
   claim by someone with no authority over it.
2. **Sprint 10 inheritance.** The cumulative-VO escalation step is *synthesised at routing time*
   (`ApprovalRoutingService.cs:75`) with **no `ApprovalPolicyRule` row behind it**. When VO reuses
   `PinnedApprovalChainResolver`, any tenant whose VO policy lacks a rule at that synthesised
   `StepNo` hits `RequireStep`'s `InvalidOperationException` → HTTP 500 on every approval attempt,
   permanently stranding an escalated VO. Fixture R4 masks this only by accident (TH-Default-VO
   happens to own a StepNo 3 rule in a different band).

**Fix.** Snapshot the resolved chain onto the document at Submit instead of re-deriving it — a
`PaymentCertificateApprovalStep` child collection (or a JSON chain column) carrying
`StepNo`/`RequiredRole`/`QuorumCount` as resolved. That is the shape approval-workflow.md §4 already
prescribes, it makes the escalation step representable, and it removes the whole re-derivation bug
class before Sprint 10 builds on it. Minimum viable alternative: persist the routing amount on the
document and re-apply the identical band predicate inside `PinnedApprovalChainResolver` — but that
still cannot represent the synthesised escalation step, so it only defers the Sprint 10 half.

**QA follow-up.** A fixture with two rules sharing a `StepNo` across different bands and *different
roles* — the whole existing suite uses policies whose duplicate StepNos carry the same role, which
is exactly why 822 tests pass over this defect.

### H-02 · `QuorumCount` is accepted, persisted and echoed by the new policy-write API but enforced nowhere — a configured dual-control clears on one signature

`UpdateApprovalPolicyRequest.cs:9,11` · `UpdateApprovalPolicyCommandHandler.cs:59` ·
`ApprovePaymentCertificateCommandHandler.cs:67` · `PaymentCertificate.cs:259-286`

`UpdateApprovalPolicyRuleRequest` takes `int QuorumCount = 1` straight from the request body;
`UpdateApprovalPolicyCommandValidator.cs:26` validates only `>= 1`; `ApprovalPolicyRule` stores it;
`ToDto` reflects it back so the Admin — and the S9-FE-03 editor — sees the value they set.
`ApprovalChainStep` carries it. **No code reads it.** `PaymentCertificate.Approve` advances
`CurrentStepNo` (or certifies) on the first matching approval; there is no distinct-approver count.

approval-workflow.md §6.2 is unambiguous: "A step clears only when `QuorumCount` *distinct* users of
that role have approved it." ADR-0008 lists `QuorumCount > 1` as "schema-present but deliberately
**not surfaced** in v1" — S9-BE-06 surfaced it in the write contract without building enforcement.

**Attack scenario (execution-verified, probe 2).** An Admin configures the IPC final step with
`quorumCount: 2` — the organisation's two-signature rule for certifying payment. The PUT returns
`200` and echoes `quorumCount: 2`. One QS then approves:

```
policy write with QuorumCount=2 accepted? True; echoed quorum=2
submit ok=True totalSteps=1
ONE QS approves a QuorumCount=2 step => success=True status=Certified
```

Certified on a single signature, ledger rows posted. This is a security control the product
actively tells the customer is switched on and that silently is not. Sprint 10 inherits it for VO.

**Fix — choose one, do not leave it ambiguous:**

1. **Enforce it.** In `ApprovePaymentCertificateCommandHandler`, count distinct `ActorUserId`s with
   `Action == Approve && RevisionNo == certificate.RevisionNo && StepNo == CurrentStepNo` from the
   history already loaded at line 54, and only advance when that count + 1 reaches
   `currentStep.QuorumCount`. The `DuplicateChainApprover` check (lines 55-60) already guarantees
   distinctness — but it must be reconciled, since today it forbids one user approving *any* two
   steps of a chain.
2. **Or reject it at the boundary,** per ADR-0008's "not surfaced in v1": have the validator reject
   `QuorumCount != 1` with a specific error code until the engine supports it.

Either way, add an Architecture or Domain test asserting the write API and the enforcement engine
agree, so this cannot silently reopen.

---

## 3. Findings — Medium

**M-01 · Append-only and money-freeze are C#-API conventions, not persistence-layer controls**
`ApprovalActionConfiguration.cs:21-22` · `ProjectFinanceLedgerConfiguration` ·
`ActualCostEntryConfiguration` · `EvmPeriodSnapshotConfiguration` · `PaymentCertificate.cs:425-433`

Execution-verified (probe 7) that an `ApprovalAction` row can be rewritten and deleted through an
ordinary `CmPlusDbContext`. The guarantee rests entirely on no developer writing such code. The
money-field freeze is structurally sound *as a domain invariant* — I verified by reading the whole
of `PaymentCertificate` that `SetPeriodClaim` is the only writer of
`ApprovePct`/`GrossCertifiedAmount`/`RetentionAmount`/`AdvanceRecoveryAmount`/`NetPayment`, so
"frozen from `PendingApproval`" and "`Certified` immutable forever" hold by construction — but it
has the same persistence-layer gap. Not exploitable today (no reachable path), hence Medium; but
DoD item 5 says *cannot*, and today the honest answer is *nothing stops it except discipline*.

**Fix.** A ~15-line `AppendOnlyGuardInterceptor` throwing when any entity implementing a new
`IAppendOnly` marker is `Modified`/`Deleted` at `SavingChanges` (extend to `PaymentCertificate`'s
money properties when `Status >= PendingApproval`), registered alongside the existing interceptors.
Back it with `DENY UPDATE, DELETE ON dbo.ApprovalActions TO <app-login>` in the deployment runbook.

**M-02 · No project-scoped authorization: any tenant user holding the step's role can act on any project's certificate**
`PaymentCertificateRepository.cs:10-11`

`FindAsync` filters by tenant and nothing else. Routes are flat
(`/api/v1/payment-certificates/{id}/...`) so there is no project to cross-check, and no
project-membership entity exists anywhere. A QS working on Project A can certify Project B's
interim payment certificate. Nothing crosses a tenant boundary, so Medium — but DoD item 3
explicitly asks for this case. Same *class* as Sprint 3 M-04: needs a **product decision**, not just
code. `po-analyst`/`domain-expert` should decide whether CM+ models per-project assignment; until
then record it as an accepted, documented limitation rather than silently assumed closed.

**M-03 · `ApprovalPolicy.ValidateBands`' Domain gap is reachable — a policy built outside the Application guard permanently bricks a certificate with HTTP 500**
`ApprovalPolicy.cs:177-182` · `ApprovalPolicySeeder.cs:78-80`

`AssertContiguousStepsAt` applies `.Distinct()` to the covering `StepNo`s, collapsing two rules that
share a `StepNo` over overlapping bands, so the sequence check passes.
`UpdateApprovalPolicyCommandHandler.FindOverlappingStepNo` closes this for the write API — but
`ApprovalPolicySeeder` calls `ApprovalPolicy.CreateInitialVersion` **directly**, bypassing it, and
its own doc comment anticipates "a future dedicated tenant-onboarding flow should call this too".

I was asked whether the resolver's `InvalidOperationException` "data corruption" branches are
genuinely unreachable. **They are not.** Execution-verified (probe 8), constructing the policy
exactly as the seeder does:

```
ApprovalPolicy.CreateInitialVersion ACCEPTED the overlapping same-StepNo rule set
submit ok=True totalSteps=2
attempt 1 as PM => success=True step=2/2 status=PendingApproval
attempt 1 as QS => THREW InvalidOperationException -> HTTP 500
     The pinned approval chain has no step 2 defined (resolved steps: [1,1]). This indicates data corruption.
```

Routing counts two matching rules (`TotalSteps = 2`) but both carry `StepNo 1`, so once
`CurrentStepNo` advances to 2 there is no step 2 to resolve. Every subsequent approve, reject and
return-for-revision throws → 500. The certificate is stuck in `PendingApproval` forever with money
fields frozen, and there is **no withdraw or cancel endpoint** to recover it. Today's literal seed
data is valid, so not exploitable in the current build — hence Medium.

**Fix.** Move `FindOverlappingStepNo`'s logic into `ApprovalPolicy.ValidateBands` (drop the
`.Distinct()`, or add a per-StepNo interval-intersection pass) so the invariant lives with the
aggregate and every construction path — seeder, onboarding, migration, Sprint 10 — inherits it.
Also convert `RequireStep`'s throw into a mapped `Result` failure so a corrupt policy degrades to a
clear 409/422 rather than an unrecoverable 500.

**M-04 · The `Guid.Empty` no-policy fallback is reachable through a normal flow, silently weakens a tenant's DoA, and pollutes the evidence ledger**
`ApprovalRoutingService.cs:49-53` · `PaymentCertificate.cs:247` · `PinnedApprovalChainResolver.cs:37-42`

I was told this path was "only fake-tested, never reachable through normal flows". **It is
reachable.** Execution-verified (probe 5): a tenant with no active `PaymentCertificate` policy
submits successfully, pins `ApprovalPolicyId = Guid.Empty` / `Version = 0`, and the certificate is
certified by a single `ProjectDirector`. The behaviour is *correct* per §5.3 step 6 ("restrictive,
not permissive") — the surrounding properties are the gap:

- A tenant whose real DoA is a 3-step chain silently drops to 1 step, with **no log, no metric, no
  alarm**. The system cannot distinguish "deliberately configured" from "provisioning forgot".
- `ApprovalRoutingService` conflates "no policy exists at all" (the case §5.3 step 6 addresses) with
  "a policy exists but is not currently active/effective" — both land on the fallback rather than
  the fail-closed `ApprovalPolicyGap`.
- `ApprovalPolicyId = 00000000-...` / `Version = 0` are written into append-only legal evidence,
  indistinguishable from "unpinned".

**Fix.** Log + metric whenever the fallback fires; persist it as `ApprovalPolicyId = null` plus an
explicit `UsedFallbackChain` flag rather than an all-zeros GUID; treat "policies exist but none
effective" as `ApprovalPolicyGap` (422). Add a provisioning assertion that a new tenant has both
default policies before it can be used.

---

## 4. Findings — Low (tracked, non-blocking)

| ID | Finding | Fix |
| :-- | :-- | :-- |
| L-01 | `var actorUserId = currentUser.UserId ?? Guid.Empty` (`ApprovePaymentCertificateCommandHandler.cs:34`, same idiom in Reject/Return/RecordPayment) fabricates an all-zeros actor for an append-only legal-evidence row instead of failing closed. Not reachable today, but an `ApprovalAction` attributing a payment certification to `00000000-…` is evidentially worthless. It also defeats the §6.1 self-approval guard, since `CreatedByUserId`/`SubmittedByUserId` can never be `Guid.Empty`. | Return a failure (or throw) when `UserId` is null, as `Role` already does. |
| L-02 | A submitter holding the **final** step's role can `Reject` their own submission, terminally killing it. The literal §6.1 reading is defensible and documented, but `Rejected` is terminal and unrecoverable, and Sprint 10 applies the same rule to VOs where unilateral termination has contractual weight. | `domain-expert` to confirm before Sprint 10 opens; record the answer in approval-workflow.md §6 so it stops being an implementer's judgement call. |
| L-03 | `RowVersionSaveChangesInterceptor` sits in the **production** composition root purely to work around EF Core InMemory. Its "harmless on SQL Server" claim is **code-verified correct**: the migration emits a true `[RowVersion] rowversion NOT NULL`, `IsRowVersion()` marks the property `ValueGeneratedOnAddOrUpdate` + concurrency token (before/after-save behaviour `Ignore`), so the client value is excluded from INSERT and UPDATE, and the concurrency predicate uses `OriginalValue`. Still a test-shaped dependency in production DI, and it means the 409 is proven only under simulation. | Gate registration to non-relational providers, or move it into the test harness. |
| L-04 | `GET /api/v1/{…}/{id}/approval-actions` is in `design.md` §2.3 but unimplemented, and there is no `GET` for a certificate at all — nor any create endpoint (`grep "new PaymentCertificate("` in `backend/src` returns zero production hits). The append-only history is not inspectable by the people it protects, and S9-FE-02's chain bar has nothing to read. | Add the read endpoint (tenant-scoped, same 404 discipline) alongside S9-FE-01/02. |
| L-05 | No `FallbackPolicy` on `AddAuthorization()` (`Program.cs:148`) — Sprint 2 L-03, still open. A controller added later without `[Authorize]` defaults to anonymous. Materially more serious now that money-moving endpoints live here. | `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();` plus explicit `[AllowAnonymous]` on `/auth/login` and health. |
| L-06 | Carried forward: Sprint 2 M-03 (no rate limiting on `/auth/login`; now also relevant to approval endpoints), Sprint 2 M-04 (no CSP), Sprint 3 L-06 (EPPlus still Polyform **Noncommercial** — blocking for production, restated in ADR-0014). | Sprint 15 owns M-03/M-04; EPPlus needs a commercial key before the S16 gate. |

---

## 5. Areas explicitly checked and found sound

- **"No PM escape hatch" — confirmed, structurally.** `PaymentCertificatesController.cs:47-67`:
  `approve`/`return-for-revision`/`reject` carry **no** `Roles=` attribute; only class-level
  `[Authorize]`. `submit`/`record-payment` add `CertificateCrudRoles` (QS/PM/ProjectDirector/Admin),
  a *document-lifecycle* gate, not an approval-authority gate. No global authorization policy could
  act as a second gate, no globally registered `[Authorize]` filter, no role-name shortcut in the
  three decision handlers. `Approve_Has_No_Static_Role_Gate_A_Site_User_Reaches_The_Handler...`
  proves this end-to-end by asserting the 403 carries the handler's own `not-current-step`
  ProblemDetails rather than a bare framework challenge — a genuinely good test.
  **Caveat:** the property "no role bypasses the chain" holds; "the chain is the routed chain" does
  not (H-01).
- **Version pinning — sound.** `GetByIdAsync` loads by id with no `IsActive`/`EffectiveTo` filter;
  `Deactivate` only flips `IsActive`/stamps `EffectiveTo` and no path deletes a policy or rule;
  `CreateNextVersion` always produces a new aggregate with a new `Id`; the handler persists
  deactivate + insert in one `SaveChanges`. `PaymentCertificateVersionPinningTests` proves both
  directions. **No policy edit, deactivation or rule change can re-route or strand an in-flight
  document** — except a policy that was never valid to begin with (M-03).
- **Fail-closed routing — sound.** `ApprovalRoutingService.cs:79-82` returns `PolicyGap` on an empty
  chain; mapped to **422**; `SubmitPaymentCertificateCommandHandler.cs:48-53` leaves the certificate
  in `Draft`. `PaymentCertificate.Submit` re-asserts `totalSteps >= 1` as defence in depth.
  Escalation appends only to an already non-empty chain, so it can never rescue an empty one.
  `abs(Amount)` is applied for `VariationOrder` only (correct for Sprint 10 fixture R2, and
  correctly *not* applied to a Payment Certificate's already non-negative $G_k$).
- **Separation of duties — sound where implemented.** Self-approval blocked unless the *pinned*
  policy's `AllowSelfApproval`; one human cannot satisfy two steps of the same revision, correctly
  keyed on `RevisionNo` so `ReturnForRevision` resets it. Fixture R10 passes end-to-end.
- **Concurrency shape — sound.** `TrySaveChangesAsync` catches only `DbUpdateConcurrencyException`,
  not `DbUpdateException` generally, so a constraint violation surfaces rather than being silently
  swallowed as a 409. Mapped to 409 `concurrent-transition`.
- **Atomicity.** Certificate state change, `ApprovalAction` row and the retention/advance ledger rows
  all commit in one `SaveChanges` on the same scoped `DbContext`, so a `Certified` certificate can
  never exist without its ledger rows.
- **Audit.** `AuditSaveChangesInterceptor` writes a row per changed entity in the same transaction,
  including for policy versioning; `SuppressPerEntityAudit` is not used on any approval path; the
  Sprint 2 `PasswordHash` redaction is intact.
- **Injection / error leakage / mass assignment.** No raw SQL. `ProblemDetails` only; 500s never
  echo `exception.Message`. Neither `UpdateApprovalPolicyRequest` nor the payment DTOs expose
  `TenantId`, `ProjectId`, `RequiredUserId`, `Status`, `RowVersion` or any money field to the client.
- **Dependencies.** `dotnet list package --vulnerable --include-transitive` → clean, all 8 backend
  projects. `npm audit --omit=dev` → 0 vulnerabilities.

---

## 6. Frontend note (not a finding)

`web/src/features/` contains no `payment` or `tenant-admin` directory, so S9-FE-01/02/03 are not in
this build. The S9-FE-03 DoD item "non-Admin เข้าตรงด้วย URL → 403" cannot be assessed — though the
backend already enforces it (§1.1), so a UI route guard would be defence in depth rather than the
control itself. **When those screens land, the policy editor must not present `QuorumCount` as an
editable field until H-02 is resolved.**

---

## 7. What could not be verified without a running system

1. **Real `rowversion` optimistic concurrency and the 409.** Every concurrency test runs on EF Core
   InMemory with `RowVersionSaveChangesInterceptor` supplying the token SQL Server would generate.
   Proven against a *simulation of* SQL Server semantics, not SQL Server.
2. **The filtered unique index** `IX (TenantId, ProjectId, DocumentType) WHERE IsActive = 1` versus
   the deactivate-then-insert sequence in a single `SaveChanges`. EF Core's command ordering
   *should* emit the UPDATE before the INSERT — but InMemory ignores unique indexes entirely, so if
   that assumption is wrong **every second policy version would fail on real SQL Server** and no
   test would catch it. **The single highest-value thing to run first once a database exists.**
3. **CHECK constraints** (`CK_PaymentCertificates_ApprovePct`, `..._GrossCertifiedAmount`,
   `CK_ProjectFinanceLedgers_Amount_NotZero`) and `FK_ProjectFinanceLedgers_PaymentCertificates` —
   present in the migration SQL, never executed.
4. **Migration application** end to end against a database already carrying Sprints 1-8.
5. **Anything HTTP-transport-level**: TLS/HSTS behind the real proxy, browser cookie/token handling,
   response-compression interaction with `application/problem+json`, real CORS.
6. **Live probing** — no timing analysis, no fuzzing, no concurrent racing against a real database.

Findings H-01, H-02, M-01, M-03 and M-04 are **execution-verified at the handler/persistence layer**
against the real production classes and do not depend on any of the above. M-02 and L-01 through
L-06 are **code-verified only**.

---

## 8. Required before Sprint 9 can close

1. Fix **H-01** — snapshot the resolved chain on Submit (preferred) or re-apply the band filter in
   `PinnedApprovalChainResolver`. Re-verify with the new different-role-per-band fixture.
2. Fix **H-02** — enforce `QuorumCount`, or reject `!= 1` at the write boundary. Add the
   write-API/engine agreement test.
3. Re-run `dotnet build` (0 warnings), `dotnet test`, and the vulnerable-package scan; report real
   numbers.
4. `security-auditor` re-verifies both fixes by execution, the same way the findings were found.

Recommended in the same pass, because Sprint 10 reuses all of it: M-03 (move the band invariant into
the Domain; turn `RequireStep`'s throw into a mapped failure) and M-01 (the
`AppendOnlyGuardInterceptor`). M-02 needs a product decision and should be scheduled, not rushed.
ADR-0008 requires this same review again before Sprint 10 closes.

---

## 9. Re-verification of the H-01 / H-02 fixes (S9-SEC-02)

**Reviewer:** `security-auditor` · **Date:** 2026-08-09 · Step 4 of §8. Same method and the same
limits as §"Method": Docker still cannot start, so there is no SQL Server and no running API.
Probes are throwaway console programs in the session scratchpad referencing the *real*
`CMPlus.Application`/`CMPlus.Infrastructure`/`CMPlus.Domain` assemblies and driving the *real*
handlers, repositories and `CmPlusDbContext` on EF Core InMemory. No repository file was created,
modified or deleted. The new migration `20260809084015_Sprint9_PaymentCertificateApprovalStep`,
its CHECK constraints and its FK cascade remain **unapplied and unverified**.

### 9.1 Verdict

| ID | Verdict | Basis |
| :-- | :-- | :-- |
| **H-01** | **PASS — closed** | execution-verified |
| **H-02** | **PASS — closed** | execution-verified |
| M-03 | **Holds** | execution-verified |
| M-01 | **Partially holds** — append-only enforced; money-freeze **not** | execution-verified |

**Zero new Critical or High.** Five new findings: two Medium, three Low. **Sprint 9 can close.**

Toolchain, re-run at review time: `dotnet build -c Release` → **0 warnings, 0 errors**.
`dotnet test -c Release` → **838/838** (Domain 208 + Application 349 + Architecture 10 +
Integration 271). `dotnet list package --vulnerable --include-transitive` → **no vulnerable
packages** across all 8 projects. `npm audit --omit=dev` in `web/` → **0 vulnerabilities**.

### 9.2 H-01 — PASS

The fix is the preferred option, not the cheaper one: `PinnedApprovalChainResolver.cs` is
**deleted** (confirmed absent), and `IApprovalPolicyReader` is now referenced by
`SubmitPaymentCertificateCommandHandler` only — the three decision handlers no longer touch the
policy store at all, so the re-derivation bug class is structurally gone rather than patched.

Original attack re-run verbatim (three-rule tiered DoA: StepNo 1 = QS for [0,1M), StepNo 1 = PM for
[1M,inf), StepNo 2 = ProjectDirector for [1M,inf); 5,000,000 THB certificate):

```text
submit                  => success=True status=PendingApproval step=1/2
snapshot rows           => rev1/step1=PM(q1), rev1/step2=ProjectDirector(q1)
OUT-OF-BAND QS approve  => success=False error=PaymentCertificateNotAuthorizedForApprovalStep
LEGITIMATE  PM approve  => success=True  step=2/2
OUT-OF-BAND QS at step2 => success=False error=PaymentCertificateNotAuthorizedForApprovalStep
LEGITIMATE  PD approve  => success=True  status=Certified
```

The `ReturnForRevision` variant, proven equally exploitable in §2, is also closed — and so is
`Reject`, which was the same root cause:

```text
OUT-OF-BAND QS return-for-revision => success=False error=...NotAuthorizedForApprovalStep
OUT-OF-BAND QS reject              => success=False error=...NotAuthorizedForApprovalStep
RANDOM Site-role return            => success=False error=...NotAuthorizedForApprovalStep
PENDING-step PD return (legit §4)  => success=True  status=Draft rev=2
```

The `.Include` claim is real and load-bearing: `PaymentCertificateRepository.FindAsync` returns
`ApprovalSteps.Count = 1`; the same query without `.Include` returns `0`. The developer's flag was
correct. Checked deliberately: had the `Include` been forgotten, every path **fails closed** —
Approve/Reject return `CorruptApprovalChain` (409) and `ReturnForRevision` returns 403 — never
open. `FindAsync` is the only production loader on the decision path.

Return-then-resubmit rebuilds correctly. After a PM return, step rows = **0**; re-pricing to
400,000 THB and resubmitting yields `rev2/step1=QS` only, and the old chain is not replayable:

```text
replay: PM (rev-1 step-1 role) approves => success=False error=...NotAuthorizedForApprovalStep
replay: PD (rev-1 step-2 role) approves => success=False error=...NotAuthorizedForApprovalStep
new chain: QS approves                  => success=True status=Certified rev=2
```

Tenant isolation of the new table is intact: `PaymentCertificateApprovalStep` implements
`ITenantOwned`, so ADR-0002's reflection-applied global filter covers it automatically; a second
tenant sees 0 step rows and gets `PaymentCertificateNotFound` on approve.

Regression tests exist and mirror these attacks —
`tests/CMPlus.Integration.Tests/Approval/PaymentCertificateChainSnapshotSecurityTests.cs`, five
facts including `H01_A_High_Value_Certificate_Requires_PM_At_Step_1_Not_The_Low_Band_QS_...` and
`The_Chain_Snapshot_Is_Actually_Deleted_From_The_Database_On_ReturnForRevision_And_Rebuilt_Fresh_On_Resubmit`.
This is the different-role-per-band fixture §2's QA follow-up asked for.

### 9.3 H-02 — PASS

```text
snapshot: step1=QS QuorumCount=2
QS-A approve (1st signature)     => success=True status=PendingApproval step=1/1
  -> persisted status=PendingApproval; Approve rows=1; ledger rows=0
QS-A approve AGAIN (self-quorum) => success=False error=PaymentCertificateDuplicateChainApprover
QS-B approve (2nd distinct sig)  => success=True status=Certified
  -> Approve rows=2 distinct actors=2
```

All three requirements met: no certification on one signature, no ledger posting, and the first
approver's vote **is** preserved as an `ApprovalAction` — evidence is not lost. One user cannot
satisfy a quorum alone. A mixed chain (quorum 2 on step 1, quorum 1 on step 2) advances correctly.

**The `DuplicateChainApprover` interaction reasoning was checked independently and is sound.** It
runs at `ApprovePaymentCertificateCommandHandler.cs:70-75`, before the count at lines 83-88, and
its predicate (`RevisionNo == current && Action == Approve && ActorUserId == actor`, *any* StepNo)
is strictly broader than the count's (`... && StepNo == CurrentStepNo`). So no actor can ever appear
twice in the counted set, and `.Distinct()` is genuinely redundant rather than papering over a gap.
Neither control weakens the other. Quorum state also cannot be laundered by
return-for-revision — the count is keyed on `RevisionNo`, which `ReturnForRevision` bumps.

Snapshotting `QuorumCount` onto the document is an improvement beyond the original ask: editing the
policy after submission can no longer lower an in-flight document's quorum.

### 9.4 M-03 and M-01

**M-03 holds.** `ApprovalPolicy.CreateInitialVersion` now rejects the seeder-path rule set that §3
proved it accepted (`DomainException`), while the legitimate non-overlapping tiered DoA of §9.2 is
still accepted — no false positive. `RequireStep`'s unmapped throw is gone: against a deliberately
holed snapshot, Approve and Reject return `PaymentCertificateCorruptApprovalChain` → **409**, and
`ReturnForRevision` degrades to 403. No 500 on any path.

**M-01 partially holds.** `AppendOnlyGuardInterceptor` is registered in the production composition
root (`Infrastructure/DependencyInjection.cs:34,41`) and works — re-running the original probe:

```text
ApprovalAction UPDATE       => BLOCKED (InvalidOperationException)
ApprovalAction DELETE       => BLOCKED (InvalidOperationException)
ProjectFinanceLedger UPDATE => BLOCKED (InvalidOperationException)
ProjectFinanceLedger DELETE => BLOCKED (InvalidOperationException)
AuditLog DELETE             => BLOCKED (InvalidOperationException)
```

But the money-field half of M-01's fix ("extend to `PaymentCertificate`'s money properties when
`Status >= PendingApproval`") was not built:

```text
certificate status=Certified NetPayment=2000000
Certified NetPayment UPDATE => PERSISTED
```

**M-01 stays open at Medium** for the money-freeze half only. The DB grant
(`DENY UPDATE, DELETE ON dbo.ApprovalActions`) is still runbook-only and unverifiable here.

### 9.5 New findings introduced by the fixes

**N-01 · Medium — the snapshot is now the sole authority record, and it is the one table
deliberately excluded from `IAppendOnly`**
`PaymentCertificateApprovalStepConfiguration.cs` · `Domain/Common/IAppendOnly.cs:17`

Execution-verified: rewriting a rung through an ordinary `CmPlusDbContext` persisted, and a `Site`
user then certified a 9,000,000 THB certificate the tenant's DoA reserved for a ProjectDirector at
`QuorumCount = 3`:

```text
(a) direct UPDATE of RequiredRole/QuorumCount => PERSISTED
    re-read: step1 RequiredRole=Site QuorumCount=1
(a) Site user approves a PD-only step        => success=True status=Certified
```

**Not reachable through any handler** — the constructor is `internal` (0 public constructors),
`PaymentCertificate.Submit` is the only writer, and no handler exposes a mutation path. So this is
the same defense-in-depth class as M-01, not an exploitable escalation, and the exclusion decision
is defensible *in intent*. The problem is its scope: `ReturnForRevision`/`Withdraw` need `Deleted`,
not `Modified` — and **no production path ever modifies a rung** (all setters private, no mutator
methods, Submit adds and `VoidChainSnapshot` clears). **Fix:** have `AppendOnlyGuardInterceptor`
(or a sibling) reject `EntityState.Modified` on `PaymentCertificateApprovalStep` while still
allowing `Added`/`Deleted`. That closes the hole with no cost to the legitimate lifecycle, and
should land with M-01's money-freeze remainder.

**N-02 · Medium — `QuorumCount` is now enforced but still unbounded, and there is no recovery path
from an unsatisfiable one**
`UpdateApprovalPolicyCommandValidator.cs:26`

The validator still only checks `GreaterThanOrEqualTo(1)`. Before the H-02 fix an absurd value was
inert; now it is binding. Worse, `DuplicateChainApprover` caps the achievable quorum at *the number
of distinct users holding that role in the tenant* — so `QuorumCount = 3` on a role held by two
people is already unsatisfiable. The certificate is then stranded in `PendingApproval` forever with
money fields frozen, and there is **no way out**: a grep for `.Withdraw(`/`.Cancel(` returns **zero
production call sites**, so §3 M-03's "no withdraw or cancel endpoint" observation now bites a
second, much more reachable scenario. A rogue or compromised tenant Admin can irrecoverably freeze
a contractor's payment claims. **Fix:** bound `QuorumCount` in the validator (e.g. `<= 5`) with a
specific error code, and surface the `Withdraw`/`Cancel` endpoints the aggregate already implements.

**N-03 · Low — a non-advancing quorum vote writes no UPDATE to the certificate row, so nothing
serialises two concurrent first voters**
`PaymentCertificate.cs:325-331` · `ApprovePaymentCertificateCommandHandler.cs:83-88`

Execution-verified: `RowVersion` is byte-identical before and after a vote that does not clear the
step. The `Approve` early-return leaves the aggregate `Unchanged`, so the `rowversion` token that
protects every other transition does not apply here. The handler's read of the `ApprovalAction`
history and its write are therefore an unprotected read-then-write. **Code-verified consequence
(not reproducible under InMemory, stated as inference):** two simultaneous first votes on a
`QuorumCount = 2` step can both observe zero prior approvers, both compute `quorumSatisfied = false`
and both commit — after which the step holds two distinct approvals but never cleared, and neither
actor may vote again (`DuplicateChainApprover`). Recoverable only by a third role-holder. **Fix:**
have the non-advancing path still touch the aggregate (e.g. a `LastVoteAt` stamp) so `rowversion`
serialises voters, or take a `UPDLOCK` row lock. Worth re-testing once a real database exists.

**N-04 · Low — no UNIQUE index on the snapshot's natural key** (code-verified; migration unapplied)
`PaymentCertificateApprovalStepConfiguration.cs:25`

`IX_..._TenantId_PaymentCertificateId_RevisionNo_StepNo` is non-unique, so the schema permits two
rungs claiming the same `(RevisionNo, StepNo)`. Both `Approve` and `Reject` resolve with
`FirstOrDefault`, which then picks nondeterministically by row order. A probe that injected a
duplicate rung failed to escalate — but only because the genuine rung happened to come first, which
is luck, not a guarantee. Given M-03 now blocks this at policy-construction time, this is
belt-and-braces. **Fix:** make the index `IsUnique()`.

**N-05 · Low — dual-control asymmetry: one person can still terminally reject a `QuorumCount = 2`
step** (extends L-02)

Execution-verified: the same actor approved (1 of 2) and then rejected, taking the certificate to
the terminal `Rejected` state alone. An organisation that configures two-signature control to stop
one person certifying a payment still lets one person unilaterally kill the claim, with no
withdraw/cancel path back. `Reject` also has no `DuplicateChainApprover` equivalent. Defensible
under §6.1's literal text, but it should be a recorded product decision, not an implementation side
effect. **Fix:** `domain-expert` to rule before Sprint 10 applies the same shape to VOs; record the
answer in approval-workflow.md §6.

**Informational (no severity).** (i) `ReturnForRevision` hard-deletes the prior revision's rungs,
contradicting `PaymentCertificateApprovalStep`'s own remark that "more than one revision's steps can
coexist historically"; the chain in force at revision 1 becomes unreconstructible for audit once the
amount is re-priced. Either keep the rows (filtering by `RevisionNo`, which the handlers already do)
or correct the comment. (ii) `PaymentCertificateDto` exposes neither `ApprovalSteps` nor quorum
progress, so a first approver on a quorum step receives `200 success=true` with an unchanged status
— indistinguishable from a no-op. S9-FE-02's chain bar will need this before the quorum feature is
usable; §6's warning about the policy editor exposing `QuorumCount` can now be lifted, subject to
N-02's bound.

### 9.6 Carried forward unchanged

M-01 (money-freeze half only), M-02, M-04 and L-01 through L-06 are untouched by this fix cycle and
remain as written in §3/§4. §7's list of what cannot be verified without a running system still
applies in full, and now additionally covers the new table's CHECK constraints, its FK cascade
(the mechanism `VoidChainSnapshot` relies on — verified only under InMemory's orphan-delete
semantics), and N-03's concurrency behaviour. ADR-0008 requires this review again before Sprint 10
closes; N-01 through N-05 should be scheduled into that pass.

---

## 10. Status of the §9 follow-up findings (implementer-reported — pending `security-auditor` re-verification)

Recorded 2026-08-09 by the orchestrator so §9's findings do not read as open when they are not.
Everything below is **implementer-reported and orchestrator-verified at the build/test level only** —
`security-auditor` has **not** re-probed these the way it probed H-01/H-02, so none of them should be
treated as auditor-closed. Suite at time of writing: build 0 warnings, **894/894**.

| ID | Severity | Status |
| :-- | :-- | :-- |
| N-01 | Medium | **Fixed.** New `INeverModified` marker + interceptor branch blocks `Modified` on `PaymentCertificateApprovalStep` while leaving `Added`/`Deleted` legal, so `ReturnForRevision`'s clear-and-rebuild still works. Both halves tested. |
| N-02 | Medium | **Partially fixed.** `QuorumCount` now bounded 1–5 in `UpdateApprovalPolicyCommandValidator` with a message stating the consequence, backed by 8 tests. **Still open:** nothing prevents a quorum larger than the number of users actually holding that role, and `Withdraw`/`Cancel` remain unexposed by any controller — so a stranded certificate still has no recovery path. |
| N-03 | Low → **was under-rated** | **Fixed.** The review recorded this as "not reproducible under InMemory, stated as inference"; `qa-engineer` disproved that and reproduced it deterministically with two `DbContext`s (no threads needed) — both voters co-committed and the step stuck permanently with both actors locked out by `DuplicateChainApprover`. Fixed with an unconditional `LastVoteAt` stamp so a non-advancing vote still marks the aggregate `Modified` and `rowversion` serialises voters. The pre-existing pin test was flipped to assert correct behaviour and mutation-verified. New migration `20260809121505_Sprint9_PaymentCertificate_LastVoteAt` (**unapplied** — no SQL Server). |
| N-04 | Low | **Open.** Snapshot natural-key index still non-unique. |
| N-05 | Low | **Open.** Needs a `domain-expert` ruling before Sprint 10 applies the same shape to VOs. |
| M-01 | Medium | **Now fully fixed.** §9.4 correctly reported the money-freeze half was never built, and `qa-engineer` re-confirmed live that a `Certified` certificate's `NetPayment` was still rewritable through a raw `DbContext`. The interceptor now rejects modification of the five money fields once `Status` leaves `NotDue`/`Draft`, keyed on per-property `IsModified` so lifecycle transitions stay legal. Mutation-verified, with explicit happy-path coverage. |
| M-02 | Medium | **Open — needs a product decision, not a code fix.** Partially mitigated only: the new list endpoint is project-scoped by route. The id-scoped mutating actions are unchanged, and no project-membership concept exists in the model. |

### Verification-quality note

`qa-engineer`'s independent pass (S9-QA-01/02/03) used canary mutation rather than re-running the
suite, and found two places where green tests protected nothing: deleting
`AdvanceRecoveryCalculator`'s `ThresholdEndPct` branch left all 24 tests passing (P6's parameters
make the branch unobservable), and R9's chain-clearing had no Domain-layer coverage at all. Both are
now closed. This is the second and third time this sprint that a fully green suite sat on top of a
real defect — the first being H-01, which 822 tests passed straight over. Mutation testing, not pass
counts, is what has actually caught things here.

### Still unverifiable without a running database

Unchanged from §7 and §9.6, plus: the two new migrations
(`..._PaymentCertificateApprovalStep`, `..._PaymentCertificate_LastVoteAt`) are unapplied; and EF
Core InMemory supports no transactions at all, so while N-03's core guarantee (the certificate row
cannot silently co-commit) is proven, the "loser's `ApprovalAction` rolls back with its failed
update" property is not — on a relational provider EF wraps each `SaveChanges` in an implicit
transaction, but that is reasoning, not an executed result.
