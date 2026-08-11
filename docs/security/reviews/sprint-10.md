# Sprint 10 Security Review (S10-SEC-01)

**Reviewer:** `security-auditor` · **Date:** 2026-08-10 · **Gate:** `docs/10.` §8 Sprint 10,
security-auditor row — *"ยืนยัน: ผู้ยื่นอนุมัติเองไม่ได้ (`AllowSelfApproval=0`), หนึ่งคนกินสอง step ไม่ได้,
แก้จำนวนเงินแล้ว chain ถูก re-resolve, ไม่มีเส้นทางข้าม escalation; **Critical/High = ปิด sprint ไม่ได้**"*.

Read against ADR-0002 (tenant isolation), ADR-0008 (amount-tiered, version-pinned policy engine),
**ADR-0015** (net-signed escalation over the baseline contract value) and **ADR-0016** (quorum binds
rejection) — both human-confirmed 2026-08-10 — plus `docs/specs/variation-order/domain-rules.md`
§2–§8, `.claude/knowledge/domain/approval-workflow.md` §6 (incl. new item 2a), and
`docs/security/reviews/sprint-09.md` in full including §9 and §10.

---

## Verdict

> **CURRENT STATUS (2026-08-10, after the fix cycle): PASS — Sprint 10 can close.**
> H-01, H-02 and H-03 were all fixed and independently re-verified by execution; DoD item 4 is now
> MET. See **§10**, which is the authoritative current verdict. Everything from here to §9 is the
> *original* review that found them, kept verbatim as the record of what was wrong and why — do not
> read the original verdict line below as the present state.

**Original verdict (first pass): FAIL — 3 High, 0 Critical.** Sprint 10 could not close until H-01,
H-02 and H-03 were fixed and re-verified by execution.

DoD items 1, 2 and 3 were **MET** and execution-verified. DoD item 4 was **NOT MET**: the guard that
was built for it is correct, but two independent paths reached the threshold with no `Executive`
signature, and a third did so as a side effect of a lost update.

None of the three Highs is in the guard's own logic. Two are in code the guard *depends on*
(`ApprovalRoutingService`'s policy selection; `Project`'s baseline columns) and one is in what the
approval transaction *does not* protect (`Project` has no concurrency token). All three are cheap
to fix.

---

## Method, and its limits — read before trusting anything below

Docker Desktop cannot start on this machine (requires Administrator; `docs/perf/gantt-frontend-s6.md`
§3), so **there is no SQL Server and no running API**. Nothing here is a live penetration test.

- **Code-verified** — read the production source directly.
- **Execution-verified** — 22 throwaway probes in the session scratchpad, outside the repository,
  referencing the **real** `CMPlus.Domain`/`CMPlus.Application`/`CMPlus.Infrastructure` assemblies
  and driving the **real** command handlers, repositories, interceptors and `CmPlusDbContext` on EF
  Core InMemory. **No repository file was created, modified or deleted** — confirmed by
  `git status --porcelain` before and after (87 entries, identical).
- Findings were hunted by probing, not by reading the test suite. Where a probe result and a green
  test disagree, the probe is reported.

Toolchain, re-run at review time (not taken on report):

| Command | Result |
| :-- | :-- |
| `dotnet build backend/CMPlus.sln -c Release` | **0 Warning(s), 0 Error(s)** |
| `dotnet test tests/CMPlus.Domain.Tests` | **Passed! 270/270** |
| `dotnet test tests/CMPlus.Application.Tests` | **Passed! 438/438** |
| `dotnet test tests/CMPlus.Architecture.Tests` | **Passed! 10/10** |
| `dotnet test tests/CMPlus.Integration.Tests` | **Passed! 352/352** |
| `dotnet list CMPlus.sln package --vulnerable --include-transitive` | **no vulnerable packages**, all 8 projects |
| `npm audit --omit=dev` (`web/`) | **found 0 vulnerabilities** |

Per-project counts only — no summed total, which can hide a failing project.

§8 lists what could not be verified without a running system.

---

## 1. DoD checklist

| # | DoD item | Verdict | Basis |
| :-- | :-- | :-- | :-- |
| 1 | ผู้ยื่นอนุมัติเองไม่ได้ (`AllowSelfApproval = 0`) | **MET** | execution-verified |
| 2 | หนึ่งคนกินสอง step ไม่ได้ | **MET** | execution-verified |
| 3 | แก้จำนวนเงินแล้ว chain ถูก re-resolve | **MET** | execution-verified |
| 4 | ไม่มีเส้นทางข้าม escalation | **NOT MET** | **H-01, H-02, H-03** |

### 1.1 Self-approval — MET

`VariationOrder.Approve` (`VariationOrder.cs:285-288`) and
`ApproveVariationOrderCommandHandler.cs:129-132` both block creator/submitter unless the **pinned**
`AllowSelfApproval` is set.

```text
chain: [1:PM(q1),2:ProjectDirector(q1)]
submitter(QS) tries approve as PM     : FAIL:VariationOrderSelfApprovalNotPermitted
creator/submitter tries approve as PD : FAIL:VariationOrderNotAuthorizedForApprovalStep
```

The pin is load-bearing and was tested separately: after an Admin creates a **new policy version**
with `AllowSelfApproval = 1` and deactivates the old one, the in-flight document still refuses —

```text
pinned AllowSelfApproval=False
after Admin sets AllowSelfApproval=1 on a NEW version, submitter approves:
    FAIL:VariationOrderSelfApprovalNotPermitted
```

— so a policy edit cannot retroactively unlock self-approval on a document already in flight. This
is strictly better than the Sprint 9 IPC, which does not expose `AllowSelfApproval` on its DTO.

### 1.2 One person cannot satisfy two steps — MET

`DuplicateChainVoter` (`ApproveVariationOrderCommandHandler.cs:136-144`) is keyed on `RevisionNo` +
actor, **any** `StepNo`, `Action ∈ {Approve, Reject}`.

```text
distinct PM approves step 1           : OK
SAME person now approves step 2 as PD : FAIL:VariationOrderDuplicateChainVoter
status=PendingApproval step=2/2
```

The guard's predicate is strictly broader than the per-step quorum count's, so the `.Distinct()` in
the quorum count stays genuinely redundant — the property sprint-09.md §9.3 checked for the IPC
holds here too.

### 1.3 Re-pricing during revision re-resolves the chain — MET (fixture V-10, all six steps)

```text
rev1 @400k  : [1:PM(q1)] total=1 rev=1
PM returns  : OK
after return: status=Draft rev=2 snapshotRows=0 policyPin=null lastVoteAt=null
re-price 600k, resubmit -> rev2 : [1:PM(q1),2:ProjectDirector(q1)] total=2
re-price 3.2M, resubmit -> rev3 : [1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)] total=3
re-price 300k, resubmit -> rev4 : [1:PM(q1)] total=1
replay PD (not in rev4 chain)   : FAIL:VariationOrderNotAuthorizedForApprovalStep
```

The chain grows, grows into escalation, and shrinks; the snapshot is genuinely deleted, the policy
pin and `LastVoteAt` are cleared, and a rev-1 authority replaying against rev-4 gets **403, never a
500**.

`domain-rules.md` §3.4's H-01 inheritance test also passes — an escalated VO against a policy that
owns **no StepNo-3 rule at all**:

```text
submit: OK chain=[1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]
PM : OK   PD : OK   EXE: OK status=Approved
```

The synthesised escalation rung is representable and signable because the chain is snapshotted.
H-01 (Sprint 9) is structurally closed for the VO.

### 1.4 No path bypasses escalation — NOT MET

**The guard itself is correct.** Fixture V-6, driven through the real handlers:

```text
submit A  : OK chain=[1:PM(q1),2:ProjectDirector(q1)]
submit B  : OK chain=[1:PM(q1),2:ProjectDirector(q1)]
A step1 PM: OK
A step2 PD: OK status=Approved
B step1 PM: OK
B step2 PD: FAIL:VoEscalationThresholdCrossedSinceSubmission -> status=PendingApproval step=2/2
```

It **fails closed and appends nothing** — the H-01/N-01 bug class is not repeated. A blocked final
vote leaves the document byte-for-byte unchanged:

```text
result=FAIL:VoEscalationThresholdCrossedSinceSubmission
ApprovalAction rows 2 -> 2 | AuditLog rows 35 -> 35
RowVersion unchanged? True | LastVoteAt unchanged
chain rows unchanged? True | activity budget 50000000 -> 50000000 | Project.BAC unchanged
remedy: return : OK ; resubmit: OK chain=[1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]
```

**`backend-developer`'s R4 claim is confirmed, and their reasoning is right.** R4's VO carries the
`Executive` rung from `Submit`, so the guard's blocking condition
`freshChainRequiresRole && !snapshottedChainAlreadyHasRole` has a term that is false **by
construction**:

```text
submit R4 : OK
chain     : [1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]  totalSteps=3
snapshot already carries Executive? True
  => the guard's '!snapshottedChainAlreadyHasRole' condition is ALWAYS FALSE (no-op)
```

**V-6 is the only fixture that reaches the blocking branch**, and it is the only one that can be.
Both existing V-6 tests run with the pinned policy still active, which is exactly the case H-01
escapes.

Orderings hunted:

| Ordering | Outcome |
| :-- | :-- |
| Interleaved submissions (V-6) | **Blocked** ✓ |
| Return-for-revision that re-prices upward | **Re-resolves, picks up Executive** ✓ (§1.3) |
| Deduct VO that later flips the net back over | **Sound** — Φ is recomputed fresh at every final vote from the live Σ; a document whose *pinned* chain already carries the Executive keeps it even if Σ later falls (conservative direction) |
| Withdraw racing an approval | **Sound** — `WithdrawVariationOrderCommandHandler.cs:32-38` reads the append-only history and refuses once any vote exists (`VariationOrderWithdrawAfterVoteCast`) |
| **Admin edits the approval policy mid-flight** | **BYPASS — H-01** |
| **Project row predating the Sprint 10 migrations** | **BYPASS — H-02** |
| **Concurrent finals** | **BYPASS — H-03** |

---

## 2. Findings — High (block sprint close)

### H-01 · A routine approval-policy edit silently disables the escalation-bypass guard for every in-flight VO

`ApprovalRoutingService.cs:49-55` (with `SelectPolicy`, `:112-131`) · consumed by
`ApproveVariationOrderCommandHandler.cs:284-315`

The guard re-runs the real routing formula against the **single pinned policy**, which is the right
instinct — it is what stops the guard and the routing engine ever disagreeing numerically. But
`Resolve` does not know it has been handed an already-chosen policy. It re-applies `SelectPolicy`'s
live-policy predicate to it:

```csharp
// ApprovalRoutingService.cs:115-119
var effective = candidates.Where(p =>
    p.IsActive
    && p.EffectiveFrom <= submittedAt
    && (p.EffectiveTo is null || p.EffectiveTo >= submittedAt))
```

and when that yields nothing it **succeeds with the hard-coded fallback chain** rather than failing:

```csharp
// ApprovalRoutingService.cs:51-55
if (policy is null)
{
    return Result<ApprovalChainResolution>.Success(new ApprovalChainResolution(
        routingAmount, Guid.Empty, 0, FallbackChain, EscalationApplied: false, AllowSelfApproval: false));
}
```

`FallbackChain` is a single `ProjectDirector` step. It contains no `Executive`, so
`freshChainRequiresRole` (handler `:302`) is `false` and the guard passes.

`IApprovalPolicyReader.GetByIdAsync` deliberately ignores `IsActive` so a pinned version stays
readable (correct, verified sound in sprint-09.md §5). `UpdateApprovalPolicyCommandHandler:45` calls
`current?.Deactivate(now)` on every edit. The two combine into the defect.

**Root cause, isolated (probe 22):**

```text
IsActive=True  -> Success chain=[1:PM,2:ProjectDirector,3:Executive] escalationApplied=True  policyId=pinned
IsActive=False -> Success chain=[1:ProjectDirector]                  escalationApplied=False policyId=Guid.Empty(FALLBACK)
baseline=0 on a superseded pin -> Success (fail-closed 422 SKIPPED)
```

The last line matters too: the `ContractValueNotConfigured` fail-closed that §4.6 required
(`ApprovalRoutingService.cs:82-85`) is skipped by the same path.

**Attack scenario (execution-verified, probe 3).** Contract `C^orig = 485,000,000`, θ = 10.00%,
Σ = 44,000,000. VO-A `+2,400,000` and VO-B `+2,300,000` both submit under the line, chain
`[PM, PD]`. VO-A is approved (Σ → 46,400,000). VO-B's PM approves. A tenant Admin then does anything
at all to the VO approval policy through the shipped
`PUT /api/v1/tenants/{tenantId}/approval-policies/{documentType}` — adjust a band, change a quorum,
correct a typo in the threshold. That versions the policy and deactivates the pinned row.

```text
A approved, sigma now 46,400,000
Admin edits the VO policy -> pinned version deactivated
B step2 PD: OK -> status=Approved
chain that approved it: [1:PM(q1),2:ProjectDirector(q1)]
BAC=489700000 CV=489700000  ratio=10.0412%
VERDICT: *** BYPASS - no Executive signature anywhere ***
```

This is the exact §4.7 scenario the guard exists to prevent, re-opened by an ordinary administrative
act. It needs no malice; and a *deliberate* actor gets a bypass that is invisible in the audit trail,
because the VO still records `CumulativeVoPctAtApproval = 10.0412%` against a chain that has no
Executive in it — the evidence contradicts itself and nothing raises it.

**Fix.** Do not route through `SelectPolicy` when the policy is already chosen. Either:

1. add an explicit "policy already pinned" entry point (e.g. `Resolve(policy, request)`) that skips
   selection entirely and reuses the band/escalation logic verbatim; **or**
2. keep the call but make an unusable candidate set a **failure**, never the fallback: `Resolve`
   should return `Failure(ApprovalErrorCodes.PolicyGap)` when `CandidatePolicies` is non-empty and
   selection still yields nothing. Silently substituting the fallback for "you gave me one policy
   and I rejected it" is not the same case as §5.3 step 6's "this tenant has no policy at all".

Either way the guard must **fail closed** — an unresolvable pinned policy should block the final
approval (409 `CorruptApprovalChain`), the branch the handler already has at `:270`.

**QA follow-up.** Both existing V-6 tests keep the policy active. Add a V-6 variant that versions the
policy between VO-A's approval and VO-B's final vote, and assert the 409 still fires.

### H-02 · `Project.OriginalContractValue` / `Project.OriginalBac` are added with **no backfill**, so on any pre-existing project an ordinary edit moves the escalation trigger and historical EVM is rewritten

`20260810002248_Sprint10_Project_OriginalContractValue.cs:13-19` ·
`20260810045006_Sprint10_Project_OriginalBac.cs:13-19` · `Project.cs:49,122` ·
`EvmDataReader.cs:51-63`

Both migrations are `AddColumn<decimal>(… nullable: true)` with a CHECK constraint and **no
`UPDATE`**. Every project row that exists when they are applied reads `NULL`. The two accessors then
fall back to the live figures:

```csharp
public decimal EffectiveOriginalBac            => OriginalBac ?? BAC;                     // Project.cs:49
public decimal EscalationBaselineContractValue => OriginalContractValue ?? ContractValue; // Project.cs:122
```

The doc comments justify the fallback as "correct for a project that predates this field **and has
never had an approved VO move `ContractValue` away from its original figure**". That precondition
holds only until the first VO is approved — and nothing enforces or detects it.

Three consequences, all execution-verified against a row with the columns cleared:

**(a) The self-diluting denominator ADR-0015 exists to remove is silently reinstated.**
`EscalationBaselineContractValue` becomes `ContractValue`, which `ApplyVariationOrderApproval`
(`Project.cs:293`) increases on every approval — exactly D-2, which ADR-0015 calls "a bug to fix,
not a behaviour to preserve".

**(b) An ordinary project edit moves the escalation trigger — a second bypass.**
`UpdateProjectCommandHandler.cs:30-31` calls `SetBac`/`SetContractValue`; the route is
`PUT /api/v1/projects/{projectId}`, `[Authorize(Roles = "PM,QS,Executive,Admin")]` — **not**
Admin-only.

```text
(a) new project: OCV=485000000 baseline=485000000
    after PUT bac/cv=900,000,000 -> OCV=485000000 baseline=485000000
    R4-shaped VO chain = [1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]   <- trigger NOT moved (correct)

(b) legacy row : OCV=NULL baseline=485000000
    R4-shaped VO chain before edit = [1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]
    after PUT cv=600,000,000 -> baseline=600000000
    R4-shaped VO chain after edit  = [1:PM(q1),2:ProjectDirector(q1)]   ratio 8.200%
```

A QS raises `ContractValue` on the Project Info screen and the `Executive` step disappears from every
subsequent VO. For a **post-migration** project the design is correct and the trigger cannot be
moved — that half passes. The gap is entirely the missing backfill.

**(c) `GetBacAsOfAsync` double-counts every approved VO, rewriting historical EVM upward.**
`EvmDataReader.cs:63` returns `(OriginalBac ?? BAC) + Σ(approved VO with ApprovedAt <= asOf)`. With
`OriginalBac` NULL, `BAC` **already contains** those VOs:

```text
legacy=False OriginalBac=100000000 Project.BAC=110000000
    | BAC(before tA)=100000000 (truth 100,000,000)  BAC(after tA)=110000000 (truth 110,000,000)   OK
legacy=True  OriginalBac=NULL     Project.BAC=110000000
    | BAC(before tA)=110000000 (truth 100,000,000)  BAC(after tA)=120000000 (truth 110,000,000)   WRONG
```

Sprint 10 switched `EvmComputationService.cs:52` from `settings.Bac` to `GetBacAsOfAsync`, so this
reaches every EAC/VAC/TCPI/%-complete figure on the dashboard and every historical read. Frozen
`EvmPeriodSnapshot` rows are still immutable (S10-QA-02's guarantee holds), but a live recomputation
of an *open* historical data date now disagrees with its snapshot by exactly ΣVO — the precise
divergence §5.5(c) was written to eliminate, in the opposite direction.

**Fix.**
1. Add a data migration (or amend both, since neither has been applied):
   `UPDATE Projects SET OriginalContractValue = ContractValue WHERE OriginalContractValue IS NULL;`
   and `UPDATE Projects SET OriginalBac = BAC WHERE OriginalBac IS NULL;` — run **before** any VO can
   exist, which is true today.
2. Then make both columns `NOT NULL` and delete the `??` fallbacks. A nullable baseline that silently
   degrades to the live figure is worse than a NOT NULL column, because it turns a provisioning gap
   into a *silently weaker control* instead of a visible error (the M-04 failure mode again).
3. If a nullable column must be kept, treat `NULL` as **fail-closed** for escalation
   (422 `ContractValueNotConfigured`), never as "use the current value".
4. Add the §5.5(c) invariant test `Project.BAC == BAC(now)` and an equivalent for the escalation
   baseline, so this cannot silently reopen.

### H-03 · `Project` carries no optimistic-concurrency token, so two concurrent VO final approvals lose one money move and each guard sees a stale Σ

`ProjectConfiguration.cs` (no `IsRowVersion()`) · `Project.cs:290-299` ·
`ApproveVariationOrderCommandHandler.cs:158,211,236`

`VariationOrder` has a `rowversion` and `TrySaveChangesAsync` correctly translates a conflict to 409.
`Project` has neither a `RowVersion` property nor a concurrency configuration. Two VO approvals on
the **same project** are two different VO rows, so nothing they share is protected.

**Execution-verified (probe 8)** — two independent `CmPlusDbContext`s, the shape two simultaneous
HTTP requests have:

```text
second write: PERSISTED (no concurrency token)
final BAC=487300000 (both moves = 489,700,000; one lost = 487,300,000)
```

VO-A's `+2,400,000` is silently discarded. Both VOs are `Approved`, both carry immutable
`BacBefore`/`BacAfter` stamps, and `Project.BAC` agrees with neither. `domain-rules.md` §5.1 states
the opposite guarantee: *"An `Approved` VO whose BAC move did not land must be impossible."*
Atomicity **within** one `SaveChanges` is correct; isolation **across** two approvals is absent.

**The escalation consequence (code-verified inference, not reproducible on InMemory).** Under SQL
Server's default READ COMMITTED, T2's `GetApprovedNetSignedTotalAsync` will not see T1's uncommitted
VO. Both final approvals therefore evaluate Φ against the pre-race Σ, both pass the guard, and both
commit — V-6 with the two approvals compressed into the same window. The parallel probe serialised
under InMemory and correctly returned `FAIL:VoEscalationThresholdCrossedSinceSubmission`, so this
specific race is **not** execution-verified; the lost-update mechanism that enables it **is**.

The same exposure applies to `Activity.BudgetCost`, which `AdjustBudgetCost` moves by a delta on an
unprotected row.

**Fix.** Add `RowVersion` to `Project` (and configure `IsRowVersion()`), exactly as
`PaymentCertificate` and `VariationOrder` already do. `TrySaveChangesAsync` then turns the loser into
a 409 and the approver retries — at which point the guard re-reads Σ and blocks correctly.
Re-verify against real SQL Server once a database exists (§8).

---

## 3. Findings — Medium

**M-01 · The VO scope payload has no persistence-layer freeze; a rewritten `BudgetCostDelta`
desynchronises `Activity.BudgetCost` from `Project.BAC` permanently**
`VariationOrderScopeItem.cs:26` · `AppendOnlyGuardInterceptor.cs:59-65,151-170`

`domain-rules.md` §2.4 requires the freeze on **money *and scope*** at the persistence layer.
`VariationOrderFrozenContentProperties` covers `Amount`/`Description`/`Justification`/
`TimeImpactDays` — scalars on the parent row. The scope payload lives in a separate table, and
`VariationOrderScopeItem` implements neither `IAppendOnly` nor `INeverModified`.

Execution-verified, through an ordinary `CmPlusDbContext` on a `PendingApproval` VO:

```text
raw Amount rewrite on PendingApproval : BLOCKED (InvalidOperationException)
raw ScopeItem.BudgetCostDelta rewrite : *** PERSISTED ***
re-read BudgetCostDelta = 44000000 (VO.Amount is still 800000)
raw chain-rung rewrite (N-01 class)   : BLOCKED (InvalidOperationException)
PM : OK   PD : OK
VO.Amount=800000  Project.BAC moved to 485800000  activity budget 94000000
```

The approval then moves `Project.BAC` by 800,000 while moving the activity budget by 44,000,000.
That is §5.2's named nightmare — `EV` can never reach `BAC`, and `TCPI`/`VAC`/% complete are wrong
forever with no test on `Project.BAC` noticing. **Not reachable through any handler**
(`SetVariationContent` is the sole writer and refuses outside `Draft`), so this is the M-01/N-01
defense-in-depth class, not an exploitable escalation — but its blast radius is larger than M-01's.

**Fix.** Mark `VariationOrderScopeItem : INeverModified`. `SetVariationContent` clears and re-adds
the whole collection, so `Added`/`Deleted` stay legal and the lifecycle is unaffected — the identical
shape already applied to `VariationOrderApprovalStep`.

**M-02 · An ordinary BAC edit is silently ignored by every EVM read — `Project.BAC == BAC(now)` no
longer holds**
`UpdateProjectCommandHandler.cs:30` · `Project.cs:233` · `EvmDataReader.cs:35-64` ·
`EvmComputationService.cs:52`

Distinct from H-02: this happens on **brand-new, correctly-backfilled** projects. `SetBac` moves
`BAC` but not `OriginalBac`, and Sprint 10 repointed EVM at `GetBacAsOfAsync = OriginalBac + ΣVO`.
Execution-verified:

```text
after PUT bac=150,000,000: Project.BAC=150000000 OriginalBac=100000000 BAC(now)=100000000
    invariant holds? False
```

A QS edits the budget on the Project Info screen (a shipped S4-BE-02 feature), the value persists,
the screen shows it — and the dashboard, EAC, VAC, TCPI and % complete all keep using the old figure
with no warning. This is a behavioural regression Sprint 10 introduced into previously correct
shipped code. Needs a product decision (`domain-rules.md` never says what an ordinary BAC edit means
once `BAC(t)` exists): either move `OriginalBac` on a deliberate rebaseline and record it, or block
`SetBac` once an approved VO exists. Whichever is chosen, add the §5.5(c) invariant test.

**M-03 · The human-confirmed 10.00% threshold is not seeded, so cumulative-VO escalation is inert in
every tenant**
`ApprovalPolicySeeder.cs:47-55`

The seeder still writes `CumulativeVoEscalationPct = NULL`, correctly implementing
`domain-rules.md` §4.5 — which was written *before* the human answered. **ADR-0015 (Accepted,
human-confirmed 2026-08-10) now states the answer: "Threshold default 10.00%".** The two have drifted.

Execution-verified against the **real production seeder**:

```text
seeded TH-Default-VO: CumulativeVoEscalationPct=NULL Role=Executive
chain with sigma=200,000,000 (41.9% of contract): [1:PM(q1),2:ProjectDirector(q1)]
PM : OK   PD : OK status=Approved
```

A project 41.9% varied approves another VO with no Executive anywhere. The behaviour is *correct for
a NULL threshold* — the problem is that the sprint's headline control ships switched off, with no
log, metric or alarm distinguishing "the tenant deliberately disabled it" from "provisioning never
turned it on". That is precisely sprint-09.md M-04's finding, one level up.

**Fix.** Seed `10.00` with `Role = Executive` per ADR-0015, and keep `NULL` reachable only as a
deliberate Admin choice. If the team prefers to keep seeding `NULL`, log + emit a metric whenever a
VO submits against a policy with no threshold, so "not configured" is observable.

**M-04 · No project-scoped authorization — carried from sprint-09.md M-02, now more serious**
`VariationOrderRepository.cs:24-28` · `VariationOrdersController.cs` (flat `{id}` routes)

Any tenant user holding the current step's role can approve **any** project's VO, and a VO approval
directly moves `Project.BAC` and `Project.ContractValue`. No `ProjectMember`/`UserProject` concept
exists anywhere in `backend/src`. `domain-rules.md` §7.5 and §9 instruct that this be *recorded here
as an explicitly accepted, documented limitation rather than silently assumed closed* — **so
recorded.** Nothing crosses a tenant boundary, hence Medium. Needs a product decision.

---

## 4. Findings — Low (tracked, non-blocking)

| ID | Finding | Fix |
| :-- | :-- | :-- |
| L-01 | `var actorUserId = currentUser.UserId ?? Guid.Empty` remains in **Reject** (`:43`), **ReturnForRevision** (`:32`), **Withdraw** (`:40`) and **Cancel** (`:25`). `Create` and `Approve` correctly fail closed with `ActorRequired`. `domain-rules.md` §7.5 says *"**Fail closed** on a null user id in **every** VO handler."* Unreachable behind `[Authorize]`, but a `Guid.Empty` actor on an append-only evidence row is evidentially worthless. | Use the same `is not { } actorUserId` guard in the other four. |
| L-02 | The 409 ProblemDetails for `VoEscalationThresholdCrossedSinceSubmission` (`ResultProblemMapper.cs:144`) is a fixed string. §4.7 requires it to name **Φ, θ and the missing role**. The approver is told to return for revision but not why. | Return the three values as ProblemDetails extensions (tenant's own data — no cross-tenant disclosure). |
| L-03 | `GET /api/v1/variation-orders/{id}/approval-actions` 404s with the error code `PaymentCertificateNotFound`. Deliberate reuse and **not a leak** — it maps to the generic `not-found` ProblemDetails, identical for a cross-tenant id and an unknown same-tenant id (both verified) — but a VO-typed code would be clearer. | Cosmetic; introduce a shared `ApprovalDocumentNotFound` code. |
| L-04 | `VoReadRoles` closes the read/write gap on the **id-scoped** routes, but `ProjectVariationOrdersController` is `[Authorize(Roles = VoCrudRoles)]` at class level (`:19`), so an `Executive` gets 403 on `GET /api/v1/projects/{projectId}/variation-orders` — the VO register. They can open a VO they already have the id for, but cannot find it. Same gap `VoReadRoles` was created to fix, half-closed. | Split the list `GET` onto `VoReadRoles`, keep `POST` on `VoCrudRoles`. |
| L-05 | Still no `FallbackPolicy` on `AddAuthorization()` (`Program.cs:148`) — Sprint 2 L-03 / Sprint 9 L-05, now third sprint open. A controller added later without `[Authorize]` defaults to anonymous, and money-moving VO endpoints now live here. | `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();` plus explicit `[AllowAnonymous]` on `/auth/login` and health. |
| L-06 | Carried unchanged: Sprint 2 M-03 (no rate limiting), Sprint 2 M-04 (no CSP), Sprint 3 L-06 (**EPPlus still Polyform Noncommercial — blocking for production**, restated in ADR-0014). | Sprint 15 owns M-03/M-04; EPPlus needs a commercial key before the S16 gate. |

---

## 5. ADR-0016 re-verification — PASS, and the deadlock escape works

ADR-0016 required `security-auditor` to re-verify by execution, inverting sprint-09.md §9.5's probe.
Done, on **both** document types.

**Variation Order** (`TH-DualControl-VO`, one step, `ProjectDirector`, `QuorumCount = 2`):

```text
chain=[1:ProjectDirector(q2)]
V-11a PD-A approve      : OK   status=PendingApproval
V-11a PD-A then reject  : FAIL:VariationOrderDuplicateChainVoter   status=PendingApproval
V-11c PD-B reject       : OK   status=PendingApproval            <- neither quorum met
PD-A votes again        : FAIL:VariationOrderDuplicateChainVoter
PD-B votes again        : FAIL:VariationOrderDuplicateChainVoter
V-11d PD-C return       : OK   status=Draft rev=2
```

**Payment Certificate — the shipped, PASS-verified Sprint 9 code the retrofit changed:**

```text
V-11b PD-A reject (1 of 2)   : OK  status=PendingApproval
     LastVoteAt stamped? True   RowVersion changed? True
N-05: PD-A approve after rejecting : FAIL:PaymentCertificateDuplicateChainVoter
V-11b PD-B reject (2 of 2)   : OK  status=Rejected
```

All three ADR-0016 behaviour changes are present and correct.

**No deadlock.** The intended escape works, and better than the ADR claims. I specifically tested
the harder case the ADR does not: a `q = 2` step where **only two people hold the role and both have
already voted**, so there is no "third role-holder" to send it back.

```text
-- VO, escape 2: only TWO people hold the role and both already voted
split reached; status=PendingApproval
PD-A (already approved) returns for revision : OK status=Draft rev=2

-- IPC, same shape
PD-A returns for revision (single-actor escape) : OK status=Draft rev=2
```

`ReturnForRevision` carries **no** duplicate-voter guard on either document type, so an actor who has
already voted can still return the document. That is what makes ADR-0016 genuinely deadlock-free
rather than deadlock-free-if-a-third-person-exists. It is load-bearing and undocumented —
**`ReturnForRevision` must never gain a `DuplicateChainVoter` guard**; add a regression test naming
this so a future "consistency" cleanup cannot remove the escape.

- `Withdraw` correctly does **not** serve as an escape:
  `submitter withdraw after votes : FAIL:VariationOrderWithdrawAfterVoteCast`. Correct per §2.3.
- **`QuorumCount = 1` is unchanged** — `single PD reject : OK status=Rejected`. Zero regression for
  the overwhelmingly common configuration, asserted by execution rather than assumed.
- **N-02's remaining half is closed for the VO, still open for the IPC.** The VO exposes `withdraw`
  and `cancel`; the IPC still exposes neither, so a stranded certificate's only recovery is
  `ReturnForRevision`. That is now a genuine escape (verified above), which downgrades N-02's
  urgency but does not close it.

---

## 6. Areas explicitly checked and found sound

- **Tenant isolation across all three new VO tables (ADR-0002) — clean.** All implement
  `ITenantOwned`, so `CmPlusDbContext.ApplyTenantQueryFilters` reaches them by reflection with no
  per-entity wiring to forget. Execution-verified with two tenants sharing one store:

  ```text
  tenant B sees VariationOrders=0  ApprovalSteps=0  ScopeItems=0  ApprovalActions=0
  tenant B approve/submit/return/withdraw/reject/update-content : all FAIL:VariationOrderNotFound
  tenant B GET approval-actions : FAIL (bare 404)
  tenant A GET unknown id       : FAIL (identical bare 404)
  tenant B list A's project VOs : rows=0
  ```

  No repository adds an explicit `TenantId` predicate — correct; the filter is ambient and cannot be
  forgotten. `GetApprovedNetSignedTotalAsync` and `FindActivitiesForApprovalAsync` are both
  project-scoped **and** tenant-filtered.

- **The `VariationOrder` arm of `GetApprovalActionHistoryQueryHandler` — correct.** Existence is
  checked through the tenant-filtered repository before any history is returned, so a cross-tenant id
  and an unknown id produce the identical bare 404. No 200-with-empty-array oracle.

- **`VoReadRoles` — the read/write split is real and did not widen anything else.** Applied to
  exactly two endpoints; every statically-gated write endpoint keeps `VoCrudRoles`; repo-wide grep
  confirms the constant appears nowhere else. `approve`/`reject`/`return-for-revision` deliberately
  carry **no** static role gate — the only shape that lets an escalated `Executive` sign. Only L-04
  is left over.

- **`Project.OriginalContractValue` cannot be moved by an ordinary edit — on post-migration rows.**
  No mutator exists for either baseline field; `ApplyVariationOrderApproval` leaves both untouched.
  The **only** way to move the trigger is the missing-backfill path — H-02.

- **`abs()` is a routing input only; a Deduct VO is never flipped to an Add.** Fixture R2's
  assertion (iii), the regression a green HTTP suite once sat on:

  ```text
  chain=[1:PM(q1),2:ProjectDirector(q1)] Amount=-800000 Type=Deduct
  PM : OK   PD : OK
  status=Approved Amount=-800000 BAC=99200000 CV=99200000
  activity budget after = 49200000
  ```

  Signed all the way through. `Type` is a computed property with no backing column
  (`VariationOrder.cs:72`), so sign and type cannot desynchronise even at the persistence layer.

- **A VO approval writes zero `ProjectFinanceLedger` rows** (§6.4, unconditional) and does write the
  §6.2 disclosure: `ProjectFinanceLedger rows=0  disclosure audit rows=1`, with the in-flight
  certificate untouched.

- **The chain snapshot is protected.** `VariationOrderApprovalStep : INeverModified` — a raw rung
  rewrite is blocked while `Added`/`Deleted` stay legal. Its natural-key index **is** `IsUnique()`,
  so N-04 is not repeated on the VO.

- **`ApprovalRoutingService` band logic, reused not rewritten** (S10-BE-02's DoD). `Math.Abs` for
  `VariationOrder` only; full-`decimal` comparison with strict `>`; escalation appends only to a
  non-empty chain; empty chain → 422 `ApprovalPolicyGap`, document stays `Draft`; the §4.6
  `ContractValueNotConfigured` fail-closed replaced the old silent skip. All correct — except when
  reached through the H-01 path.

- **Injection / error leakage / mass assignment.** No raw SQL on the VO path. Request DTOs expose no
  `TenantId`, `ProjectId`, `Status`, `RevisionNo`, `RowVersion`, `ApprovalPolicyId` or chain field.
  All 21 VO error codes map to generic-titled ProblemDetails; 404s are indistinguishable across
  "wrong tenant" and "does not exist".

- **Frontend.** `web/src/features/vo/` gates buttons only and says so explicitly
  (`chainPermissions.ts:23-33`); the server is the authorization boundary. No
  `dangerouslySetInnerHTML`, no token in `localStorage`. No VO export path exists this sprint, so
  CSV/Excel formula injection is not yet in scope.

- **Dependencies.** Zero vulnerable NuGet packages across all 8 projects; `npm audit --omit=dev` → 0.

---

## 7. Status of inherited Sprint 9 findings

Per sprint-09.md §10; not re-reported as new.

| ID | Sprint 9 status | Sprint 10 status |
| :-- | :-- | :-- |
| H-01, H-02 | Closed | **Still closed** — re-verified for the VO (§1.2, §1.3) |
| M-01 | Fully fixed | **Holds** for `PaymentCertificate`; correctly extended to `VariationOrder`'s scalars — **but not to the VO scope payload** (new M-01 above) |
| M-02 | Open, needs a product decision | **Open** — recorded as M-04 above per `domain-rules.md` §9 |
| M-03, M-04 | Fixed / open | M-03 holds; M-04's family recurs as M-03 above |
| N-01 | Fixed | **Holds**, and the pattern was correctly applied to the VO chain snapshot |
| N-02 | Partially fixed | **Half closed** — VO exposes `withdraw`/`cancel`; IPC still exposes neither. Quorum bounded 1–5 |
| N-03 | Fixed | **Holds**, and extended to rejections per ADR-0016 (verified §5) |
| N-04 | Open (IPC) | **Open for the IPC**; **not repeated** on the VO (unique index from day one) |
| N-05 | Ruled 2026-08-10, fix scheduled S10 | **CLOSED** — implemented and execution-verified on both document types (§5) |
| L-01 | Open | **Partially closed** — Create/Approve fail closed; four VO handlers still fabricate `Guid.Empty` |
| L-03…L-06 | Open | Unchanged; L-05 is now third sprint open |

---

## 8. What could not be verified without a running system

1. **Real `rowversion` concurrency and the 409.** Every concurrency result runs on EF Core InMemory
   with `RowVersionSaveChangesInterceptor` simulating SQL Server's token. **H-03's escalation
   consequence specifically needs a real database** — the lost-update mechanism is
   execution-verified, the two-transactions-under-READ-COMMITTED race is inference.
2. **Five unapplied migrations**: `…_PaymentCertificate_LastVoteAt`,
   `…_Sprint10_Project_OriginalContractValue`, `…_Sprint10_VariationOrder`,
   `…_Sprint10_VariationOrder_ApprovedIndex_And_IpcStepUnique`, `…_Sprint10_Project_OriginalBac`.
   None has been executed. **H-02's backfill fix must be written and applied together with them.**
3. **The new UNIQUE index** on `(TenantId, VariationOrderId, RevisionNo, StepNo)` versus
   `ReturnForRevision`'s delete-then-re-insert within one `SaveChanges`. InMemory ignores unique
   indexes entirely; if EF's command ordering emits the INSERT before the DELETE, **every
   resubmission would fail on real SQL Server and no test would catch it.** This is the single
   highest-value thing to run first once a database exists — the same trap sprint-09.md §7 item 2
   flagged, now with a second instance.
4. **CHECK constraints** on the three new tables and the two new `Projects` columns, and the FK
   cascade `VariationOrder → VariationOrderApprovalStep` that `VoidChainSnapshot` relies on.
5. **The `Σ^VO` covering index** `IX_VariationOrders_TenantId_ProjectId_Approved` — never planned
   against real data. `GetBacAsOfAsync` additionally filters `ApprovedAt` outside that index.
6. **Anything HTTP-transport-level**: TLS/HSTS behind the real proxy, cookie handling, CORS,
   response compression versus `application/problem+json`.
7. **Live probing** — no timing analysis, no fuzzing, no concurrent racing against a real database.

Findings **H-01, M-01, M-02, M-03** and the H-02 consequences are execution-verified at the
handler/persistence layer against the real production classes. **H-03**'s lost update is
execution-verified; its escalation consequence is code-verified. **H-02**'s behaviour is
execution-verified against a simulated pre-migration row; the *existence* of such rows follows from
the migrations having no backfill. **M-04** and **L-01…L-06** are code-verified only.

---

## 9. Required before Sprint 10 can close

1. Fix **H-01** — stop routing a pinned policy through `SelectPolicy`, or make an unresolvable
   candidate set a failure instead of the fallback chain. The guard must fail closed.
2. Fix **H-02** — backfill both `Projects` baseline columns in the (still unapplied) migrations, then
   make them `NOT NULL` and remove the `??` fallbacks; add the `Project.BAC == BAC(now)` invariant
   test.
3. Fix **H-03** — add a `rowversion` concurrency token to `Project`.
4. Re-run `dotnet build` (0 warnings) and `dotnet test` **per project**, and the vulnerable-package
   scans; report real numbers.
5. `security-auditor` re-verifies all three by execution, plus the new V-6-with-a-versioned-policy
   fixture and the `ReturnForRevision`-stays-single-actor regression test.

Recommended in the same pass, because they are one-line changes in code already being touched:
**M-01** (`VariationOrderScopeItem : INeverModified`), **M-03** (seed the ADR-0015 threshold),
**L-01** (fail closed on a null actor in the remaining four VO handlers). **M-02** and **M-04** need
product decisions and should be scheduled, not rushed.

ADR-0008 requires this review again before Sprint 11 closes if the VO approval surface changes.

---

## 10. Re-verification (S10-SEC-01-R1)

**Reviewer:** `security-auditor` · **Date:** 2026-08-10 · Re-verifies §9 items 1-5 by execution.

> **Verdict: PASS on H-01, H-02 and H-03. 0 Critical, 0 High. DoD item 4 is now MET.
> Sprint 10 can close.** Three new **Low** findings are tracked, none blocking.

### 10.1 Toolchain, re-run at re-verification time

| Command | Result |
| :-- | :-- |
| `dotnet build backend/CMPlus.sln -c Release` | **0 Warning(s), 0 Error(s)** |
| `dotnet test tests/CMPlus.Domain.Tests` | **Passed! 271/271** |
| `dotnet test tests/CMPlus.Application.Tests` | **Passed! 445/445** |
| `dotnet test tests/CMPlus.Architecture.Tests` | **Passed! 10/10** |
| `dotnet test tests/CMPlus.Integration.Tests` | **Passed! 358/358** |
| `dotnet list CMPlus.sln package --vulnerable --include-transitive` | **no vulnerable packages**, 8/8 projects |
| `npm audit --omit=dev` (`web/`) | **found 0 vulnerabilities** |

Method unchanged: throwaway probes in the session scratchpad, outside the repository, driving the
**real** handlers/repositories/interceptors/`CmPlusDbContext` on EF Core InMemory. **No repository
file was created, modified or deleted** — `git status --porcelain` = 100 entries before and after.
Findings were re-hunted by probing, not by reading the fix or the tests.

One new capability this round: `dotnet ef migrations script` runs **offline**, so the six unapplied
migrations' **actual generated T-SQL** is now execution-verified even without a database.

### 10.2 H-01 — PASS (execution-verified)

Option 1 implemented as `IApprovalRoutingService.ResolvePinned`
(`ApprovalRoutingService.cs:63-68`, shared `ResolveForPolicy` at `:80-136`), called from
`ApproveVariationOrderCommandHandler.cs:297`.

**Probe 3 re-run verbatim** — `C^orig` 485,000,000, θ 10.00%, Σ 44,000,000, VO-A +2,400,000,
VO-B +2,300,000, Admin versions the policy between A's approval and B's final vote:

```text
submit A  : OK chain=[1:PM(q1),2:ProjectDirector(q1)]
submit B  : OK chain=[1:PM(q1),2:ProjectDirector(q1)]
A step1 PM: OK   A step2 PD: OK status=Approved
A approved, sigma now 46,400,000
B step1 PM: OK
Admin edits the VO policy -> pinned version deactivated
B step2 PD: FAIL:VoEscalationThresholdCrossedSinceSubmission -> status=PendingApproval step=2/2
BAC=487,400,000 CV=487,400,000 OCV=485,000,000   ratio if B landed = 10.0412%
ApprovalAction rows 2 -> 2
VERDICT H-01: *** BLOCKED 409 - bypass CLOSED ***
```

Repeated with the **realistic** administrative act (`Deactivate` **plus** `CreateNextVersion`, which
is what `UpdateApprovalPolicyCommandHandler` actually does) — the 409 still fires, and the remedy the
error tells the approver to take now works end to end:

```text
Admin edit: v1 deactivated, v2 active
B step2 PD: FAIL:VoEscalationThresholdCrossedSinceSubmission
return    : OK
resubmit  : OK chain=[1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]
```

**Root cause inverted (probe 22 re-run, isolated):**

```text
Resolve       IsActive=True  -> Success chain=[1:PM,2:ProjectDirector,3:Executive] policyId=pinned
ResolvePinned IsActive=True  -> Success chain=[1:PM,2:ProjectDirector,3:Executive] policyId=pinned
Resolve       IsActive=False -> Success chain=[1:ProjectDirector]  policyId=Guid.Empty(FALLBACK)
ResolvePinned IsActive=False -> Success chain=[1:PM,2:ProjectDirector,3:Executive] policyId=pinned
ResolvePinned baseline=0     -> FAIL:ContractValueNotConfigured
ResolvePinned baseline=null  -> FAIL:ContractValueNotConfigured
```

Both claims confirmed: no selection step can reject an already-chosen policy, and the §4.6
`ContractValueNotConfigured` fail-closed is reachable on the re-check path. `Resolve`'s own fallback
behaviour is unchanged — see **N-06** below.

### 10.3 H-02 — PASS

Generated T-SQL for `20260810002248_Sprint10_Project_OriginalContractValue` and
`20260810045006_Sprint10_Project_OriginalBac`:

```sql
ALTER TABLE [Projects] ADD [OriginalContractValue] decimal(18,2) NULL;
UPDATE [Projects] SET [OriginalContractValue] = [ContractValue] WHERE [OriginalContractValue] IS NULL;
ALTER TABLE [Projects] ALTER COLUMN [OriginalContractValue] decimal(18,2) NOT NULL;
ALTER TABLE [Projects] ADD CONSTRAINT [CK_Projects_OriginalContractValue] CHECK ([OriginalContractValue] >= 0);
-- identical shape for [OriginalBac] = [BAC]
ALTER TABLE [Projects] ADD [RowVersion] rowversion NOT NULL;   -- no DEFAULT emitted: correct, SQL Server rejects one on rowversion
```

Backfill is **correct and ordered safely** (populate before tightening, in one migration). Live EF
model, read from the real `CmPlusDbContext`:

```text
OriginalContractValue: clrType=Decimal IsNullable=False IsConcurrencyToken=False valueGen=Never
OriginalBac          : clrType=Decimal IsNullable=False IsConcurrencyToken=False valueGen=Never
RowVersion           : clrType=Byte[]  IsNullable=False IsConcurrencyToken=True  valueGen=OnAddOrUpdate
```

No `??`-style silent degradation survives on either field (repo-wide grep: only a stale doc comment,
**N-08**). The three original consequences, re-probed — the first with amounts that genuinely
discriminate (Φ = 10.3918% against the baseline, 5.6000% against the edited contract value):

```text
before edit: chain=[1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]
PUT bac/cv=900,000,000 : OK
after PUT: CV=900,000,000 baseline=485,000,000
after edit : chain=[1:PM(q1),2:ProjectDirector(q1),3:Executive(q1)]   <- rung survives

OriginalBac=100,000,000 Project.BAC=110,000,000
BAC(before tA)=100,000,000 (truth 100,000,000)  BAC(after tA)=110,000,000 (truth 110,000,000)
invariant Project.BAC == BAC(now) ? True

submit with baseline=0 : FAIL:ContractValueNotConfigured
```

**M-02 is unchanged and still open** (`after PUT bac=150,000,000: Project.BAC=150,000,000
OriginalBac=100,000,000 BAC(now)=110,000,000  invariant holds? False`) — correctly deferred, it needs
a product decision.

### 10.4 H-03 — PASS

`Project.RowVersion` + `IsRowVersion()` (`ProjectConfiguration.cs:41`) and the interceptor extension
(`RowVersionSaveChangesInterceptor.cs:67-73`) are both present and effective.

```text
-- raw two-context read-modify-write (the Sprint 10 finding, inverted)
first  write: PERSISTED, BAC=487,400,000
second write: DbUpdateConcurrencyException  <- token active   (was: PERSISTED, move lost)

-- probe 8 through the REAL approve handler, two simultaneous finals
final A (winner): OK
final B (loser, stale Project): FAIL:VariationOrderConcurrencyConflict   BAC=487,400,000
retry B: OK status=Approved
FINAL BAC=489,700,000 CV=489,700,000
VO-A BacBefore/After = 485,000,000/487,400,000
VO-B BacBefore/After = 487,400,000/489,700,000
```

Both moves land and the two documents' immutable stamps chain correctly — `domain-rules.md` §5.1's
"an `Approved` VO whose BAC move did not land must be impossible" now holds across two approvals, not
only within one. See **N-07** for the InMemory caveat on the retry.

### 10.5 M-01 / M-03 / L-01 — all hold

```text
M-01  raw ScopeItem.BudgetCostDelta rewrite : BLOCKED (InvalidOperationException)
      re-read BudgetCostDelta = 800,000 (VO.Amount = 800,000)
      Deleted state still legal? YES  <- re-price lifecycle unaffected
M-03  seeded TH-Default-VO : Pct=10.00 Role=Executive | TH-Default-IPC: Pct=NULL
      41.2%-varied project, 400,000 VO (band 1 = PM only) -> chain=[1:PM(q1),2:Executive(q1)]
      PM: OK (PendingApproval)   EXE: OK (Approved)      <- control is live, not inert
L-01  Reject/Return/Withdraw/Cancel with a null actor : all FAIL:VariationOrderActorRequired
      Guid.Empty actor rows in ApprovalActions: 0
ADR-0016 escape: both role-holders voted; PD-A (already voted) returns : OK status=Draft rev=2
```

### 10.6 The new `IProjectRepository` change — audited, no finding

`IProjectRepository.SaveChangesAsync` → `Task<bool> TrySaveChangesAsync`
(`IProjectRepository.cs:35`), `ProjectErrorCodes.ConcurrencyConflict`, mapped to 409 at
`ResultProblemMapper.cs:136`.

- Repo-wide grep: **exactly three** production paths mutate a `Project` —
  `UpdateProjectCommandHandler:49`, `SetEacVariantDefaultCommandHandler:26` (both on the new
  `TrySaveChangesAsync`) and `ApproveVariationOrderCommandHandler:215` (saved through
  `IVariationOrderRepository.TrySaveChangesAsync` on the same scoped context). All three translate a
  conflict to 409. No caller of the old member remains.
- `ProjectRepository.TrySaveChangesAsync` catches **only** `DbUpdateConcurrencyException`, so a
  constraint violation still surfaces — the correct, narrow catch.
- Execution-verified, two racing `PUT /api/v1/projects/{id}`:
  `PUT #1: OK` / `PUT #2 (stale): FAIL:ProjectConcurrencyConflict` — a 409, **never an unhandled 500,
  and never a silent lost update**. The pre-emptive fix was warranted and is correct.

### 10.7 Regression tests — both present, executed, non-vacuous

| Test | Result |
| :-- | :-- |
| `V6_Variant_The_Escalation_Guard_Still_Fires_Even_When_The_Pinned_Policy_Is_Deactivated_Between_Submission_And_The_Final_Vote_H01` (`tests/CMPlus.Integration.Tests/Vo/VariationOrderApprovalRoutingFixtureTests.cs:287`) | **Passed 1/1**, 0 skipped |
| `ReturnForRevision_Must_Never_Gain_A_DuplicateChainVoter_Guard_Even_For_An_Actor_Who_Already_Voted` (`tests/CMPlus.Application.Tests/Features/VariationOrder/RejectVariationOrderCommandHandlerTests.cs:267`) | **Passed**, 0 skipped |
| `Handle_Still_Blocks_The_Final_Approval_When_The_Pinned_Policy_Was_Deactivated_By_An_Ordinary_Edit_Since_Submission_H01` (unit-level twin, not requested) | **Passed**, 0 skipped |

Non-vacuity was established **independently of the assertions**: probe 22's re-run proves the V-6
variant's property genuinely discriminates (`Resolve` still returns the no-`Executive` fallback for a
deactivated policy, so reverting the handler to `Resolve` must fail the test), and the ADR-0016 probe
reproduces the live single-actor escape with **both** role-holders exhausted. The V-6 variant
additionally seeds a **real** scope item specifically so a removed H-01 fix surfaces as the true
bypass instead of a masking `VariationOrderUnknownActivity` — a mutation-aware precaution worth
keeping.

### 10.8 New findings — Low (tracked, non-blocking)

| ID | Finding | Fix |
| :-- | :-- | :-- |
| **N-06** | `ApprovalRoutingService.Resolve` (`:49-55`) still substitutes the permissive one-step `FallbackApprovalChain` when `CandidatePolicies` is **non-empty but nothing is selectable** — H-01's option 2, deliberately not implemented since option 1 closed the guard path. Execution-verified on the **Submit** path: with the tenant's only VO policy deactivated, a 50,000,000 VO on a project already 41.2% varied routes to `[1:ProjectDirector(q1)]`, pins `Guid.Empty`, and one PD signature approves it — Σ reaches **51.55%** of baseline with no `Executive`. Because the pin is `Guid.Empty`, `CheckEscalationBypassAsync` returns early, so the §4.7 guard cannot fire either. **Not reachable through any shipped endpoint today**: `UpdateApprovalPolicyCommand` carries no caller-supplied `EffectiveFrom` (the handler uses `now`) and always inserts the replacement in the same save, and `Deactivate()` has exactly one production caller — verified, no gap window exists. It becomes reachable the moment a deactivate-without-replace endpoint, a caller-supplied `EffectiveFrom`, or the Sprint 15 project-scoped-override surface ships. | `Resolve` returns `Failure(ApprovalErrorCodes.PolicyGap)` when `CandidatePolicies` is non-empty and selection yields nothing. Reserve the fallback strictly for §5.3 step 6's "this tenant has no policy at all". |
| **N-07** | **EF Core InMemory does not roll back a failed `SaveChanges`** — isolated: an unrelated insert staged in the same `SaveChanges` as a conflicting row **persists** despite the `DbUpdateConcurrencyException` (the conflicting row itself does not move). Consequence for H-03: the loser's `ApprovalAction` row survives (2 → 3), so a retry **by the same approver** then trips `DuplicateChainVoter` and the document strands. On SQL Server the implicit `SaveChanges` transaction rolls it back (no `AutoTransactionBehavior` override anywhere; no execution strategy configured), so this is an **environment artifact, not a production defect** — but it means every InMemory assertion of the form "a failed transition wrote nothing" is unsound **on the concurrency path specifically** (guard paths that return before staging remain sound). | Re-verify the same-approver retry against real SQL Server (§8 item 1). Do not add an InMemory test asserting post-conflict state. |
| **N-08** | Documentation drift left by the fixes: `ApprovalRoutingModels.cs:61` still describes the baseline as `OriginalContractValue ?? ContractValue`; `20260810045006_Sprint10_Project_OriginalBac`'s backfill comment claims "no `VariationOrder` can exist yet", but that table is created by the **earlier** `20260810025227` migration — the backfill is correct only because all six migrations are unapplied and will run in one batch (and the app cannot start against the intermediate schema). Harmless today, misleading to the next reader. | Correct both comments, or move the `OriginalBac` migration ahead of `Sprint10_VariationOrder`. Cosmetic: `Project` audit rows now carry `RowVersion` in before/after JSON (tenant's own data; `PaymentCertificate`/`VariationOrder` already do the same). |

### 10.9 Status of the Sprint 10 findings

| ID | Status |
| :-- | :-- |
| H-01, H-02, H-03 | **CLOSED** — execution-verified above |
| M-01, M-03, L-01 | **CLOSED** — execution-verified above |
| M-02, M-04 | **Open** — both need a product decision; correctly not rushed |
| L-02, L-03, L-04, L-05, L-06 | **Open**, unchanged; L-05 (`FallbackPolicy`) is now third sprint open |
| N-06, N-07, N-08 | **New**, Low, tracked |

§8's list of what cannot be verified without a running system stands unchanged, with N-07 added as a
newly-quantified limit of the InMemory substitute. ADR-0008 still requires this review again before
Sprint 11 closes if the VO approval surface changes.
