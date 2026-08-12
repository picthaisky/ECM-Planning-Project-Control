# Approval Workflow — Variation Orders & Payment Certificates

Canonical domain reference for the approval state machine and the **per-tenant configurable
permission matrix** (confirmed by the human 2026-07-27; closes docs/9. §11 item 4).
Scope is domain + workflow only — no UI. Companion money math lives in
`docs/specs/master-plan/domain-decisions.md` §2 (payment/retention).

## 1. Definitions

| Term | Definition |
| --- | --- |
| **Variation Order (VO / CO)** | A contractual instruction adding to or omitting from the contract scope, with a signed money value. `Type = Add` (positive) or `Deduct` (negative). |
| **Payment Certificate (IPC)** | Interim Payment Certificate — the document certifying the value of work executed in a period and the net amount due after retention and advance recovery. |
| **Approval chain** | The ordered list of approval *steps* a document must clear, resolved from the permission matrix at submission time. |
| **Step** | One rung of the chain: a required role, a quorum, and its position `StepNo`. |
| **Quorum** | How many distinct users holding the step's role must approve before the step is cleared (default 1). |
| **Routing amount** ($A^{route}$) | The single money value the matrix is evaluated against. See §5.1 — it is **not** always the raw `Amount`. |
| **Return for revision (ตีกลับ)** | Non-terminal rejection: the document goes back to `Draft` for a new revision. Distinct from `Rejected`. |
| **Rejected** | Terminal refusal. The document is dead; a replacement must be a new document with a new number. |
| **DoA (Delegation of Authority)** | The organisation's schedule of who may approve what value — the industry name for the permission matrix. |

## 2. Variation Order state machine

```
                        ┌──────── return for revision (rev+1) ────────┐
                        v                                             │
  [Draft] ──submit──> [PendingApproval(step n)] ──approve all steps──> [Approved] ──> BAC/Contract impact
     │                        │        ^   │
     │                        │        └───┘ approve step n → n+1 (chain not yet exhausted)
     │                        └──reject──> [Rejected]   (terminal)
     └──cancel──> [Cancelled] (terminal)   [PendingApproval] ──withdraw──> [Draft]
```

States: `Draft`, `PendingApproval`, `Approved`, `Rejected`, `Cancelled`.
`Approved`, `Rejected`, `Cancelled` are terminal — no transition leaves them. A superseding VO is
always a **new record**, never an edit of a terminal one.
The `reject` arrow in the diagram is the *terminal* transition; on a step with `QuorumCount > 1` it
fires only once `QuorumCount` distinct role-holders have rejected (§6.2a). Full Sprint 10 rules —
guards, field-freeze matrix, approval effects, fixtures — are in
`docs/specs/variation-order/domain-rules.md`.

**Gap against the current data model:** docs/9. §4 gives `VariationOrder.Status` as
`Pending/Approved/Rejected` only. `Draft` and `Cancelled` must be added, and
`Pending` renamed `PendingApproval` for symmetry with the payment enum.
*(Status 2026-08-10: the `VariationOrderStatus` enum already carries all five values; the migration
that lands them in the database is S10-DB-01, not yet applied.)*
The prototype's "ตีกลับ" badge is currently mapped to `rejected` — it must map to
**return-for-revision** (→ `Draft`), otherwise a returned VO can never be resubmitted.

## 3. Payment Certificate state machine

```
[NotDue] ──period reached──> [Draft] ──submit──> [PendingApproval(step n)]
                                ^                    │        │
                                └── return for rev ──┘        │
                                                     approve all steps
                                                              v
                                                        [Certified] ──payment recorded──> [Paid]
                                     [PendingApproval] ──reject──> [Rejected] (terminal)
```

States: `NotDue`, `Draft`, `PendingApproval`, `Certified`, `Paid`, `Rejected`, `Cancelled`.
Maps onto the prototype's badges: `future` → `NotDue`, `checked` (ตรวจรับแล้ว) → `Certified`,
`paid` (จ่ายแล้ว) → `Paid`.

Two differences from the VO machine:

1. `Certified` is **not** the end — `Paid` records actual disbursement (date, reference,
   bank/cheque). Cash-flow "รับเงินสะสม" sums `Paid`, not `Certified`.
2. Per `.claude/knowledge/patterns/conventions.md`, a certificate is **immutable from
   `PendingApproval` onward**. Corrections after `Certified` are a new certificate (a negative
   adjustment line or a credit certificate), never an edit.

`Paid` amounts are **receipts**, not costs. They must never be written into $AC$ (ACWP) —
the prototype deliberately shows "รับเงินสะสม 238.4 MB" and "จ่ายจริงสะสม (AC) 253.1 MB"
as two different figures.

## 4. Transition table (guards and effects — both document types)

| From | Event | Guard | To | Effects |
| --- | --- | --- | --- | --- |
| `Draft` | `Submit` | chain resolves to ≥ 1 step (§5.3); all required fields valid | `PendingApproval` (step 1) | snapshot the resolved chain + `ApprovalPolicyVersionId` onto the document; freeze editable fields; audit |
| `PendingApproval` | `Approve` | actor holds step role; actor ≠ creator/submitter (§6.1); actor has not already approved this step | same state, `StepNo+1`, **or** `Approved`/`Certified` when chain exhausted | write `ApprovalAction`; on final step run the approval effects (§7); audit |
| `PendingApproval` | `ReturnForRevision` | actor holds any pending step's role; comment mandatory | `Draft` | `RevisionNo += 1`; **void all approvals collected on this revision**; unfreeze fields; audit |
| `PendingApproval` | `Reject` | actor holds the **final** step's role; actor has cast no prior `Approve`/`Reject` on this revision (§6.2a); comment mandatory | same state (vote recorded) until `QuorumCount` distinct rejectors, then `Rejected` | write `ApprovalAction`; stamp `LastVoteAt` even when non-advancing; terminal only on the $q$-th rejection; audit |
| `PendingApproval` | `Withdraw` | actor = submitter, no step approved yet | `Draft` | void chain snapshot; audit |
| `Draft` | `Cancel` | actor = creator or PM | `Cancelled` | terminal; audit |
| `Certified` | `RecordPayment` | payment reference + date supplied | `Paid` | posts retention accrual and advance recovery to the ledger; audit |

Rules that hold for every transition:

- Only `Reject` (final step) is terminal-negative; **intermediate approvers may only return for
  revision**, never kill a document — mirrors real construction practice where only the ultimate
  authority refuses. On a step with `QuorumCount > 1`, "the ultimate authority" means the quorum,
  not any one member of it — see §6.2a.
- A comment is mandatory on `Reject` and `ReturnForRevision`, optional on `Approve`.
- Concurrency: transitions take an optimistic-concurrency token (`RowVersion`). Two approvers
  clicking simultaneously → the second gets `409 Conflict`, never a double-advance.
- Every transition writes `AuditLog` **and** an `ApprovalAction` row (see §5.2).

## 5. Permission matrix

### 5.1 Is amount-tiered approval the industry norm? — Yes.

Delegation-of-Authority thresholds are standard construction practice, in three independent
traditions the CM+ user base will span:

- **FIDIC**: the Engineer may instruct Variations, but the Particular Conditions routinely
  require the Employer's prior consent above a stated value.
- **Thai public works** (พ.ร.บ.การจัดซื้อจัดจ้างฯ 2560 and its ระเบียบ): approval authority for
  งานเพิ่ม/งานลด is explicitly tiered by value, escalating from หัวหน้าเจ้าหน้าที่พัสดุ up to
  หัวหน้าหน่วยงานของรัฐ.
- **Corporate contractors/developers**: an internal DoA table (PM → Project Director →
  MD/Board) keyed on baht value.

**Recommendation: the matrix must be role + amount-threshold, not role-only.** A role-only matrix
cannot express the single most common real rule ("PM approves VOs under ฿500,000; above that the
Project Director must also sign") and would force per-tenant code branches later.

Add a second, cumulative dimension: many contracts require owner/board sign-off once the
**cumulative** value of approved VOs passes a percentage of the contract sum (commonly 10%), even
when the individual VO is small. The matrix should support this as an *escalation rule*, not by
inflating the amount bands.

### 5.2 Recommended tables — *recommendation for `system-architect`, not an accepted ADR*

```
ApprovalPolicy
  Id, TenantId, ProjectId NULL          -- NULL = tenant-wide default; non-null = project override
  DocumentType   enum { VariationOrder, PaymentCertificate }
  Version        int                     -- bumped on every edit; never edit in place
  IsActive       bit, EffectiveFrom, EffectiveTo NULL
  AllowSelfApproval        bit  default 0
  CumulativeVoEscalationPct decimal(5,2) NULL   -- VO policies only, e.g. 10.00
  CumulativeVoEscalationRole enum NULL

ApprovalPolicyRule                       -- the matrix rows
  Id, ApprovalPolicyId
  StepNo         int                     -- 1..n, sequential; ties are invalid
  MinAmount      decimal(18,2)           -- inclusive
  MaxAmount      decimal(18,2) NULL      -- exclusive; NULL = unbounded
  RequiredRole   enum { PM, PlanningEngineer, QS, ProjectDirector, Executive, Admin }
  RequiredUserId Guid NULL               -- named-person override; use sparingly
  QuorumCount    int default 1

ApprovalAction                           -- immutable ledger, one row per human act
  Id, TenantId, DocumentType, DocumentId, RevisionNo
  StepNo, ActorUserId, ActorRoleAtTime
  Action enum { Submit, Approve, ReturnForRevision, Reject, Withdraw, Cancel, RecordPayment }
  Comment, ActedAt DateTimeOffset
  ApprovalPolicyId, ApprovalPolicyVersion   -- pin the version that routed this document
```

Notes that matter:

- **Version-pin, never mutate.** Editing a policy creates `Version+1`. Documents keep the version
  that routed them so a two-year-old approval is still explainable in a dispute. Retro-active
  matrix edits are the classic audit finding in payment systems.
- `RequiredUserId` exists for the "only the MD may sign" case; prefer roles so staff turnover
  doesn't strand documents.
- Single currency (THB) is assumed. If multi-currency ever lands, thresholds need a currency
  column and a rate date — flag now, don't build now.

### 5.3 Routing resolution algorithm (deterministic, fail-closed)

1. **Compute $A^{route}$:**
   - VO: $A^{route} = |Amount|$. **Absolute value is mandatory** — omitting a ฿5,000,000 scope is
     as consequential as adding it, and a signed value would fall outside every `MinAmount ≥ 0`
     band and silently produce an empty chain.
   - Payment Certificate: $A^{route} = G_k$, the **gross certified value of the period**
     (before retention and advance recovery). Rationale: retention is a temporary hold that will be
     paid later, so the gross is the true value being certified, and it keeps VO and IPC authority
     on the same scale. *(Alternative: route on $N_k$, the net cash out. Lower authority triggers.
     Flagged as an open question in §9.)*
2. **Select policy:** project-specific active policy for the `DocumentType`, else the tenant-wide
   active policy, matching the submission date against `EffectiveFrom/To`.
3. **Select rules:** all rules where $MinAmount \le A^{route} < MaxAmount$ (or `MaxAmount IS NULL`).
4. **Order by `StepNo`** → the chain.
5. **Apply cumulative-VO escalation** (VO only): if
   $\dfrac{\sum \text{approved VO} + Amount}{\text{ContractValue}} \times 100 > CumulativeVoEscalationPct$,
   append `CumulativeVoEscalationRole` as a final step if it is not already in the chain.
6. **Fail closed:** an empty chain **blocks submission** with `ApprovalPolicyGap` (422). A missing
   or misconfigured policy must **never** auto-approve. If no policy exists at all, fall back to a
   single hard-coded step requiring `ProjectDirector` — restrictive, not permissive.
7. Bands must be validated non-overlapping and gap-free per `StepNo` when a policy is saved;
   overlapping bands are a configuration error, not a runtime tie-break.
8. If the document's amount changes during a revision, the chain is **re-resolved on resubmission**
   (a revised VO that grew from ฿400k to ฿600k must now collect the Director's signature).

## 6. Business rules

1. **Separation of duties.** The creator and the submitter may not approve any step, unless
   `AllowSelfApproval = 1` on the policy. Default off. A single user may not satisfy two steps of
   the same chain even if they hold both roles — each step needs a distinct human.
2. **Quorum (approval).** A step clears only when `QuorumCount` *distinct* users of that role have
   approved it.

   **2a. Quorum binds rejection too — and no one may vote both ways.** *(Ruled by `domain-expert`
   2026-08-10, closing finding **N-05** in `docs/security/reviews/sprint-09.md` §9.5 and its
   predecessor L-02 §4. Full reasoning and fixtures V-11a–f:
   `docs/specs/variation-order/domain-rules.md` §8.)*

   - A step terminates the document only when **`QuorumCount` *distinct* users of that role have
     rejected it**, counted exactly as approvals are (`Action = Reject`, same `RevisionNo`, same
     `StepNo`). Until the $q$-th rejection the vote is recorded as an `ApprovalAction`, the
     aggregate is stamped (`LastVoteAt`, so `rowversion` serialises concurrent rejectors — the
     N-03 fix applies in both directions), and the document stays `PendingApproval`.
   - **No actor may cast both an `Approve` and a `Reject` on the same revision.** The existing
     `DuplicateChainApprover` predicate widens to `Action ∈ {Approve, Reject}` and is renamed
     **`DuplicateChainVoter`**. It stays strictly broader than either quorum count's predicate, so
     no actor can appear twice in either counted set.
   - Unchanged: only the **final** step's role holder may reject at all; intermediate approvers may
     only `ReturnForRevision`; `q` is read from the document's **snapshotted** rung, never
     re-derived from the policy (H-01).
   - **`ReturnForRevision` is deliberately *not* quorum-bound** and stays available to any holder of
     any pending step's role. It is what makes 2a deadlock-free: a `q = 2` step holding one
     approval and one rejection satisfies neither quorum and both voters are now blocked, so the
     lone dissenter sends the document back instead — how a split committee actually behaves. Expose
     `QuorumSplit` plus per-step vote counts on the document DTO so a human can see the condition.

   **Why:** the object of a quorum is the *decision*, not its direction. An organisation that
   configures dual control is asserting "a decision of this consequence needs two humans"; reading
   that as "two to say yes, one to say no" is a different control from the one the customer
   switched on. Refusal is a positive act with contractual weight — a rejected VO can refuse a
   contractor payment for work already instructed; under FIDIC 3.5 (1999) / 3.7 (2017) the
   Engineer's refusal is a formal **determination** appealable to the DAAB, and under Thai public
   procurement งานเพิ่ม/งานลด are decided by a คณะกรรมการตรวจรับพัสดุ acting by **มติ**, not by any
   one member. The prior literal reading of §6.1 (which restricts *approval* only) was silence, not
   a decision. **Blast radius:** `QuorumCount = 1`, the default and overwhelmingly common case, is
   completely unaffected — one rejector still terminates.

   ⚠ **This changes shipped Sprint 9 Payment Certificate behaviour**, not only Sprint 10's VOs:
   (i) an actor who approved 1-of-2 may currently then reject (execution-verified, §9.5); (ii) one
   rejector currently terminates a `QuorumCount = 2` step; (iii) rejections do not currently stamp
   `LastVoteAt`. All three are to be fixed in the shared handlers during Sprint 10 and re-verified
   by execution under S10-SEC-01.
3. **Revision voiding.** `ReturnForRevision` voids every approval collected on that revision. No
   partial carry-over — it is the only defensible rule when the amount may have changed.
4. **Immutability.** From `PendingApproval` onward the money fields are frozen. Payment
   certificates stay immutable after `Certified` forever (conventions.md). For a VO the same
   freeze covers `Amount`/`Type` **and the scope payload**, with one deliberate difference:
   supporting attachments may be **appended** (never removed or replaced) while `PendingApproval`,
   because a variation's case rests on drawings and site instructions and forcing a
   `ReturnForRevision` — which voids every collected approval — merely to attach one is a
   workflow failure. Append-only means nothing already signed is altered; the addition writes an
   `AuditLog` entry, **not** an `ApprovalAction` (that enum stays "a human decision act"). See
   `docs/specs/variation-order/domain-rules.md` §2.4.
5. **Tenant scoping.** Policy lookup, approver lookup and every document query are `TenantId`-scoped
   (ADR-0002). A cross-tenant approver resolution is release-blocking.
6. **Audit.** Every transition writes `AuditLog` (Before/After JSON) *and* `ApprovalAction`.
   `ApprovalAction` is append-only — corrections are compensating rows.
7. **Notification** is a side effect of a transition, never a state. Failure to notify must not
   roll back an approval.
8. **Delegation** (approver on leave) is deliberately out of scope for v1; if added, model it as
   `ApprovalDelegation(FromUserId, ToUserId, From, To, DocumentType)` and record the *acting*
   delegate plus the *delegated-from* user on `ApprovalAction`.

## 7. Effects on approval

**VO reaching `Approved`:**

- $BAC_{new} = BAC_{old} + Amount$ (signed; a Deduct VO carries a negative `Amount`).
- `ContractValue` increases/decreases by the same signed amount → this **moves the retention cap
  ceiling and the advance-recovery base**; see domain-decisions.md §2.
- S-Curve rebaselines from `ApprovedAt` forward. Historical PV/EV/AC points are never rewritten
  (audit integrity — existing rule in evm-formulas.md).
- The activities/WBS nodes the VO adds become schedulable; CPM must be re-run.
- A Deduct VO can push `ContractValue` below retention already held — the cap headroom formula
  uses `max(0, …)` so retention is **never clawed back automatically**.

**Payment Certificate reaching `Certified`:** posts retention accrual and advance recovery to the
ledger; the certificate becomes printable and immutable. Reaching `Paid`: records the cash receipt.

## 8. Routing fixtures (turn straight into unit tests)

Sample tenant policy **`TH-Default-VO`** (THB):

| StepNo | MinAmount | MaxAmount | RequiredRole |
| --- | --- | --- | --- |
| 1 | 0.00 | 500,000.00 | ProjectManager |
| 1 | 500,000.00 | 5,000,000.00 | ProjectManager |
| 2 | 500,000.00 | 5,000,000.00 | ProjectDirector |
| 1 | 5,000,000.00 | *null* | ProjectManager |
| 2 | 5,000,000.00 | *null* | ProjectDirector |
| 3 | 5,000,000.00 | *null* | Executive |

`CumulativeVoEscalationPct = 10.00`, `CumulativeVoEscalationRole = Executive`,
`ContractValue = 485,000,000.00`.

| Fixture | Input | $A^{route}$ | Expected chain |
| --- | --- | --- | --- |
| R1 | VO-018 Add **+2,400,000.00**, cumulative approved VO 6,000,000 | 2,400,000.00 | `[PM, ProjectDirector]` (2 steps) |
| R2 | VO-015 Deduct **−800,000.00** | **800,000.00** (abs) | `[PM, ProjectDirector]` — proves the absolute-value rule; signed routing would yield an empty chain |
| R3 | VO Add **+300,000.00** | 300,000.00 | `[PM]` (1 step) |
| R4 | VO Add **+3,200,000.00** with cumulative approved VO already **46,000,000.00** → $(46{,}000{,}000+3{,}200{,}000)/485{,}000{,}000 = 10.14\% > 10\%$ | 3,200,000.00 | `[PM, ProjectDirector, Executive]` — band gives 2 steps, escalation appends the 3rd |
| R5 | VO Add **+50,000.00** against a policy whose lowest `MinAmount` is 100,000.00 | 50,000.00 | **no chain → submission blocked**, `ApprovalPolicyGap`; must not auto-approve |
| R6 | Boundary: VO Add **+500,000.00** exactly | 500,000.00 | `[PM, ProjectDirector]` — `MinAmount` inclusive, `MaxAmount` exclusive |

Sample policy **`TH-Default-IPC`**: step 1 `QS` (0 → ∞), step 2 `ProjectManager` (0 → ∞),
step 3 `ProjectDirector` (10,000,000.00 → ∞).

| Fixture | Input | $A^{route}$ | Expected chain |
| --- | --- | --- | --- |
| R7 | Certificate, gross certified 21,600,000.00 | 21,600,000.00 | `[QS, PM, ProjectDirector]` |
| R8 | Certificate, gross certified 5,000,000.00 | 5,000,000.00 | `[QS, PM]` |
| R9 | R7 approved by QS, then **returned for revision** by PM, resubmitted at gross 9,000,000.00 | 9,000,000.00 | `RevisionNo = 2`; QS approval **voided**; new chain `[QS, PM]` — re-approval starts at step 1 |
| R10 | R8 where the QS is also the submitter | 5,000,000.00 | QS `Approve` **rejected** (`SelfApprovalNotPermitted`) while `AllowSelfApproval = 0` |

## 9. Open questions for the human — [ต้องยืนยัน]

1. **IPC routing amount:** gross certified $G_k$ (recommended) or net payment $N_k$? Affects how
   often the Director is pulled in — at $r=5\%,a=10\%$ the net is 15% lower than the gross, so
   certificates near a threshold would route differently.
2. **Cumulative-VO escalation threshold** — **Resolved 2026-08-10, see ADR-0015.** Default
   **10.00%**; the counter **resets on a formal contract amendment** (re-baselines against the
   amended value); numerator is **net signed** (additions less deductions); denominator is the
   **baseline contract value for the current cumulative window**, not the live, self-diluting
   `ContractValue` the code originally used (that was a bug, fixed alongside the ADR).
   `CumulativeVoEscalationPct` stays nullable — `NULL` still means "no escalation configured", never
   "10" — and `ApprovalPolicySeeder` seeds `NULL` deliberately. Fixtures: `domain-rules.md` §4.
3. **Can an intermediate approver reject outright?** Recommended answer is no (return-for-revision
   only). Confirm this matches the organisation's DoA. *(Distinct from the quorum question, which
   is no longer open — see §6.2a for the ruling on whether one person may terminally reject a step
   configured for two signatures.)*
4. **Role list:** docs/9. §4 lists `PM/Planning/Site/QS/Executive/Admin` but the docs elsewhere
   mention Project Director. `ProjectDirector` is required for a 3-tier VO matrix — confirm it is a
   real role to be added to the `User.Role` enum.
5. **Are VOs and IPCs ever approved by an external party** (Employer's representative / consultant
   engineer) who is not a CM+ tenant user? If yes, an "external approval recorded by X on behalf
   of Y" action type is needed for evidentiary completeness.
