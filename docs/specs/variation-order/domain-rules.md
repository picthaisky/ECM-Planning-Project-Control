# Variation Order — Domain Rules (Sprint 10)

**Stage-2 artifact.** Author: `domain-expert` · Date: 2026-08-10 · Feature: `variation-order`
**Consumers:** `system-architect` (design.md), `backend-developer` (S10-BE-01/02/03),
`database-engineer` (S10-DB-01), `frontend-developer` (S10-FE-01/02), `qa-engineer` (S10-QA-01/02),
`security-auditor` (S10-SEC-01).

**Upstream sources this document is bound by** (read them, do not paraphrase from here):

| Source | What it fixes |
| :-- | :-- |
| `docs/10.` §8 Sprint 10 | the task table and every DoD clause quoted below |
| `docs/10.` §11 **R-04** | the open decision on the cumulative escalation threshold |
| `.claude/knowledge/domain/approval-workflow.md` | state machines §2–§4, routing §5.3, SoD §6, effects §7, fixtures R1–R10 |
| `.claude/knowledge/domain/payment-retention.md` | $R_k$/$D_k$/$N_k$, the retention cap, the advance base |
| `.claude/knowledge/domain/evm-formulas.md` | $BAC$, the five EAC variants, the unified $PF$ form, edge matrix |
| `.claude/knowledge/domain/actual-cost.md` + ADR-0013 | $AC$ is never a certified payment; a VO posts no cost |
| ADR-0002 / ADR-0008 / ADR-0009 | tenant filter · version-pinned amount-tiered policy · `EvmPeriodSnapshot` immutability |
| `docs/security/reviews/sprint-09.md` §9.5 | N-01…N-05, all inherited by this sprint |

Precision throughout: money `decimal(18,2)`, percent `decimal(5,2)`, ratios `decimal(9,6)`
(`RoundingRules.RatioDecimals = 6`), all **half-away-from-zero**
(`MidpointRounding.AwayFromZero` — never .NET's default banker's rounding; risk R-12).
Currency: THB, single-currency (approval-workflow.md §5.2).

---

## 1. Definitions

| Term | Symbol | Definition |
| :-- | :-- | :-- |
| Variation Order | VO | A contractual instruction adding to or omitting from the contract scope, carrying a signed money value and (optionally) a time impact. In Thai: ใบสั่งงานเปลี่ยนแปลง / งานเพิ่ม-งานลด. Also called Change Order (CO) — the same record. |
| VO type | — | `Add` ⟹ $A > 0$; `Deduct` ⟹ $A < 0$. `Type` is **derived from the sign of `Amount`**, never independently settable, so the two can never disagree (see §7.3). |
| VO amount | $A$ | `VariationOrder.Amount decimal(18,2)`, **signed**. The contract-price impact of the variation. |
| Routing amount | $A^{route}$ | The single value the DoA matrix is evaluated against. For a VO, $A^{route} = \lvert A \rvert$ — see §3. |
| Revision number | $\rho$ | `VariationOrder.RevisionNo`, 1-based, bumped **only** by `ReturnForRevision`. |
| Approval chain | — | The ordered list of steps resolved at `Submit` and **snapshotted onto the document** (H-01 fix, sprint-09.md §9.2). Never re-derived at decision time. |
| Step / quorum | $q$ | One rung: `StepNo`, `RequiredRole`, `QuorumCount`. A step clears when $q$ **distinct** users of that role approve it. |
| Original contract value | $C^{orig}$ | The Accepted Contract Amount / วงเงินตามสัญญา **as signed**, before any VO. Moved **only** by a formal contract amendment. See §4.3 — this field does not yet exist and must be added. |
| Current contract value | $C^{cur}$ | `Project.ContractValue` — the contract sum **including approved VOs** (payment-retention.md §1). The base for retention cap and advance rate. |
| Budget at completion | $BAC$ | `Project.BAC`. The EVM budget baseline. Distinct from $C^{cur}$ in principle; equal by default (payment-retention.md §7 Q2, still open). |
| Original BAC | $BAC^{orig}$ | $BAC$ as first baselined, before any VO. Needed to make $BAC(t)$ reconstructible — see §5.5. |
| Cumulative VO base | $\Sigma^{VO}$ | The running total of approved VO amounts used by the escalation test. Its exact definition is the open decision R-04 — see §4. |
| Escalation threshold | $\theta$ | `ApprovalPolicy.CumulativeVoEscalationPct decimal(5,2)`, nullable. **No default is set by this document** (R-04, §4.5). |
| Approval timestamp | $t_A$ | `VariationOrder.ApprovedAt DateTimeOffset` — the rebaseline boundary. |
| Rebaseline boundary | — | The instant $t_A$ at which $BAC$ and $C^{cur}$ step. Everything dated **before** $t_A$ keeps the old values (ADR-0009). |
| Scope payload | — | The concrete WBS/Activity budget deltas the VO carries. Its net must equal $A$ — invariant §5.2. |
| Return for revision | — | ตีกลับ. Non-terminal refusal → `Draft`, $\rho{+}1$. **Not** `Rejected` (docs/10. §8 S10-FE-01 explicitly). |
| Rejected | — | Terminal refusal. The VO is dead; a replacement is a new record with a new number. |

---

## 2. The five-state machine

### 2.1 States and legal transitions

```
                     ┌──────── ReturnForRevision (ρ+1) ────────┐
                     v                                          │
  [Draft] ──Submit──> [PendingApproval(step n, quorum q)] ──all steps cleared──> [Approved]
     │  ^                    │        ^   │                                        (terminal)
     │  │                    │        └───┘ step n cleared → n+1                        │
     │  └───Withdraw─────────┤                                                   BAC / ContractValue
     │                       └──Reject (quorate, final step only)──> [Rejected] (terminal)
     └──Cancel──> [Cancelled] (terminal)
```

`VariationOrderStatus` (already shipped, `backend/src/CMPlus.Domain/Enums/VariationOrderStatus.cs`):
`Draft = 1`, `PendingApproval = 2`, `Approved = 3`, `Rejected = 4`, `Cancelled = 5`.
`Approved`, `Rejected`, `Cancelled` are **terminal — no transition leaves them, ever**.

### 2.2 Where the VO machine genuinely differs from the Payment Certificate (Sprint 9 precedent)

The instruction is to follow Sprint 9 unless the domain differs. It differs in exactly five places;
everywhere else the IPC rules are adopted verbatim.

| # | Difference | Why |
| :-- | :-- | :-- |
| 1 | **No `NotDue`.** A VO exists from the moment it is drafted. | An IPC is scheduled by period; a variation is instructed by event. There is no "not yet due" VO. |
| 2 | **No `Paid`.** | A VO disburses nothing. Its money reaches the contractor through subsequent IPCs. A VO must **never** write a `ProjectFinanceLedger` row (§6.4). |
| 3 | **`Approved` fires an irreversible external effect** ($BAC$, $C^{cur}$, CPM, S-Curve). IPC's `Certified` is followed by `Paid` and only posts ledger rows. | Correction of a wrongly-approved VO is a **new, opposite-signed reversing VO** referencing the original (`ReversesVoId`), never an edit, never a delete. |
| 4 | **Supporting evidence may be appended while `PendingApproval`** (§2.4) — money/scope stay frozen. | An IPC's fields are all computed figures; a VO's case rests on drawings, site instructions, rate build-ups. Forcing a `ReturnForRevision` (which voids every collected approval) merely to attach a drawing is a real-world workflow failure. Append-only means nothing already signed is altered. |
| 5 | **`Reject` is quorum-bound** (§8, finding N-05). | Rejecting a VO refuses payment for work that may already have been instructed and executed. See §8 for the full ruling — it changes Sprint 9's IPC behaviour too. |

### 2.3 Transition table — guards and effects

Guards are evaluated in the order listed. Every transition writes **both** an `AuditLog` row
(Before/After JSON) **and** an `ApprovalAction` row (append-only, ADR-0008 / approval-workflow.md §6.6),
and takes the `RowVersion` optimistic-concurrency token → a losing racer gets **409**, never a
double-advance.

| From | Event | Guards | To | Effects |
| :-- | :-- | :-- | :-- | :-- |
| `Draft` | `Submit` | (a) all required fields valid; (b) $\Delta B_{scope} = A$ (§5.2); (c) chain resolves to $\ge 1$ step, else **422 `ApprovalPolicyGap`** and the VO stays `Draft`; (d) if $\theta$ configured and $C^{orig} \le 0$ → **422 `ContractValueNotConfigured`** (§4.6) | `PendingApproval` (step 1) | snapshot the resolved chain rows (`StepNo`, `RequiredRole`, `QuorumCount`) + `ApprovalPolicyId`/`Version` + `AllowSelfApproval` onto the document; freeze money and scope; stamp `SubmittedByUserId`/`SubmittedAt` |
| `PendingApproval` | `Approve` | (a) actor holds the **snapshotted** current step's role; (b) actor ∉ {creator, submitter} unless pinned `AllowSelfApproval`; (c) actor has cast **no** prior `Approve` or `Reject` on this $\rho$ (widened `DuplicateChainVoter`, §8.3); (d) **at the final step only:** escalation re-check passes (§4.7) | same state at `StepNo+1` when quorum reached, **or** `Approved` when the chain is exhausted | stamp `LastVoteAt` unconditionally (N-03); write `ApprovalAction`; on reaching `Approved` run §5 |
| `PendingApproval` | `Reject` | (a) `CurrentStepNo == TotalSteps` (final step only); (b) actor holds the final step's role; (c) same duplicate-voter guard as `Approve`; (d) **comment mandatory**; (e) reject-quorum reached (§8.2) | `PendingApproval` (vote recorded) until the $q$-th distinct rejector, then `Rejected` | stamp `LastVoteAt`; write `ApprovalAction`; **no** money effect ever |
| `PendingApproval` | `ReturnForRevision` | (a) actor holds **any** pending step's role; (b) comment mandatory. **Not** quorum-bound — deliberately (§8.4) | `Draft` | $\rho \mathrel{+}= 1$; **void every vote collected on this revision** (delete the chain snapshot rows, clear `CurrentStepNo`/`TotalSteps`/policy pin/`AllowSelfApproval`/`LastVoteAt`); unfreeze money + scope |
| `PendingApproval` | `Withdraw` | actor = submitter **and** `CurrentStepNo == 1` **and** zero votes cast on this $\rho$ | `Draft` | void chain snapshot; $\rho$ **unchanged** |
| `Draft` | `Cancel` | actor = creator or PM; **comment mandatory** (VO-specific — a cancelled instruction is itself evidence) | `Cancelled` | terminal |
| any terminal | anything | — | — | **rejected with 409 `VoIsTerminal`** |

Note on `Withdraw`: N-02 (sprint-09.md §9.5) records that `Withdraw`/`Cancel` exist on the IPC
aggregate but are exposed by **no controller**, so a stranded document has no way out. Sprint 10
must expose both for the VO. The VO's own deadlock escape does not depend on it
(`ReturnForRevision` is always available to any pending-step role holder — §8.4), but shipping a
document type with an unreachable recovery path repeats a known finding.

### 2.4 Field freeze matrix

Modelled on `PaymentCertificate`'s freeze, which is enforced **by construction** — one writer
method, guarded on `Status` (`EnsureMoneyFieldsEditable`). Reproduce that shape: a single
`SetVariationContent(...)` that refuses unless `Status == Draft`, with no other writer anywhere in
the type.

| Field | `Draft` | `PendingApproval` | `Approved` | `Rejected` | `Cancelled` |
| :-- | :--: | :--: | :--: | :--: | :--: |
| `VoNumber` | set at creation | immutable | immutable | immutable | immutable |
| `Amount`, `Type` (derived) | editable | frozen | frozen | frozen | frozen |
| `Description`, `Justification` | editable | frozen | frozen | frozen | frozen |
| Scope payload (WBS/Activity budget deltas) | editable | frozen | frozen | frozen | frozen |
| `TimeImpactDays` | editable | frozen | frozen | frozen | frozen |
| Supporting attachments | add / remove | **add only** (append-only, audited) | add only | add only | add only |
| `RevisionNo` | — | — | frozen | frozen | frozen |
| Chain snapshot (`CurrentStepNo`, `TotalSteps`, `ApprovalPolicyId`/`Version`, `AllowSelfApproval`, step rows, `LastVoteAt`) | absent | written by `Submit`, cleared by `ReturnForRevision`/`Withdraw` | frozen | frozen | absent |
| `ApprovedAt`, `BacBefore/After`, `ContractValueBefore/After`, `CumulativeVoPctAtApproval`, `EscalationBasisContractValue` | — | — | **written once at the transition, immutable** | — | — |
| `RowVersion` | EF-managed | EF-managed | EF-managed | EF-managed | EF-managed |

Two persistence-layer notes carried from sprint-09.md:

- The chain snapshot rows must implement `INeverModified` (N-01 fix) — `Added`/`Deleted` legal
  (`Submit` builds, `ReturnForRevision` clears), `Modified` blocked by
  `AppendOnlyGuardInterceptor`. Otherwise the snapshot is the sole authority record and the one
  table nothing protects.
- The money/scope freeze must be enforced at the persistence layer too (M-01's fix pattern:
  reject per-property `IsModified` on the frozen set once `Status != Draft`), not only by the
  aggregate's discipline.
- The natural-key index on the VO chain snapshot must be `UNIQUE (TenantId, VariationOrderId,
  RevisionNo, StepNo)` — N-04 is still open for the IPC; do not repeat it.

---

## 3. Routing: $A^{route} = \lvert A \rvert$

### 3.1 Rule

$$A^{route} = \lvert A \rvert \qquad A \in \text{decimal}(18,2),\ A^{route} \ge 0$$

The chain is every rule of the selected, version-pinned policy satisfying

$$\textit{MinAmount} \le A^{route} < \textit{MaxAmount} \quad (\textit{MaxAmount}=\texttt{NULL} \Rightarrow +\infty)$$

ordered by `StepNo`. `MinAmount` **inclusive**, `MaxAmount` **exclusive** (fixture R6 exists to pin
this). Empty chain ⟹ **422 `ApprovalPolicyGap`**, document stays `Draft` — never auto-approve
(approval-workflow.md §5.3 step 6).

### 3.2 Why the signed amount must not drive routing authority

Three independent reasons; the first is the domain one and the one to quote to a client.

1. **Authority is a function of consequence magnitude, not of direction.** Omitting ฿5,000,000 of
   scope is as consequential a commitment as adding it: it deprives the contractor of turnover and
   recovery of preliminaries, it may found a claim for loss of profit (FIDIC 1999/2017 Sub-Clause
   13.3 valuation and 12.4-type omission provisions; Thai practice treats งานลด as a สาระสำคัญ
   change requiring the same committee), and it reduces the security base against which retention
   and the performance bond are measured. A DoA that lets a junior role sign away ฿5,000,000 of
   the contractor's work because the number is negative is not a DoA.
2. **Mechanically, a signed value falls outside every band.** All `MinAmount` are validated
   $\ge 0$, so a negative routes to an empty chain. Under the fail-closed rule that is a
   **422 `ApprovalPolicyGap`** — i.e. *Deduct VOs could never be submitted at all*. That is a
   functional break, not a security hole, but it is a break that invites the wrong fix.
3. **The wrong fix is a security hole.** The obvious repair for (2) — "add a catch-all band at
   `MinAmount = 0` so deducts route somewhere" — sends **every omission of every size** to the
   lowest tier in the matrix. Applying $|\cdot|$ at the routing boundary removes the pressure to
   ever make that change.

Already implemented correctly: `ApprovalRoutingService.cs:43-45` applies `Math.Abs` for
`ApprovalDocumentType.VariationOrder` only, and correctly **not** to an IPC's already-non-negative
$G_k$ (confirmed sound, sprint-09.md §5). S10-BE-02's DoD says *"use the Sprint 2 service, do not
rewrite it"* — comply.

⚠ **Do not carry $|\cdot|$ into the escalation ratio.** Routing measures *the magnitude of one
decision*; escalation measures *cumulative drift of the contract sum*. Different questions,
different aggregations. The apparent inconsistency is deliberate — see §4.2, and note that the
numerator's sign convention is itself an open question.

### 3.3 Sample policies for the fixtures

**`TH-Default-VO`** (tenant-wide, THB) — as approval-workflow.md §8:

| StepNo | MinAmount | MaxAmount | RequiredRole | QuorumCount |
| --: | --: | --: | :-- | --: |
| 1 | 0.00 | 500,000.00 | ProjectManager | 1 |
| 1 | 500,000.00 | 5,000,000.00 | ProjectManager | 1 |
| 2 | 500,000.00 | 5,000,000.00 | ProjectDirector | 1 |
| 1 | 5,000,000.00 | *null* | ProjectManager | 1 |
| 2 | 5,000,000.00 | *null* | ProjectDirector | 1 |
| 3 | 5,000,000.00 | *null* | Executive | 1 |

`CumulativeVoEscalationRole = Executive`; `CumulativeVoEscalationPct` = **[R-04 — see §4.5]**;
$C^{orig} = 485{,}000{,}000.00$; `AllowSelfApproval = 0`.

Band validity check for M-03's tightened `ValidateBands`: StepNo 1 covers $[0,500\text{k}) \cup
[500\text{k},5\text{M}) \cup [5\text{M},\infty)$ — non-overlapping and contiguous; StepNo 2 covers
$[500\text{k},\infty)$; StepNo 3 covers $[5\text{M},\infty)$. **Valid.**

**`TH-Gap-VO`** (for R5 only): identical, except the lowest `MinAmount` is `100,000.00` — nothing
covers $[0, 100{,}000)$.

**`TH-DualControl-VO`** (for the N-05 fixtures, §8.5): one band $[0, \texttt{null})$,
StepNo 1 = `ProjectDirector`, **`QuorumCount = 2`**.

### 3.4 Fixtures R1–R6 (S10-QA-01: must pass **through the real API**, not only the service)

All against `TH-Default-VO`, $C^{orig} = 485{,}000{,}000.00$, threshold $\theta = 10.00$ *assumed
for these six fixtures only* so they remain the same numbers approval-workflow.md §8 already
accepted. If the human sets a different $\theta$ (§4.5), **only R4 moves**, for any
$\theta \ge 2.00$ — R1, R2, R2′, R3, R6 all sit below $1.75\%$ and R5's chain is empty before
escalation is ever evaluated. (Should the human pick $\theta < 1.75$, R1/R2/R2′/R3/R6 would each
gain the `Executive` step and this table must be recomputed rather than patched.)

Cumulative approved VOs before submission $\Sigma^{VO}_{prior} = 6{,}000{,}000.00$ for R1, R2, R3, R6.

| # | Input | $A^{route}$ | Escalation ratio | Escalation? | Expected chain (snapshot rows) | `TotalSteps` | Outcome |
| :-- | :-- | --: | --: | :--: | :-- | --: | :-- |
| **R1** | VO-018 `Add` **+2,400,000.00** | **2,400,000.00** | $\frac{6{,}000{,}000+2{,}400{,}000}{485{,}000{,}000}=1.7320\%$ | no | `(1, ProjectManager, q1)`, `(2, ProjectDirector, q1)` | **2** | `PendingApproval`, step 1/2 |
| **R2** | VO-015 `Deduct` **−800,000.00** | **800,000.00** | $\frac{6{,}000{,}000-800{,}000}{485{,}000{,}000}=1.0722\%$ | no | `(1, ProjectManager, q1)`, `(2, ProjectDirector, q1)` | **2** | `PendingApproval`, step 1/2 |
| **R2′** | *twin control:* `Add` **+800,000.00** | **800,000.00** | $\frac{6{,}000{,}000+800{,}000}{485{,}000{,}000}=1.4021\%$ | no | **identical to R2** | **2** | identical to R2 |
| **R3** | `Add` **+300,000.00** | **300,000.00** | $1.2990\%$ | no | `(1, ProjectManager, q1)` | **1** | `PendingApproval`, step 1/1 |
| **R4** | `Add` **+3,200,000.00**, $\Sigma^{VO}_{prior} = 46{,}000{,}000.00$ | **3,200,000.00** | $\frac{46{,}000{,}000+3{,}200{,}000}{485{,}000{,}000}=\mathbf{10.1443\%} > 10.00$ | **yes** | `(1, ProjectManager, q1)`, `(2, ProjectDirector, q1)`, **`(3, Executive, q1)`** | **3** | `PendingApproval`, step 1/3; `EscalationApplied = true` |
| **R5** | `Add` **+50,000.00** against `TH-Gap-VO` | **50,000.00** | n/a (chain empty before escalation) | n/a | **none** | 0 | **422 `ApprovalPolicyGap`**; VO stays `Draft`; no `ApprovalAction`; **must not auto-approve** |
| **R6** | `Add` **+500,000.00** *exactly* | **500,000.00** | $1.3402\%$ | no | `(1, ProjectManager, q1)`, `(2, ProjectDirector, q1)` | **2** | proves `MinAmount` inclusive / `MaxAmount` exclusive |

**R2 is the load-bearing one.** Assert three things, not one: (i) $A^{route} = 800{,}000.00$, a
*positive* number; (ii) the chain is byte-identical to R2′'s; (iii) `Amount` on the persisted
document is still **−800,000.00** — the absolute value is a routing input only and must never be
written back onto the aggregate. A test that only checks the chain will pass over an
implementation that silently stores `Math.Abs(Amount)`, which would then make $BAC$ go **up** on a
Deduct VO in §5.

**R4 escalation detail.** The $[500\text{k}, 5\text{M})$ band gives `[PM, ProjectDirector]`; the
escalation appends `Executive` at `StepNo = 3` (last step's number + 1). Escalation only ever
**appends to an already non-empty chain** — it can never rescue an empty one (R5 must stay a 422
even if the ratio is 90%). If `Executive` were already in the banded chain (as it is in the
$\ge 5\text{M}$ band), escalation is a no-op and `EscalationApplied = false`.

⚠ **H-01 inheritance, still live for Sprint 10.** The escalation step is *synthesised* — there is
no `ApprovalPolicyRule` row behind it. It is representable **only** because the H-01 fix snapshots
the resolved chain onto the document. Any implementation that re-derives "what does step 3 require"
from the pinned policy at decision time will strand every escalated VO on a 500/409. R4 is the
fixture that would have masked this in Sprint 9 (TH-Default-VO happens to own a StepNo 3 rule in a
different band) — S10-QA-01 must therefore **also** run R4 against a policy whose banded chain is
2 steps and which owns **no** StepNo 3 rule at all, and assert the `Executive` approval succeeds.

---

## 4. Cumulative-VO escalation — and R-04, which is **not** decided here

> `docs/10.` §11 **R-04**: *"เกณฑ์ escalation VO สะสม (10% ใช่หรือไม่ / reset เมื่อแก้สัญญาหรือไม่) …
> ทำเป็น config ต่อ policy (`CumulativeVoEscalationPct`) default 10.00 ไม่ hardcode; **ยืนยันกับมนุษย์ก่อน
> Sprint 10**."*

The human has been asked and has not yet answered. **This document sets no default.** Everything
below is written so that both answers to (a) and both answers to (b) are implementable by flipping
configuration, with the divergence made testable by fixtures V-4 and V-5.

### 4.1 The rule, parameterised

Let $\theta =$ `ApprovalPolicy.CumulativeVoEscalationPct` (`decimal(5,2)`, **nullable**) and
$r =$ `ApprovalPolicy.CumulativeVoEscalationRole`. At `Submit`, after the banded chain is resolved
and only if it is non-empty:

$$
\Phi \;=\; \frac{\Sigma^{VO} + f(A)}{C^{esc}} \times 100
\qquad\text{escalate} \iff \theta \ne \texttt{NULL} \;\wedge\; \Phi > \theta
$$

and, if escalating and $r$ is not already a role in the chain, append
$(\textit{StepNo}_{\max}+1,\ r,\ q{=}1)$.

Three things in that formula are **not** obvious and each materially changes when the threshold
trips. They are §4.2 ($f$ and $\Sigma^{VO}$), §4.3 ($C^{esc}$) and §4.4 (the reset).

### 4.2 The numerator — what is summed, and with what sign

$$\Sigma^{VO} \;=\; \sum_{v \in \mathcal{V}} f(A_v) \qquad
\mathcal{V} = \{\,v : v.\textit{ProjectId} = P,\ v.\textit{Status} = \texttt{Approved},\ v \in \mathcal{W}\,\}$$

where $\mathcal{W}$ is the reset window (§4.4) and $f$ is one of:

| Option | $f(x)$ | Reading | Trips… |
| :-- | :-- | :-- | :-- |
| **N-1 — net signed** *(recommended)* | $x$ | "how far has the **contract sum** drifted from what was originally sanctioned?" | least often |
| **N-2 — gross absolute** | $\lvert x \rvert$ | "how much of the contract has been **re-scoped**, in either direction?" | most often |
| **N-3 — additions only** | $\max(0, x)$ | literal reading of "งานเพิ่มเกิน X% ต้องขออนุมัติจาก…" and of FIDIC 13.1's "Variations … increase" | middle |

**Recommendation: N-1**, on three grounds. (i) The escalation's *purpose* is to make someone
re-sanction the contract sum, and the contract sum is a net quantity: $+10\text{M}$ followed by
$-10\text{M}$ leaves it exactly where it started and there is nothing to sanction. (ii) It is
consistent with the effect the approval actually has — $C^{cur} \mathrel{+}= A$ signed (§5.1); a
control should measure the same quantity it protects. (iii) Industry precedent: FIDIC Red Book 4th
ed. Sub-Clause 52.3's cumulative-variations test is expressly a **net** test (additions less
deductions) measured against the *Effective Contract Price*. (That clause was dropped in the 1999
and 2017 editions, so it is a reference point rather than an authority — the governing text is the
client's own contract.)

**Counter-argument the human must weigh:** a re-measure that adds ฿45M of one trade and omits ฿45M
of another has re-scoped 18.6% of a ฿485M contract while netting to zero. Several DoA tables and
Thai committee practice track งานเพิ่ม and งานลด separately precisely because gross churn is itself
a governance event. If the client's DoA is worded on งานเพิ่ม alone, **N-3** is the correct reading,
not N-1.

**Pending VOs are excluded.** Only `Approved` counts, because (i) a pending VO may be rejected or
re-priced and the threshold must not depend on documents that may never exist, and (ii) two VOs in
flight would each see the other and both escalate. The VO **being submitted** *is* included (the
$f(A)$ term) — the question the control asks is "will approving *this one* push the contract past
the threshold?", which is a look-ahead by construction. The gap this leaves is closed in §4.7.

### 4.3 The denominator — and a defect waiting in the current code

$$C^{esc} \in \{\,C^{orig},\ C^{cur}\,\}$$

| Option | Value | Behaviour |
| :-- | :-- | :-- |
| **D-1 — original** *(recommended)* | $C^{orig}$, fixed at signature, moved only by a formal amendment | $\Phi$ is **monotone** in $\Sigma^{VO}$: the threshold is a fixed money line at $\frac{\theta}{100}C^{orig}$. |
| **D-2 — current** | $C^{cur} = C^{orig} + \Sigma^{VO}_{\text{approved}}$ | **Self-diluting.** Every approval raises the denominator, so each successive VO looks smaller and the trigger recedes. Under D-2 with $\theta = 10$ the ratio can never exceed $\frac{\Sigma}{C^{orig}+\Sigma}$, which approaches but never reaches 100% — but more importantly the control weakens exactly as the contract drifts furthest. |

**Recommendation: D-1.** A percentage-of-contract governance test whose denominator grows with the
thing it is measuring is not a control. The same reasoning is why FIDIC 4th ed. 52.3 defines an
"Effective Contract Price" (the accepted amount, excluding Provisional Sums and dayworks) rather
than using the running Contract Price.

⚠ **Concrete defect risk in shipped code.** `ApprovalRoutingService.cs:66-70` divides by
`request.ContractValue`, and the only project-level field that exists is `Project.ContractValue`,
which payment-retention.md §1 defines as *"contract value **incl. approved VOs**"*. Wiring
`request.ContractValue = Project.ContractValue` therefore silently implements **D-2**. There is no
$C^{orig}$ field in the model today.

**Required:** add `Project.OriginalContractValue decimal(18,2)` (immutable except by a
`ContractAmendment`, §4.4), default it to `ContractValue` at project creation, and feed **that**
into the escalation. `Project.ContractValue` stays exactly as-is as the retention-cap and
advance-rate base. `system-architect`/`database-engineer`: this is a S10-DB-01 addition.

**And note what R4 does *not* settle.** R4's stated 10.14% is reproducible from
$49{,}200{,}000 / 485{,}000{,}000$ — but approval-workflow.md §8 never says whether its
"$ContractValue = 485{,}000{,}000$" is the original or the current figure, and both readings can
produce 10.14%:

| Reading of the fixture's 485,000,000.00 | D-1 gives | D-2 gives |
| :-- | --: | --: |
| **A** — it is $C^{orig}$ (so $C^{cur} = 531{,}000{,}000$) | $\frac{49{,}200{,}000}{485{,}000{,}000} = \mathbf{10.1443\%}$ ✓ escalates | $\frac{49{,}200{,}000}{531{,}000{,}000} = 9.2655\%$ ✗ **R4 fails** |
| **B** — it is $C^{cur}$ (so $C^{orig} = 439{,}000{,}000$) | $\frac{49{,}200{,}000}{439{,}000{,}000} = 11.2073\%$ — escalates, but the stated 10.14% is wrong | $\frac{49{,}200{,}000}{485{,}000{,}000} = \mathbf{10.1443\%}$ ✓ |

**Ruling:** fixture R4 is to be read as **Reading A + D-1** — $485{,}000{,}000.00$ is $C^{orig}$ —
because that is the only combination that reproduces *both* the stated chain and the stated
percentage. Encode it that way in `TH-Default-VO`'s fixture setup so the ambiguity cannot resurface.

### 4.4 The reset — R-04(b), both semantics defined

Model the amendment as a first-class immutable record (it is a signed legal instrument; it must be
evidence, not a field edit):

```
ContractAmendment                      -- append-only (IAppendOnly)
  Id, TenantId, ProjectId
  AmendmentNo            string        -- "สัญญาแก้ไขเพิ่มเติมฉบับที่ 2"
  ExecutedAt             DateTimeOffset
  EffectiveFrom          DateTimeOffset
  PriorContractValue     decimal(18,2) -- C^orig before
  NewContractValue       decimal(18,2) -- C^orig after
  AbsorbedVariationOrderIds  Guid[]    -- the approved VOs this amendment rolls up
  ExecutedByUserId, DocumentReference
```

| | **Option A — never resets** | **Option B — resets on formal amendment** |
| :-- | :-- | :-- |
| $\mathcal{W}$ (window) | all approved VOs for the project, for the life of the project | approved VOs **not listed in any `AbsorbedVariationOrderIds`** |
| $C^{esc}$ | $C^{orig}$ as first signed, forever | the latest amendment's `NewContractValue` |
| On amendment | `Project.OriginalContractValue` unchanged; the amendment still moves `ContractValue` and hence the retention base | `Project.OriginalContractValue ← NewContractValue`; counter effectively restarts at the residual |
| Character | maximally conservative; monotone; trivially auditable | matches commercial reality — the amendment **is** the sanction the escalation was demanding |
| Failure mode | once crossed, **every** subsequent VO carries the Executive step forever. In practice this produces rubber-stamping and the control decays to noise. | a **governance-laundering vector**: an organisation that can execute amendments cheaply can reset the counter to stay under the threshold |
| Mitigation | none needed | the amendment must itself be a higher-authority, separately-audited act; the reset must be recorded with the prior counter value (`ContractAmendment` above does this) |

**Use the absorbed-set formulation, not a date cut-off.** A VO approved *after* an amendment's
`EffectiveFrom` but nonetheless absorbed by it (common — the amendment is drafted around VOs still
in flight) would be double-counted by a date rule. Date-based windowing is an acceptable fallback
only if `AbsorbedVariationOrderIds` cannot be captured.

Configuration field required either way:
`ApprovalPolicy.ContractAmendmentResetsCumulativeVo bit` — **no default set here.**

### 4.5 Open — do not guess

| R-04 sub-question | Field | Status |
| :-- | :-- | :-- |
| (a) Is $\theta = 10.00$ the right default for this organisation? | `ApprovalPolicy.CumulativeVoEscalationPct decimal(5,2) NULL` | **PENDING — no default written.** The field must be **nullable**, and `NULL` must mean *"no cumulative escalation configured"* (not *"10"*), so an unanswered question cannot silently become a policy. |
| (b) Does the counter reset on formal contract amendment? | `ApprovalPolicy.ContractAmendmentResetsCumulativeVo bit` | **PENDING — both semantics specified above; neither selected.** |
| (c) Numerator: N-1 net / N-2 gross / N-3 additions-only? | `ApprovalPolicy.CumulativeVoBasis enum` | **PENDING — N-1 recommended.** Raised by this document; not in R-04's original wording, but it changes the answer more than (a) does (see V-4). |
| (d) Denominator: D-1 original / D-2 current? | needs `Project.OriginalContractValue` | **PENDING — D-1 recommended and required by R4's own arithmetic** (§4.3). |

Until (a) is answered, `ApprovalPolicySeeder` must seed `CumulativeVoEscalationPct = NULL` for VO
policies, and S10-BE-02 must treat `NULL` as "skip the escalation test entirely". Seeding 10.00 as
a placeholder would make a guess indistinguishable from a decision in production data — exactly
the failure mode M-04 describes for the `Guid.Empty` policy fallback.

### 4.6 Determinism, boundaries and division-by-zero

| Condition | Behaviour |
| :-- | :-- |
| $\theta = \texttt{NULL}$ | No escalation test. $C^{esc}$ is irrelevant and must not be read. |
| $\theta \ne \texttt{NULL} \wedge C^{esc} \le 0$ | **422 `ContractValueNotConfigured`; submission blocked.** Never divide; and never *silently skip*. `ApprovalRoutingService.cs:66` today skips escalation when `ContractValue <= 0`, which is a silent bypass of a governance control on a misconfigured project — **fix in S10-BE-02.** |
| Comparison operator | **strict `>`**, matching `MaxAmount`'s exclusivity. Exactly $\theta$ does **not** escalate (fixture V-5a). |
| Rounding of $\Phi$ | Compare at **full `decimal` precision**. Round to `decimal(5,2)` only for display/response. Comparing the rounded value would make $\Phi = 10.0040\%$ fail to escalate because it displays as `10.00` (fixture V-5b). |
| Chain empty before escalation | escalation is skipped; the result is 422 `ApprovalPolicyGap`. Escalation never creates a chain. |
| $r$ already in the banded chain | no-op; `EscalationApplied = false`; `TotalSteps` unchanged. |
| $r = \texttt{NULL}$ while $\theta \ne \texttt{NULL}$ | **Policy configuration error — reject at policy save**, 400 naming the field. Do not silently disable the escalation at runtime. |

### 4.7 The escalation-bypass race (S10-SEC-01 hunts for exactly this)

> S10-SEC-01 DoD: *"…ไม่มีเส้นทางข้าม escalation"* — there must be no path around escalation.

There is one, and it needs no attacker:

Two VOs are submitted while $\Sigma^{VO}$ is below the line. Each, evaluated alone at its own
submission, is under $\theta$, so neither collects the `Executive` step. Both are then approved.
The contract crosses the threshold **with no Executive signature anywhere in the audit trail.**

**Ruling — re-evaluate at the final approving step, and fail closed:**

Immediately before the transition to `Approved` (i.e. as a guard on the final `Approve` that would
exhaust the chain), recompute $\Phi$ against the *then-current* $\Sigma^{VO}$. If $\Phi > \theta$
and $r$ is **not** present in this document's snapshotted chain:

- **block with 409 `VoEscalationThresholdCrossedSinceSubmission`**, naming $\Phi$, $\theta$ and the
  missing role in the ProblemDetails;
- the remedy is `ReturnForRevision` → resubmit, which re-resolves the chain (§5.3 step 8) and picks
  up the `Executive` step.

**Do not "just append the step to the in-flight chain."** Mutating a pinned, snapshotted chain
mid-flight is precisely the bug class the H-01 fix eliminated, and it would break the
`INeverModified` guarantee on the snapshot rows (N-01). Fail closed and re-route.

Check it only at the **final** step, not on every vote — earlier steps would thrash, and the
threshold only matters at the moment the effect lands.

### 4.8 Escalation fixtures V-4 … V-6

All against `TH-Default-VO`, $C^{orig} = 485{,}000{,}000.00$, $\theta = 10.00$ for illustration.

**V-4 — the numerator question, made testable.** Prior approved VOs: `Add +52,000,000.00` and
`Deduct −8,000,000.00`. New VO: `Add +3,200,000.00`.

| Basis | $\Sigma^{VO} + f(A)$ | $\Phi$ | Escalates? | Chain |
| :-- | --: | --: | :--: | :-- |
| **N-1** net signed | 44,000,000.00 + 3,200,000.00 = **47,200,000.00** | **9.7320%** | **no** | `[PM, ProjectDirector]` |
| **N-2** gross absolute | 60,000,000.00 + 3,200,000.00 = **63,200,000.00** | **13.0309%** | **yes** | `[PM, ProjectDirector, Executive]` |
| **N-3** additions only | 52,000,000.00 + 3,200,000.00 = **55,200,000.00** | **11.3814%** | **yes** | `[PM, ProjectDirector, Executive]` |

One fixture, three verdicts. Whichever the human picks, the other two become negative tests.

**V-5a — boundary, strict `>`.** $\Sigma^{VO}_{prior} = 45{,}300{,}000.00$, VO `Add +3,200,000.00`
→ numerator **48,500,000.00** → $\Phi = 48{,}500{,}000/485{,}000{,}000 = \mathbf{10.000000\%}$
→ **not** $> 10.00$ → **no escalation**, chain `[PM, ProjectDirector]`.

**V-5b — rounding must not decide.** $\Sigma^{VO}_{prior} = 45{,}319{,}400.00$, VO
`Add +3,200,000.00` → numerator **48,519,400.00** → $\Phi = \mathbf{10.004000\%}$.
Unrounded $> 10.00$ → **escalates**, chain `[PM, ProjectDirector, Executive]`.
An implementation that rounds $\Phi$ to `decimal(5,2)` before comparing sees `10.00` and
**wrongly does not escalate**. This fixture exists solely to catch that.

**V-6 — the bypass race (§4.7).** $\Sigma^{VO}_{prior} = 44{,}000{,}000.00$.

| Step | Event | $\Phi$ | Expected |
| :-- | :-- | --: | :-- |
| 1 | VO-A `Add +2,400,000.00` submitted | $\frac{46{,}400{,}000}{485{,}000{,}000}=9.5670\%$ | chain `[PM, PD]`, 2 steps, no escalation |
| 2 | VO-B `Add +2,300,000.00` submitted | $\frac{46{,}300{,}000}{485{,}000{,}000}=9.5464\%$ | chain `[PM, PD]`, 2 steps, no escalation |
| 3 | VO-A fully approved | — | `Approved`; $\Sigma^{VO} = 46{,}400{,}000.00$ |
| 4 | VO-B: PM approves (step 1→2) | not checked at non-final steps | `PendingApproval` 2/2 |
| 5 | VO-B: PD approves — **final step** | re-check: $\frac{46{,}400{,}000+2{,}300{,}000}{485{,}000{,}000}=\mathbf{10.0412\%} > 10.00$ | **409 `VoEscalationThresholdCrossedSinceSubmission`**; VO-B stays `PendingApproval` 2/2; **no** BAC change |
| 6 | PD returns VO-B for revision, QS resubmits at the same +2,300,000.00 | $10.0412\%$ | $\rho = 2$; chain re-resolves to `[PM, PD, **Executive**]`, 3 steps |

Without step 5's guard, VO-B is approved by `[PM, PD]` alone and the contract crosses 10% with no
Executive signature. **This is the escalation bypass; assert its absence.**

---

## 5. Effects of `Approved` on BAC, ContractValue and everything downstream

> S10-BE-03 DoD: *"`Approved` → `BAC += Amount` (มีเครื่องหมาย) และ `ContractValue` ขยับเท่ากัน → เพดาน
> retention/ฐาน advance เปลี่ยนตาม; VO ลบ **ห้าม** เรียกคืน retention อัตโนมัติ (`max(0,·)`); กิจกรรมใหม่
> schedulable + trigger คำนวณ CPM; จุด S-Curve ก่อน `ApprovedAt` ไม่ถูกเขียนทับ."*

### 5.1 The two money moves

$$BAC_{new} = BAC_{old} + A \qquad\qquad C^{cur}_{new} = C^{cur}_{old} + A \qquad\qquad C^{orig}\ \textbf{unchanged}$$

both **signed** — a `Deduct` VO carries $A < 0$ and both figures go down. $C^{orig}$ moves only via
a `ContractAmendment` (§4.4).

**Guard — non-negativity.** `Project.BAC` and `Project.ContractValue` are both
`MoneyGuard.EnsureNonNegative`. A Deduct VO large enough to drive either below zero must fail the
approval with **422 `VoWouldMakeContractValueNegative`** (and be *warned* at submission, so the
error is not first discovered by the final approver after a full chain has signed). Never clamp
silently — a clamp would break the invariant of §5.2.

**Atomicity.** The status change, the `ApprovalAction` row, the `Project` updates, the scope-payload
application and the audit rows commit in **one** `SaveChanges` on the same scoped `DbContext` —
the same discipline sprint-09.md §5 verified for certification. An `Approved` VO whose BAC move did
not land must be impossible.

### 5.2 The invariant everyone forgets: $BAC$ is a scalar, $EV$ and $PV$ are not

`Project.BAC` is a stored scalar, but

$$PV(t) = \sum_i BudgetCost_i \cdot plannedPct_i(t) \qquad EV = \sum_i BudgetCost_i \cdot \frac{ProgressPct_i}{100}$$

are computed from `Activity.BudgetCost` (`EvmComputationService.cs:52-54`). Moving `Project.BAC`
by $A$ **without** moving the activity budgets by the same amount silently breaks every
BAC-relative metric: $EV$ can never reach $BAC$, so $TCPI$, $VAC$ and reported % complete are wrong
forever, and no test that only looks at `Project.BAC` will notice.

**Invariant (hard, checked at `Submit` and re-checked at `Approve`):**

$$\Delta B_{scope} \;=\; \sum_{\text{added activities}} BudgetCost \;-\; \sum_{\text{reduced activities}} \Delta BudgetCost \;=\; A$$

to the cent. Violation ⟹ **422 `VoScopeBudgetMismatch`**, naming both figures.

Two consequences for `system-architect`:

1. **`Activity.BudgetCost` is `EnsureNonNegative`** (`Activity.cs:83`), so a `Deduct` VO **cannot**
   be represented as a negative-budget activity. The scope payload must therefore be a **delta
   list** — `{ActivityId | new activity spec, BudgetCostDelta}` — not merely a list of added
   activities.
2. **`Amount` (contract price) vs budget impact.** Today `Project.ContractValue` defaults to `BAC`,
   so one number moving both is coherent. The moment a tenant sets $C^{cur} \ne BAC$, a single
   `Amount` cannot correctly move both (the price includes margin; the budget is cost).
   - **Option M-1 (Sprint 10, recommended):** one `Amount` moves both. Matches the DoD literally.
   - **Option M-2 (migration path):** add `BudgetCostImpact decimal(18,2)`, defaulting to `Amount`;
     $BAC \mathrel{+}= BudgetCostImpact$, $C^{cur} \mathrel{+}= Amount$.
   - **Routing always uses `Amount`**, never `BudgetCostImpact` — DoA authority is about
     contractual commitment, not internal cost.
   This is the same question as payment-retention.md §7 Q2 ("`ContractValue` vs `BAC`: ever
   distinct?"), still open with the human. Flagged in §9.

**Omission of already-executed work.** A `Deduct` VO that reduces the budget of an activity with
$ProgressPct > 0$ retroactively destroys earned value that was legitimately earned — $EV$ drops,
$SV$ and $CV$ go sharply negative, and if the work was already certified, the S-Curve contradicts
the payment record. **Rule:** an omission may only reduce budget on **remaining** scope. If the
variation genuinely omits completed work (demolish-and-rebuild-differently), the correct modelling
is to close the executed activity at its as-built budget and add the replacement as new activities,
with `Amount` netting the two. Raise validation warning **`VoOmitsExecutedScope`** (warn at Draft,
**block at approval**) when a negative `BudgetCostDelta` targets an activity with
$ProgressPct > 0$.

### 5.3 Retention ceiling and advance base move — with two hard limits

From payment-retention.md §1, unchanged:

$$R_k = \max\!\Big(0,\ \min\big(\tfrac{r}{100}G_k,\ \ R^{max} - R^{cum}_{k-1}\big)\Big),
\qquad R^{max} = \tfrac{c}{100}\,C^{cur}\ \ (=\infty \text{ if } c \text{ is null})$$

$$D_k = \max\!\Big(0,\ \min\big(\tfrac{a}{100}G_k,\ \ A^{adv} - D^{cum}_{k-1},\ \ G_k - R_k\big)\Big)$$

**Limit 1 — a negative VO must never auto-refund retention.** When $C^{cur}$ drops, $R^{max}$ drops
with it and the headroom $R^{max}-R^{cum}_{k-1}$ can go negative. The **outer $\max(0,\cdot)$
already yields $R_k = 0.00$** — the formula is correct as written and needs no change; what must be
guaranteed is that nothing *else* reaches for the excess:

- A VO approval writes **zero `ProjectFinanceLedger` rows** (§6.4). No `Release`, no negative
  `Retention` accrual, no `Adjustment`. Not conditionally — zero.
- The over-held excess is released only through the ordinary $\text{Release}_1$ /
  $\text{Release}_2$ mechanism (payment-retention.md §4) or an explicit, separately-approved
  `Adjustment` entry.
- QA assertion: after a Deduct VO, $\texttt{SUM}(\textit{Amount})$ over
  `ProjectFinanceLedger WHERE Category = Retention` is **bit-identical** to its value before.

**Limit 2 — `AdvanceAmountPaid` is *not* recomputed.** payment-retention.md §1 says $A^{adv}$
(`Project.AdvanceAmountPaid`) *defaults* to $\tfrac{a}{100}C$. That is a **creation-time default,
not a formula.** $A^{adv}$ is the cash the employer actually disbursed; a later VO does not
retroactively increase money that was never handed over. Recomputing it silently over-recovers from
the contractor (worked, with the exact loss, in V-7). If a contract amendment provides for an
*additional* advance, that is a new `ProjectFinanceLedger` `Disbursement` entry plus an update to
`AdvanceAmountPaid` — a deliberate, audited act, never a side effect of VO approval.

### 5.4 Fixtures V-7 and V-8

**V-7 — `Add` VO raises the ceiling; the advance base must not move.**
Given: $C^{cur}_{old} = 485{,}000{,}000.00$; $c = 5.00$; $r = 5.00$; $a = 10.00$;
$A^{adv} = 48{,}500{,}000.00$ (actually disbursed); $R^{cum}_{k-1} = 24{,}100{,}000.00$;
$D^{cum}_{k-1} = 48{,}100{,}000.00$.
VO-030 `Add` **+20,000,000.00** approved ⟹ $C^{cur}_{new} = 505{,}000{,}000.00$,
$BAC \mathrel{+}= 20{,}000{,}000.00$.
Next certificate: $G_k = 12{,}000{,}000.00$.

| | Before the VO | After the VO |
| :-- | --: | --: |
| $R^{max} = \tfrac{5}{100}C^{cur}$ | 24,250,000.00 | **25,250,000.00** |
| headroom $R^{max}-R^{cum}_{k-1}$ | 150,000.00 | **1,150,000.00** |
| $\tfrac{r}{100}G_k$ | 600,000.00 | 600,000.00 |
| $R_k$ | 150,000.00 *(cap-bound)* | **600,000.00** *(rate-bound)* |
| $A^{adv}$ | 48,500,000.00 | **48,500,000.00 — unchanged** |
| outstanding advance | 400,000.00 | 400,000.00 |
| $D_k=\min(1{,}200{,}000,\ 400{,}000,\ 11{,}400{,}000)$ | — | **400,000.00** |
| $N_k = G_k - R_k - D_k$ | — | **11,000,000.00** |

**Defect counterfactual (the reason this fixture exists):** if the implementation recomputes
$A^{adv} \leftarrow \tfrac{10}{100} \times 505{,}000{,}000 = 50{,}500{,}000.00$, the outstanding
advance becomes 2,400,000.00, $D_k$ becomes 1,200,000.00 and $N_k$ becomes **10,200,000.00** — the
contractor is short-paid **800,000.00** against an advance the employer never disbursed.

**V-8 — `Deduct` VO drops the ceiling below what is already held; retention is *not* refunded.**
Given: $C^{cur}_{old} = 100{,}000{,}000.00$; $c = 5.00 \Rightarrow R^{max}_{old} = 5{,}000{,}000.00$;
$R^{cum}_{k-1} = 4{,}600{,}000.00$; $r = 10.00$; $a = 10.00$; $A^{adv} = 10{,}000{,}000.00$;
$D^{cum}_{k-1} = 4{,}000{,}000.00$.
VO-031 `Deduct` **−20,000,000.00** approved ⟹ $C^{cur}_{new} = 80{,}000{,}000.00$,
$BAC \mathrel{-}= 20{,}000{,}000.00$.
Next certificate: $G_k = 3{,}000{,}000.00$.

- $R^{max}_{new} = 4{,}000{,}000.00$; headroom $= 4{,}000{,}000 - 4{,}600{,}000 = -600{,}000.00$
- $R_k = \max\big(0,\ \min(300{,}000.00,\ -600{,}000.00)\big) = \max(0,\ -600{,}000.00) = \mathbf{0.00}$
- $D_k = \min(300{,}000.00,\ 6{,}000{,}000.00,\ 3{,}000{,}000.00) = \mathbf{300{,}000.00}$
- $N_k = 3{,}000{,}000.00 - 0.00 - 300{,}000.00 = \mathbf{2{,}700{,}000.00}$
- $R^{cum}$ **stays 4,600,000.00**. The 600,000.00 now held over the new ceiling is **not**
  refunded here and **no ledger row is written by the VO**. It clears at $\text{Release}_1$/
  $\text{Release}_2$.
- Negative-guard check: $C^{cur}_{new} = 80{,}000{,}000.00 > 0$ and $BAC_{new} > 0$, so the
  approval proceeds. A `Deduct −120,000,000.00` on the same project would fail with
  **422 `VoWouldMakeContractValueNegative`**.

### 5.5 The rebaseline boundary — what must not be rewritten, and how

> S10-QA-02 DoD: *"หลังอนุมัติ VO: BAC/EAC/VAC ใหม่ถูกต้องทุก variant; `EvmPeriodSnapshot` ก่อนหน้าไม่
> เปลี่ยนแม้แต่แถวเดียว."*

Three separate mechanisms are needed; the DoD's "S-Curve points before `ApprovedAt` are not
overwritten" is not one guarantee but three.

**(a) `EvmPeriodSnapshot` rows — immutable, ADR-0009.** Each snapshot carries its own `Bac`,
`EacVariant`, `PerformanceFactor`, `Eac`, `Etc`, `Vac`. A VO approval touches none of them; the
`AppendOnlyGuardInterceptor` (`IAppendOnly`) blocks `Modified`/`Deleted` at `SavingChanges`.
QA assertion: capture every snapshot row's full column set before approval and assert
byte-equality after — including `RowVersion`/`CreatedAt`, not just `Bac`.

**(b) The $PV$ series — the VO's scope contributes nothing before $t_A$.**

$$plannedPct_i(t) = 0 \quad \forall\, t < t_A,\ \forall\, i \in \text{scope}(v)$$

The VO's whole budget is time-phased over $[\max(t_A,\ PlannedStart_i),\ PlannedFinish_i]$. If the
added activities were allowed to time-phase from a planned start that predates approval, the
historical PV curve would move retroactively and the S-Curve's "plan" line would change shape
behind the data date. Where the instruction genuinely predates the approval, the *instruction date*
is recorded on the VO for the claim record but does **not** move the PV origin.

**(c) $BAC(t)$ must be a function of $t$, not a scalar read.** This is the subtle one and the
easiest to miss.

$$BAC(t) \;=\; BAC^{orig} \;+\!\!\sum_{\substack{v:\ v.\textit{Status}=\texttt{Approved}\\ v.\textit{ApprovedAt}\ \le\ t}}\!\! A_v$$

Without it: a closed period before $t_A$ reads its frozen `EvmPeriodSnapshot.Bac` and returns the
**old** BAC, while an *open* historical data date (no snapshot) recomputes live from
`Project.BAC` and returns the **new** BAC. The same data date then yields two different answers
depending on whether someone happened to close that period — and the DoD's guarantee holds only by
accident of snapshot coverage.

**Required:** add `Project.OriginalBac decimal(18,2)`; compute $BAC(t)$ for every historical read;
keep `Project.BAC` as the maintained denormalisation of $BAC(\text{now})$ with an invariant test
$\textit{Project.BAC} = BAC(\text{now})$. `EvmPeriodSnapshot.Bac` then becomes a cache of the same
function, and snapshot and live recomputation agree by construction rather than by luck.

Recording `BacBefore`/`BacAfter`/`ContractValueBefore`/`ContractValueAfter` on the VO at approval
(§2.4) makes this reconstructible and auditable years later without replaying every VO in order.

**(d) Presentation, not arithmetic.** The step in $BAC$ at $t_A$ is real and must be **labelled,
never smoothed**. The EVM/S-Curve response should carry VO annotation markers
(`{ voNumber, approvedAt, amount, bacBefore, bacAfter }`) so S10-FE-02 can draw the boundary. Two
user-visible surprises live here and both are correct behaviour, not bugs (see §5.7).

### 5.6 What a VO approval does **not** change

| Quantity | Effect | Why |
| :-- | :-- | :-- |
| $AC$ (ACWP) | **none** | ADR-0013: $AC$ is the append-only `ActualCostEntry` ledger on an accrual basis. A VO approval incurs no cost. |
| $EV$ | **none at $t_A$** | added activities start at 0% progress; omissions may not touch executed scope (§5.2). |
| $PV(t)$ for $t < t_A$ | **none** | §5.5(b). |
| $SV,\ CV,\ SPI,\ CPI$ | **none, ever** | none of them contains $BAC$. See §5.7 — this continuity is the property to assert. |
| `ProjectFinanceLedger` | **zero rows written** | §5.3 Limit 1. |
| $R^{cum},\ D^{cum},\ A^{adv}$ | **none** | §5.3 Limit 2. |
| In-flight Payment Certificates | **none** | §6. |
| `EvmPeriodSnapshot` (any row) | **none** | ADR-0009 / §5.5(a). |

What it **does** additionally trigger: the VO's activities become schedulable ⟹ **CPM must be
re-run** (S10-BE-03, depends on S7-BE-05); and an approved `TimeImpactDays > 0` extends the
contract finish date, which feeds the Sprint 11 EOT module.

### 5.7 Worked example V-9 — every EAC variant across the rebaseline boundary

This is the S10-QA-02 fixture. Data date **2026-07-15**, evaluated immediately **before** and
immediately **after** VO-021 `Add` **+10,000,000.00** is approved at that instant. The VO's scope
is planned to start 2026-08-01, so $PV$ and $EV$ at the data date are unchanged by construction —
which isolates the BAC effect exactly.

**Inputs.** $BAC_{old} = 100{,}000{,}000.00$, $BAC_{new} = 110{,}000{,}000.00$;
$PV = 40{,}000{,}000.00$; $EV = 36{,}000{,}000.00$; $AC = 40{,}000{,}000.00$ (all three identical
before and after). $ETC_{manual} = 70{,}000{,}000.00$; $PF_c = 1.20$.

**Core metrics — unchanged, and that is the point.**

| Metric | Before | After |
| :-- | --: | --: |
| $SV = EV - PV$ | −4,000,000.00 | **−4,000,000.00** |
| $CV = EV - AC$ | −4,000,000.00 | **−4,000,000.00** |
| $SPI = EV/PV$ | 0.900000 | **0.900000** |
| $CPI = EV/AC$ | 0.900000 | **0.900000** |

> **Assert this.** $SV$, $CV$, $SPI$, $CPI$ contain no $BAC$ term, so performance measurement is
> **continuous across the rebaseline boundary**. A VO can never make a project look like it is
> performing better or worse than it was one second earlier. Any implementation where these four
> move on approval has a defect.

**Forecasts — every one of them steps.** $BAC - EV$: 64,000,000.00 → 74,000,000.00.

| Variant | $PF$ | $ETC$ before | $ETC$ after | $EAC$ before | $EAC$ after | $VAC$ before | $VAC$ after |
| :-- | --: | --: | --: | --: | --: | --: | --: |
| `CpiBased` | $1/0.9 = 1.111111$ | 71,111,111.11 | 82,222,222.22 | **111,111,111.11** | **122,222,222.22** | −11,111,111.11 | **−12,222,222.22** |
| `Atypical` | 1 | 64,000,000.00 | 74,000,000.00 | 104,000,000.00 | 114,000,000.00 | −4,000,000.00 | **−4,000,000.00** |
| `CpiSpiBased` | $1/0.81 = 1.234568$ | 79,012,345.68 | 91,358,024.69 | 119,012,345.68 | 131,358,024.69 | −19,012,345.68 | **−21,358,024.69** |
| `BottomUpEtc` | — | 70,000,000.00 | 70,000,000.00 *(stale)* | 110,000,000.00 | **110,000,000.00** | −10,000,000.00 | **0.00** ⚠ |
| `CustomPf` | 1.20 | 76,800,000.00 | 88,800,000.00 | 116,800,000.00 | 128,800,000.00 | −16,800,000.00 | **−18,800,000.00** |

Sanity checks: $BAC_{new}/CPI = 110{,}000{,}000/0.9 = 122{,}222{,}222.22$ ✓ equals `CpiBased` EAC.
`Atypical`'s $VAC = CV$ invariant **survives the rebaseline** ✓ ($-4{,}000{,}000.00$ both sides).
$TCPI_{EAC}$ against `CpiBased` $= 74{,}000{,}000/82{,}222{,}222.22 = 0.900000 = CPI$ ✓ — the
identity from evm-formulas.md holds after the move too.

$TCPI_{BAC}$: before $= 64{,}000{,}000/60{,}000{,}000 = \mathbf{1.066667}$;
after $= 74{,}000{,}000/70{,}000{,}000 = \mathbf{1.057143}$ — the added budget gives headroom, so
the efficiency required to finish on budget **improves**. Correct.

**Closed-form deltas — worth asserting directly, they catch sign errors instantly.** Since
$VAC = BAC - AC - PF\,(BAC - EV)$ for every $PF$-based variant, $\partial VAC/\partial BAC = 1 - PF$:

$$\Delta EAC = PF \cdot \Delta BAC \qquad\qquad \Delta VAC = (1 - PF)\,\Delta BAC$$

| Variant | $\Delta EAC$ | $\Delta VAC$ | check |
| :-- | --: | --: | :-- |
| `CpiBased` | +11,111,111.11 | −1,111,111.11 | $(1-1.111111)\times10\text{M}$ ✓ |
| `Atypical` | +10,000,000.00 | 0.00 | $PF = 1$ ✓ |
| `CpiSpiBased` | +12,345,679.01 | −2,345,679.01 | $(1-1.234568)\times10\text{M}$ ✓ |
| `CustomPf` | +12,000,000.00 | −2,000,000.00 | $(1-1.20)\times10\text{M}$ ✓ |
| `BottomUpEtc` | **0.00** | **+10,000,000.00** | $VAC = BAC - AC - ETC_{man} \Rightarrow \partial/\partial BAC = 1$ |

**Two things that will be reported as bugs and are not:**

1. **"The VO made my forecast overrun *worse*."** A fully-funded +10,000,000.00 VO worsens
   `CpiBased` $VAC$ by 1,111,111.11. This is correct and important: `CpiBased` assumes the new
   scope will also be delivered at $CPI = 0.90$, i.e. that 10,000,000.00 of budget will cost
   11,111,111.11. The UI must be able to explain this, because it is the single most likely
   support question after Sprint 10 ships.
2. **"% complete went backwards."** $EV/BAC$ drops from $36.00\%$ to
   $36{,}000{,}000/110{,}000{,}000 = \mathbf{32.73\%}$ with no physical regression. Correct — the
   denominator grew. Must be annotated on the S-Curve at $t_A$, not silently rendered as a dip.

**⚠ `BottomUpEtc` is actively dangerous here and needs a rule.** The manual ETC does not know about
the VO, so $EAC$ does not move while $VAC$ improves by the **full** +10,000,000.00 — a project that
was forecast 10,000,000.00 over budget now reports **exactly on budget**. A stale manual ETC turns
a VO into apparent good news.

> **Rule:** approving a VO **invalidates `Project.EacManualEtc`.** Stamp
> `Project.EacManualEtcStaleSince = ApprovedAt`. While stale, the EVM response must return
> `BottomUpEtc` with a data-quality warning **`ManualEtcPredatesBacChange`** (carrying the VO
> number and the BAC delta), and the frontend must render the tile in a warning state rather than
> as a clean figure. The value is cleared when a QS re-enters `EacManualEtc`. Do **not** silently
> auto-adjust the manual ETC by $A$ — a bottom-up estimate is a professional judgement, not an
> arithmetic series.

**Edge cases at the boundary** (compose with evm-formulas.md's edge matrix — all must still hold
after the BAC move):

| Case | Expected after approval |
| :-- | :-- |
| $AC = 0$, $EV > 0$ | `CpiBased`/`CpiSpiBased`/`Atypical` → `null`, reason `NoActualCost`. The larger BAC changes nothing. |
| $BAC_{new} - EV = 0$ (a `Deduct` VO brings BAC down to exactly $EV$) | short-circuit **before** dividing: $ETC = 0.00$, $EAC = AC$, every variant. Never evaluate $PF$. |
| $BAC_{new} < EV$ (a `Deduct` VO omits more than the remaining scope) | $ETC$ negative; compute and return as-is **plus** warning `EarnedValueExceedsBudget`. This is a strong signal that §5.2's `VoOmitsExecutedScope` rule was violated — cross-check. |
| $BAC_{new} = 0$ (whole contract omitted) | $BAC - EV = -EV$; with $EV = 0$ too → all null, reason `NotStarted`; do **not** return 0.00. |
| $BAC_{new} - AC = 0$ | $TCPI_{BAC}$ → `null`, render "—". |

---

## 6. Interaction with an in-flight Payment Certificate

**Scenario:** a VO reaches `Approved` while an IPC for the same project sits in `PendingApproval`
with one of two signatures already collected. Does that certificate's retention ceiling change
under it?

### 6.1 Ruling — **no. The certificate is not recomputed. Its figures are frozen.**

$R^{max}$, $R_k$, $D_k$ and $N_k$ on that certificate stay exactly as computed when it was last in
`Draft`. Three reasons, in descending order of force:

1. **Evidentiary.** The approvers who have already signed signed *specific figures*. Silently
   changing the numbers under a partially-signed certificate invalidates the signatures already
   collected. It is the money-field equivalent of re-routing a pinned approval chain — the same
   defect class as H-01, and the classic audit finding in payment systems.
2. **Structural.** The Sprint 9 freeze is by construction: `SetPeriodClaim` is the only writer of
   the five money fields and refuses unless `Status ∈ {NotDue, Draft}`
   (`PaymentCertificate.cs:511-519`), now backed at the persistence layer by the M-01 interceptor.
   A "recompute in flight" feature would have to *break* both guarantees. Do not.
3. **Contractual.** An IPC certifies the value of work executed in a defined period. The VO's
   effect belongs to the next certificate, whose gross will itself reflect the varied work.

The same answer applies to a `Certified`-but-unpaid certificate (immutable forever) and, trivially,
to `Paid`.

For an IPC still in **`Draft`**, nothing is auto-recomputed either — but `SetPeriodClaim` reads
`Project.ContractValue` live, so simply re-saving the draft picks up the new ceiling. The UI should
prompt the QS to do so.

### 6.2 But it must not be silent — mandatory disclosure

On a VO reaching `Approved`, the handler must find every IPC for that project with
`Status = PendingApproval` and, for each:

- write an `AuditLog` entry `VoApprovedWithCertificateInFlight` carrying **both** document ids, the
  VO amount, and $C^{cur}_{before} \to C^{cur}_{after}$;
- surface an advisory on the certificate's approval screen (Thai-first):
  *"เพดาน retention ของโครงการเปลี่ยนหลังจากใบรับรองนี้ถูกส่ง — ใบรับรองนี้ใช้ตัวเลข ณ วันที่ส่ง (VO-xxx,
  ContractValue A → B)"*.

The remedy available to any pending approver is the ordinary one: **`ReturnForRevision`** → the
certificate goes back to `Draft` ($\rho{+}1$, every collected approval voided), `SetPeriodClaim`
runs against the new $C^{cur}$, and resubmission re-resolves the chain (which may itself change,
since $G_k$ may have changed). That is the **only** legitimate route by which the new ceiling
reaches that certificate, and it correctly discards signatures given on the old figures.

### 6.3 Direction matters — only one direction can harm the contractor

| VO direction | Effect on the frozen certificate | Severity |
| :-- | :-- | :-- |
| **`Add`** — $C^{cur}$ ↑, $R^{max}$ ↑ | the frozen $R_k$ is now an **under**-deduction relative to the new ceiling: the employer withholds *less* than it could | Harmless. Self-correcting — the unused headroom simply carries to the next certificate. Advisory only. |
| **`Deduct`** — $C^{cur}$ ↓, $R^{max}$ ↓ | the frozen $R_k$ may now exceed the new headroom: the certificate **over-withholds** from the contractor | Real. And **not** self-correcting: the cap logic will give $R_k = 0.00$ on subsequent certificates but never returns the excess, which sits until $\text{Release}_1$. |

**Ruling for the `Deduct` case:** the certificate still is not recomputed (§6.1 is unconditional),
but the final approval must not be able to happen *unknowingly*. Guard the transition to
`Certified` with **409 `CertificateStaleAgainstContractValue`** unless the approve command carries
an explicit `acknowledgeStaleContractValue: true`, which is recorded verbatim in the
`ApprovalAction` comment. The approver's two options are then: acknowledge (money stays withheld,
recovered at release — the conservative posture the "never auto-refund" DoD already chose), or
`ReturnForRevision` and re-price.

This costs one boolean, moves no money, and leaves an audit trail showing the approver knew. The
lighter alternative (advisory only, no guard) is stated as an open question in §9 item 6, because
whether an employer may knowingly over-withhold for a period is a contract question, not a software
one.

### 6.4 Absolute rule

> **A Variation Order approval writes zero `ProjectFinanceLedger` rows. Not conditionally — zero.**

No accrual, no release, no adjustment, no recovery, in either direction. Every ledger row originates
from a certificate reaching `Certified`, a release event, or an explicit approved adjustment. This
is the strongest form of the DoD's "VO ลบ ห้ามเรียกคืน retention อัตโนมัติ" and it is trivially
testable: count rows before and after.

---

## 7. Business rules

### 7.1 Identity, numbering, supersession

1. `VoNumber` is unique per `(TenantId, ProjectId)`, assigned at creation in `Draft`, and
   **immutable in every state** — a returned-for-revision VO keeps its number; `RevisionNo`
   distinguishes revisions. Enforce with a unique index.
2. A superseding VO is **always a new record**, never an edit of a terminal one
   (approval-workflow.md §2). Correcting a wrongly-approved VO is a new, opposite-signed VO with
   `ReversesVariationOrderId` set. Both remain in the register; the net is what matters.
3. `Rejected` and `Cancelled` VOs are retained forever — a refused variation is claim evidence.
   No delete path, for any status.

### 7.2 Separation of duties

Adopt approval-workflow.md §6 in full, **as amended by §8 of this document**:

1. Creator and submitter may not approve any step unless the **pinned** policy's
   `AllowSelfApproval = 1` (default 0).
2. One human may not satisfy two steps of the same chain, keyed on `RevisionNo` so
   `ReturnForRevision` resets it (verified sound, sprint-09.md §5).
3. A step clears only when `QuorumCount` **distinct** users of that role approve it.
4. **New (§8):** a step terminates only when `QuorumCount` **distinct** users of that role reject
   it; and no actor may cast both an approval and a rejection on the same revision.

### 7.3 Validation at Draft / Submit

| Rule | Error |
| :-- | :-- |
| `Type` is derived from $\operatorname{sign}(A)$, never independently set | — (structural) |
| $A$ has at most 2 decimal places | 400 `AmountPrecision` |
| $\Delta B_{scope} = A$ exactly | 422 `VoScopeBudgetMismatch` |
| No negative `BudgetCostDelta` on an activity with $ProgressPct > 0$ | warn at Draft, **422 `VoOmitsExecutedScope`** at approval |
| $BAC + A \ge 0$ and $C^{cur} + A \ge 0$ | warn at Submit, **422 `VoWouldMakeContractValueNegative`** at approval |
| $A = 0$ **is permitted** — a time-only variation (EOT with no cost) or an equal-value scope swap is a real instrument. It routes on $A^{route} = 0.00$ (band $[0, 500\text{k})$ → `[PM]`), has **no** BAC/ContractValue effect, and still requires approval because it may carry time impact. | — |
| …but a VO with $A = 0$ **and** `TimeImpactDays = 0` **and** an empty scope payload is meaningless | 422 `EmptyVariation` |
| Comment mandatory on `Reject`, `ReturnForRevision` and (VO-specific) `Cancel` | 400 `CommentRequired` |
| Chain re-resolves on every resubmission (approval-workflow.md §5.3 step 8) | — (fixture V-10) |

### 7.4 Fixture V-10 — amount changed during revision re-routes the chain

Against `TH-Default-VO`. S10-SEC-01 tests this explicitly
(*"แก้จำนวนเงินแล้ว chain ถูก re-resolve"*).

| Step | Event | Expected |
| :-- | :-- | :-- |
| 1 | VO-022 `Draft`, $A = +400{,}000.00$, submitted | $A^{route} = 400{,}000.00$ → chain `[PM]`, `TotalSteps = 1`, $\rho = 1$ |
| 2 | PM returns for revision (comment mandatory) | `Draft`; $\rho = 2$; chain snapshot rows deleted (**count = 0**); `CurrentStepNo`/`TotalSteps`/policy pin/`LastVoteAt` cleared; money + scope unfrozen |
| 3 | QS re-prices to $A = +600{,}000.00$ (scope payload updated so $\Delta B_{scope} = 600{,}000.00$) and resubmits | $A^{route} = 600{,}000.00$ → chain **`[PM, ProjectDirector]`**, `TotalSteps = 2`, $\rho = 2$, approvals restart at step 1 |
| 4 | *Replay attempt:* an approver holding only the revision-1 chain's authority tries to act on a step that no longer exists | **403 `NotAuthorizedForApprovalStep`** — never a 500 |
| 5 | *Reverse direction:* instead re-price to $A = +300{,}000.00$ | chain **shrinks** to `[PM]`, `TotalSteps = 1` |
| 6 | *Escalation direction:* instead re-price to $A = +3{,}200{,}000.00$ with $\Sigma^{VO}_{prior} = 46{,}000{,}000.00$ | chain grows to `[PM, ProjectDirector, Executive]` (R4's chain), `TotalSteps = 3` |

### 7.5 Cross-cutting

- **Tenant scoping (ADR-0002).** Policy lookup, approver resolution, the $\Sigma^{VO}$ aggregate
  and every VO query are `TenantId`-scoped via the ambient global filter. The chain-snapshot child
  table must implement `ITenantOwned` so the filter reaches it by reflection (as
  `PaymentCertificateApprovalStep` does).
- **Project scoping (M-02, still open).** Any tenant user holding the current step's role can act
  on **any** project's VO — there is no project-membership concept in the model. For VOs this is
  more serious than for IPCs, because approval directly moves `Project.BAC`. Carry M-02 forward
  into `docs/security/reviews/sprint-10.md` as an explicitly accepted, documented limitation, or
  resolve it with a product decision. Do **not** let it be silently assumed closed.
- **Audit.** Every transition writes `AuditLog` (Before/After JSON) **and** an `ApprovalAction`.
  `ApprovalAction` is append-only; corrections are compensating rows. Attachment additions during
  `PendingApproval` (§2.2 item 4) write an `AuditLog` entry only — `ApprovalAction` retains its
  meaning as "a human decision act" and its enum is not extended.
- **Actor identity must never be fabricated.** L-01: `currentUser.UserId ?? Guid.Empty` on an
  append-only legal-evidence row is evidentially worthless and defeats the self-approval guard
  (`CreatedByUserId` can never be `Guid.Empty`). **Fail closed** on a null user id in every VO
  handler.
- **Notification** is a side effect of a transition, never a state. A failed notification must not
  roll back an approval.
- **Concurrency.** Every transition takes the `RowVersion` token → 409, never a double-advance.
  Non-advancing votes (quorum not yet reached, in **either** direction) must still stamp
  `LastVoteAt` so the row is `Modified` and `rowversion` serialises voters (N-03).
- **Single currency (THB).** Multi-currency would need a currency column on the amount bands and a
  rate date — flag now, do not build now.

---

## 8. Ruling on finding **N-05** — quorum and rejection

> `docs/security/reviews/sprint-09.md` §9.5 N-05 (Low, extends L-02), execution-verified: *"the same
> actor approved (1 of 2) and then rejected, taking the certificate to the terminal `Rejected` state
> alone… `Reject` also has no `DuplicateChainApprover` equivalent… **`domain-expert` to rule before
> Sprint 10 applies the same shape to VOs; record the answer in approval-workflow.md §6.**"*

### 8.1 The ruling

> **Quorum binds rejection exactly as it binds approval.** A step configured with
> `QuorumCount = q` requires **$q$ distinct users holding that step's role to reject** before the
> document reaches the terminal `Rejected` state. Additionally, **no actor may cast both an
> approval and a rejection on the same revision.**
>
> `ReturnForRevision` remains deliberately **single-actor and not quorum-bound** — it is the escape
> valve that makes the rule above deadlock-free.

**Reasoning.**

1. **The object of a quorum is the decision, not its direction.** An organisation that configures
   `QuorumCount = 2` is asserting "a decision of this consequence requires two humans". Reading
   that as "two humans to say yes, one to say no" is not a weaker version of the control — it is a
   *different* control, and not the one the customer configured. The product currently tells the
   customer dual control is switched on and then permits unilateral termination; that is the same
   class of defect as H-02 (a configured control that silently is not enforced), one severity band
   down only because the failure is conservative with money.
2. **For a Variation Order, refusal is a positive act with contractual weight.** A rejected VO can
   mean a contractor is refused payment for work already instructed and executed. Under FIDIC
   Sub-Clause 3.5 (1999) / 3.7 (2017) the Engineer's refusal is a formal, reasoned **determination**
   — an act of the same character as an acceptance, appealable to the DAAB and thence to
   arbitration. It is not a passive "do nothing" default. Under Thai public procurement, งานเพิ่ม /
   งานลด decisions are taken by a คณะกรรมการตรวจรับพัสดุ acting by **มติ** — the committee resolves;
   an individual member cannot unilaterally refuse a contractor's claim. Both traditions in the CM+
   user base treat refusal as a collective act where acceptance is one.
3. **Asymmetry creates a cheap denial vector.** Under the current reading, a single role-holder can
   terminally kill a claim that two people were required to certify, with **no withdraw/cancel
   recovery path** (N-02) — the replacement must be a brand-new document with a new number, losing
   the claim's date. That is a strictly larger unilateral power than the one dual control was
   configured to remove.
4. **The literal §6.1 reading is defensible but under-specified, not correct.** §6.1 restricts
   *approval* only. That is silence, not a decision. This ruling closes the silence rather than
   preserving an implementation side effect as policy.
5. **Blast radius is small.** `QuorumCount = 1` — the default and the overwhelmingly common case —
   is completely unaffected: one rejector still terminates. Only steps an administrator has
   deliberately configured as dual control change behaviour, which is precisely the population that
   asked for it.

### 8.2 Mechanics — mirror the approve path exactly

Reject-quorum counting, mirroring the H-02 fix verified in sprint-09.md §9.3:

$$\textit{rejectQuorumSatisfied} \iff \Big|\{\,\text{distinct } \textit{ActorUserId} : \textit{Action}=\texttt{Reject},\ \textit{RevisionNo}=\rho,\ \textit{StepNo}=\textit{CurrentStepNo}\,\}\Big| + 1 \;\ge\; q$$

- Until satisfied: the rejection **vote is recorded** as an `ApprovalAction` (evidence is never
  lost), `LastVoteAt` is stamped (N-03 parity — otherwise two concurrent first rejectors co-commit
  on an unprotected read-then-write), and the document stays `PendingApproval` at the same
  `CurrentStepNo`.
- On satisfaction: `Rejected`, terminal.
- Unchanged: only the **final** step's role holder may reject at all
  (`CurrentStepNo == TotalSteps`); intermediate approvers may only `ReturnForRevision`. `q` is read
  from the **document's snapshotted rung**, never re-derived from the policy (H-01).
- `Reject` gains a `rejectQuorumSatisfied` parameter mirroring `Approve`'s `quorumSatisfied`.

### 8.3 The duplicate-voter guard, widened

The existing `DuplicateChainApprover` predicate (`RevisionNo == current && Action == Approve &&
ActorUserId == actor`, **any** `StepNo`) becomes:

$$\textit{Action} \in \{\texttt{Approve},\ \texttt{Reject}\}$$

renamed **`DuplicateChainVoter`**. This alone fixes the exact scenario N-05 execution-verified: an
actor who has approved (1 of 2) can no longer then reject. It preserves the property sprint-09.md
§9.3 checked independently — the guard's predicate stays strictly broader than either quorum
count's, so no actor can appear twice in either counted set and `.Distinct()` stays genuinely
redundant rather than papering over a gap.

### 8.4 Deadlock, and why `ReturnForRevision` must stay single-actor

A `q = 2` step can reach 1 approval + 1 rejection: neither quorum met, and (with the widened guard)
neither actor may vote again. Left there, the document is stranded — the N-02 failure mode again.

**Resolution:** `ReturnForRevision` is **not** quorum-bound and is available to any holder of any
pending step's role at any time. A lone dissenter who cannot muster a rejection quorum can always
send the document back. That is exactly how a real committee handles a split — it does not resolve,
it returns the paper to the originator — and it needs **no new state and no new transition**.

Surface the condition so a human knows to act: expose `QuorumSplit = true` (approvals > 0 **and**
rejections > 0 on the current step) plus per-step vote counts on the document DTO. This also closes
the §9.5 informational note that `PaymentCertificateDto` exposes neither `ApprovalSteps` nor quorum
progress, so a first approver receives `200 success=true` with an unchanged status and cannot tell
it from a no-op.

### 8.5 Fixtures V-11 (VO) — and they apply unchanged to the IPC

Policy `TH-DualControl-VO`: one band $[0, \texttt{null})$, StepNo 1 = `ProjectDirector`,
`QuorumCount = 2`. VO `Add +1,000,000.00`, submitted by a QS. Approvers PD-A, PD-B, PD-C.

| # | Sequence | Expected |
| :-- | :-- | :-- |
| **V-11a** | PD-A `Approve` → PD-A `Reject` | vote 1 recorded, `PendingApproval` 1/1, approvals = 1 of 2. Then **409 `DuplicateChainVoter`**. Status still `PendingApproval`. **This is the exact N-05 scenario; today it yields terminal `Rejected`.** |
| **V-11b** | PD-A `Reject` → PD-B `Reject` | after PD-A: `ApprovalAction` written, `LastVoteAt` stamped, status **still `PendingApproval`** (1 of 2), `RowVersion` **changed**. After PD-B: **`Rejected`**, terminal. **Today PD-A alone terminates it.** |
| **V-11c** | PD-A `Approve` → PD-B `Reject` | neither quorum met; `PendingApproval` 1/1 with `QuorumSplit = true`, approvals 1, rejections 1. PD-A and PD-B both blocked from voting again (`DuplicateChainVoter`). |
| **V-11d** | continue V-11c: PD-C `ReturnForRevision` with a comment | **`Draft`**, $\rho = 2$, all votes voided, chain snapshot rows deleted (count 0), money + scope unfrozen. Proves the deadlock has an exit. |
| **V-11e** | continue V-11c: PD-C `Reject` | rejections reach 2 of 2 → **`Rejected`**, terminal, notwithstanding PD-A's approval. The audit trail records the split; the step required 2 to clear and got 1, required 2 to terminate and got 2. |
| **V-11f** | `QuorumCount = 1` (default) — any single final-step role holder `Reject`s | **`Rejected`** immediately. **Unchanged from today.** Proves zero regression for the common case. |

### 8.6 Does this change Sprint 9? **Yes — explicitly, and it should be fixed now**

`PaymentCertificate` uses the same shape, so the ruling is not VO-only. Three behaviour changes to
**shipped, PASS-verified Sprint 9 code**:

| # | Change | Today | After |
| :-- | :-- | :-- | :-- |
| 1 | `DuplicateChainVoter` on `RejectPaymentCertificateCommandHandler` | an actor who approved 1-of-2 may then reject | 409 `DuplicateChainVoter` |
| 2 | Reject-quorum on `PaymentCertificate.Reject` (new `rejectQuorumSatisfied` param, count from the `ApprovalAction` history the handler already loads) | one rejector terminates a `QuorumCount = 2` step | $q$ distinct rejectors required |
| 3 | `LastVoteAt` stamped on non-terminal rejections | only approvals stamp it | both do — N-03 parity, otherwise concurrent first rejectors co-commit |

**Recommendation:** implement all three in Sprint 10, in the shared handlers, alongside the VO work
— the code is the same code, and shipping two inconsistent readings of the same control across two
document types is worse than either reading. `security-auditor` should re-verify by execution under
S10-SEC-01, using V-11a/V-11b as the probes (they are the §9.5 probe, inverted). The N-05 row in
`sprint-09.md` §10 should move from *"Open — needs a `domain-expert` ruling"* to *"Ruled 2026-08-10;
fix scheduled S10"*, with a pointer here.

`approval-workflow.md` §6 has been updated with this ruling (new item 2a and an amended item 4) so
it is no longer an implementer's judgement call.

---

## 9. Open questions for the human — [ต้องยืนยัน]

Ordered by how much they change the build. Items 1–4 block S10-BE-02; item 5 blocks S10-BE-03;
items 6–9 can ship with the stated default and be revisited.

| # | Question | Blocking | Default until answered |
| :-- | :-- | :-- | :-- |
| **1** | **R-04(a)** — is $\theta = 10.00$ the right cumulative-VO escalation threshold? | S10-BE-02 | **None. `CumulativeVoEscalationPct` is seeded `NULL` = "no escalation configured".** Seeding 10.00 would make a guess indistinguishable from a decision in production data. |
| **2** | **R-04(b)** — does the counter reset when a formal contract amendment absorbs the approved VOs? | S10-BE-02 | None. Both semantics fully specified in §4.4; `ContractAmendmentResetsCumulativeVo` has no default. |
| **3** | **Numerator basis** — net signed (N-1) / gross absolute (N-2) / additions only (N-3)? Not in R-04's original wording but it moves the answer more than the threshold does (fixture V-4: 9.73% / 13.03% / 11.38% on identical data). | S10-BE-02 | **N-1 recommended** (§4.2) — matches the quantity the approval actually moves, and FIDIC 4th ed. 52.3's net test. |
| **4** | **Denominator** — original contract value (D-1) or current (D-2)? Requires `Project.OriginalContractValue`. | S10-BE-02, S10-DB-01 | **D-1 recommended and required by R4's own arithmetic** (§4.3). Note the shipped `ApprovalRoutingService` silently implements D-2 today. |
| **5** | **Does an approved VO move `ContractValue`, or must a signed สัญญาแก้ไขเพิ่มเติม do it?** Under a strict Thai public-procurement reading the *contract sum* changes only when the amendment is executed; the approved VO is internal authority to instruct. If so, `BAC` should move on approval (budget) but `ContractValue` — and therefore the retention ceiling and advance base — only on the amendment. This decouples two things the DoD currently couples. | S10-BE-03 | **The DoD's behaviour**: approval moves both, together. §4.4's `ContractAmendment` entity is the hook if the answer is "amendment-gated". |
| **6** | **Deduct VO vs in-flight IPC** (§6.3) — acknowledgement flag (ruled), hard block/auto-return, or advisory only? Whether an employer may knowingly over-withhold for one period is a contract question. | no | **Acknowledgement flag** (§6.3). |
| **7** | **`Amount` vs `BudgetCostImpact`** (§5.2) — are `ContractValue` and `BAC` ever distinct? Same as payment-retention.md §7 Q2, still open. | no | **M-1**: one `Amount` moves both. |
| **8** | **External approvers** — are VOs ever signed by an Employer's representative / consultant Engineer who is not a CM+ tenant user? More acute for VOs than for IPCs (the Engineer instructs the Variation under FIDIC 13.1). If yes, an "external approval recorded by X on behalf of Y" action type is needed for evidentiary completeness. Carried from approval-workflow.md §9 item 5. | no | Tenant users only. |
| **9** | **Role list** — confirm `ProjectDirector` and `Executive` exist in `User.Role`. A 3-tier VO matrix and the escalation role both need them. Carried from approval-workflow.md §9 item 4. | S10-DB-01 if missing | Assumed present. |

**Not open, recorded as accepted limitations for `docs/security/reviews/sprint-10.md`:**
M-02 (no project-scoped authorization — a tenant user with the step's role can approve any
project's VO, which now directly moves `Project.BAC`), N-02's remaining half (quorum may exceed the
number of users holding the role; `Withdraw`/`Cancel` unexposed), N-04 (non-unique snapshot
natural-key index — must **not** be repeated on the VO's snapshot table, §2.4).

---

## 10. Reconciliation with Primavera P6 and MS Project

**P6 has no Variation Order object.** Its equivalent is the **Budget Change Log** at the
EPS/WBS level, and the mapping is exact:

| CM+ | P6 |
| :-- | :-- |
| `Project.OriginalBac` | **Original Budget** |
| $\Sigma$ approved VO `Amount` | **Budget Change Log** total (a register of dated, reasoned change entries — structurally the same thing as CM+'s VO register) |
| `Project.BAC` = $BAC(\text{now})$ | **Current Budget** ( = Original Budget + Budget Changes) |
| $BAC_{new} - EAC$ | **Distributed Current Variance** (approximately; P6 also tracks Undistributed Current Variance for changes not yet spread to activities — CM+'s $\Delta B_{scope} = A$ invariant (§5.2) deliberately forbids that state, so CM+ has no undistributed remainder by construction) |

**The one difference that will show up in every comparison.** P6's EVM uses the **project
baseline's** BAC. CM+ rebaselines $BAC$ on VO approval. If CM+ has rebaselined and P6's baseline has
not been re-taken, every BAC-dependent figure differs by exactly the VO amount times the variant's
$PF$ (§5.7's closed form). Using fixture V-9's numbers:

| | CM+ (rebaselined, $BAC = 110{,}000{,}000$) | P6 (baseline not re-taken, $BAC = 100{,}000{,}000$) | Difference |
| :-- | --: | --: | --: |
| `CpiBased` EAC | 122,222,222.22 | 111,111,111.11 | **11,111,111.11** = $PF \cdot \Delta BAC$ |
| VAC | −12,222,222.22 | −11,111,111.11 | −1,111,111.11 = $(1-PF)\,\Delta BAC$ |
| $SPI$, $CPI$, $SV$, $CV$ | identical | identical | **0.00** |

**Always compare like-for-like baselines** (evm-formulas.md, risk R-01). A reconciliation that shows
$SPI$/$CPI$ matching exactly while EAC differs by exactly $PF \cdot \Delta BAC$ is a **correct**
reconciliation, not a defect — it is the signature of an un-re-taken P6 baseline. Confirm the source
project's actual P6 **Earned Value** setting in writing before signing off any golden-file EAC test
(P6's *default* ETC is the schedule's bottom-up remaining cost, not a $PF$ formula).

**MS Project** has no VO concept either; `Baseline Cost` is its BAC and `Cost` its forecast. MSP's
`EAC = ACWP + (BAC - BCWP)/CPI` is identical to CM+'s `CpiBased`, and `VAC = BAC - EAC` and
`TCPI = (BAC-BCWP)/(BAC-ACWP)` match CM+'s $TCPI_{BAC}$ — so once both sides use the same BAC they
agree exactly. Re-save the MSP baseline after a variation, or expect the same fixed offset.

**Deliberate CM+ divergence — $BAC(t)$.** Neither P6 nor MSP exposes BAC as a function of time;
they hold one Current Budget and one baseline. A `.XER`/MSPDI import therefore carries only
$BAC(\text{import date})$ and **cannot** reconstruct history. Rule for the importer: treat the
file's budget as $BAC$ **as of the import date**, write no retroactive VO records, and never
back-fill $BAC(t)$ for $t <$ import date.

---

## 11. Traceability — rule → task → fixture

| Rule | Sprint 10 task | Fixture(s) | Test artifact |
| :-- | :-- | :-- | :-- |
| 5-state machine, transitions, freeze matrix (§2) | S10-BE-01, S10-DB-01 | V-10, V-11d | `CMPlus.Domain.Tests/VariationOrderStateMachineTests` |
| $A^{route}=\lvert A\rvert$, band resolution (§3) | S10-BE-02 | **R1, R2, R2′, R3, R5, R6** | `CMPlus.Integration.Tests/Vo/` (real API — S10-QA-01) |
| Escalation, parameterised (§4) | S10-BE-02 | **R4**, V-4, V-5a, V-5b | ditto + Application-level unit theories |
| Escalation cannot be bypassed (§4.7) | S10-BE-02, S10-BE-03 | **V-6** | `CMPlus.Integration.Tests/Vo/` — S10-SEC-01 gate item |
| $BAC$/$C^{cur}$ move signed; scope invariant (§5.1–5.2) | S10-BE-03 | V-9, plus `VoScopeBudgetMismatch` negatives | `CMPlus.Integration.Tests/Evm/RebaselineTests.cs` |
| Retention ceiling moves; **never** auto-refund; advance base fixed (§5.3) | S10-BE-03 | **V-7, V-8** | `CMPlus.Integration.Tests/Payment/` |
| Snapshot immutability + $BAC(t)$ + PV origin (§5.5) | S10-BE-03 | V-9 + full-row byte-equality on `EvmPeriodSnapshot` | `RebaselineTests.cs` — S10-QA-02 |
| All five EAC variants across the boundary; `ManualEtcPredatesBacChange` (§5.7) | S10-BE-03, S10-FE-02 | **V-9** | `RebaselineTests.cs` |
| In-flight IPC unaffected + disclosure (§6) | S10-BE-03 | V-7/V-8 composed with a `PendingApproval` IPC | `CMPlus.Integration.Tests/Payment/` |
| **N-05 reject quorum + `DuplicateChainVoter`** (§8) | S10-BE-01 **and a Sprint 9 fix** | **V-11a–f** | `CMPlus.Integration.Tests/Approval/` — S10-SEC-01 gate item |
| BAC impact panel: old → new, cumulative %, escalation warning | S10-FE-02 | V-9, V-5b | `web/src/features/vo/components/BacImpactPanel.test.tsx` |

**Rule from `docs/10.` §10 that binds every consumer of this file:** an agent claiming a test passes
must show the real run output, and **a fixture that has passed may never be edited to make code
pass** — if a computed value disagrees with a fixture here, escalate to `domain-expert` for a
ruling first.
