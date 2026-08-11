# Weather Log & Extension of Time (EOT) — Domain Rules (Sprint 11)

**Stage-2 artifact.** Author: `domain-expert` · Date: 2026-08-10 · Feature: `weather-eot`
**Consumers:** `system-architect` (design.md), `backend-developer` (S11-BE-01/02/03),
`database-engineer` (S11-DB-01), `frontend-developer` (S11-FE-01), `qa-engineer` (S11-QA-01).

**This is the first authoritative statement of EOT rules in this repository.** There is no
`.claude/knowledge/domain/eot-*.md`; the only prior text is one sentence in
`.claude/skills/cm-domain/SKILL.md` §"Weather Log / EOT" and two acceptance criteria in
`docs/specs/master-plan/backlog-detailed.md` US-11.1. Everything below is new and normative.
`knowledge-curator` should promote §3–§7 into `.claude/knowledge/domain/eot-method.md` after
this sprint.

**Upstream sources this document is bound by** (read them; do not paraphrase from here):

| Source | What it fixes |
| :-- | :-- |
| `docs/10.` §8 Sprint 11 | the task table and every DoD clause quoted below (S11-BE-01/02/03, S11-FE-01, S11-DB-01, S11-QA-01) |
| `.claude/knowledge/domain/cpm-method.md` | the CPM algorithm, $TF$/$FF$, `IsCritical`, and the canonical A/B/C/D fixture reused verbatim in §10 |
| `backend/src/CMPlus.Application/Services/Cpm/` | the **shipped** engine: `CpmEngine` (pure, day-counts, $LF_{proj}=\max EF$, `IsCritical` $\iff TF=0$), `WorkingCalendar` (calendar/holiday math), `GraphValidator` |
| `docs/9.` §4 | the `DailyWeatherLog` and `IssueLog` field lists; §5 the two endpoints (`POST` weather-logs is *append-only*) |
| `docs/วิเคราะห์ฯ` §5 "Weather Tracking" | the three-value impact classification and the "rainfall over a critical threshold" concept |
| `.claude/knowledge/patterns/conventions.md` | "Weather logs … are immutable — corrections are new entries" |
| `.claude/knowledge/domain/approval-workflow.md` | the approval engine — and §8.6 below, on why weather logs must **not** be routed through it |
| ADR-0002 · ADR-0008 · ADR-0009 · ADR-0015 | tenant filter · version-pinned policy · append-only snapshots and denormalised caches · *nullable config with no seeded default* |

Precision: EOT and stoppage day counts are **whole days**, `int`. Hours `decimal(4,2)`, rainfall
`decimal(6,2)`, dates `DateTimeOffset` (calendar-day identity, per `WorkingCalendar`'s existing
convention). Where any rounding occurs it is `MidpointRounding.AwayFromZero` (risk R-12); where a
fraction is reduced to whole days it is `Math.Floor`, explicitly, never `Math.Round` (§3.4).
**No money value appears anywhere in this feature** — see §2.

---

## 1. Definitions

| Term | Symbol | Definition |
| :-- | :-- | :-- |
| Extension of Time | EOT (การขยายเวลาทำการตามสัญญา) | A contractual extension of the Time for Completion. It relieves the contractor of liquidated damages for the extended period. It is a **time** award; it does not by itself award money. |
| Weather log entry | $\ell$ | One `DailyWeatherLog` row: a dated, immutable site record of weather and its effect on work. |
| Log date | $d$ | `DailyWeatherLog.LogDate` — a **calendar day**, not an instant. |
| Effective log set | $\mathcal{L}^{eff}$ | The weather entries currently in force after applying the correction chain (§8.2). The only set the evaluator ever reads. |
| Work stoppage | — | Work on one or more named activities did not proceed on $d$ because of the recorded weather. |
| Hours lost | $h_{j,d}$ | `HoursLost` — working hours lost on activity $j$ on day $d$, `decimal(4,2)`, $0 \le h \le 24$. |
| Countable stoppage day | — | A day that passes **every** gate in §3. Only countable days can ever produce EOT. |
| Stoppage-day count | $S_j$ | Countable stoppage days accrued against activity $j$ inside the evaluation window. |
| Governing CPM run | $r(d)$ | The schedule the criticality question is answered against for day $d$ — §4. |
| Total float (at a run) | $TF_j^{(r)}$ | Activity $j$'s total float as computed by run $r$. Not the live `Activity.TotalFloat`. |
| Critical | — | $TF_j^{(r)} = 0$ (the shipped `CpmEngine` definition). See §4.5 for the **negative-float caveat**. |
| As-scheduled duration | $D^{as}_r$ | `CpmCalculationResult.ProjectDurationDays` of run $r$, in working days. |
| Impacted duration | $D^{imp}_r$ | The same network re-run with the countable stoppages added to activity durations (§5.1). |
| EOT-eligible days | $E$ | The evaluator's answer. **A schedule fact, not an entitlement** — §2.2. |
| Evaluation window | $[w_0, w_1]$ | The closed date range the evaluation covers. Default: project start → data date. |
| Evaluation | — | One `EotEvaluation` record: an immutable, dated, reproducible snapshot of an evaluator run (§8.5). |
| Correction | — | A new weather entry that replaces an earlier one. Never an edit (§8.2). |
| Retraction | — | A new weather entry that voids an earlier one without replacing it (§8.2). |
| Concurrent delay | — | Two or more delay events with different risk-owners whose effects overlap in time. **Out of scope for Sprint 11** — §7. |

---

## 2. The boundary that makes this feature safe to ship

### 2.1 The evaluator is advisory. It moves no date and no money.

> **Hard invariant (assertable — fixture W-14).** A run of `EotEvaluator` writes rows to
> `EotEvaluation` and its children, and to `AuditLog`. **Nothing else in the database changes.**

Specifically, an evaluation must **not**:

- change `Activity.PlannedStart` / `PlannedFinish` / `DurationDays`;
- change `Activity.IsCritical` / `TotalFloat` / `FreeFloat` — the impacted network of §5 is a
  *projection*, computed in memory and discarded. Fixture W-03 makes activity B critical **in the
  impacted network**; the persisted `Activity.IsCritical` for B must still read `false` afterwards;
- change any project-level date, or create a `ContractCompletionDate` (no such field exists);
- write a `ProjectFinanceLedger` row, a `VariationOrder`, a `PaymentCertificate`, or an
  `EvmPeriodSnapshot`;
- trigger a CPM recalculation, or invalidate one.

**Why this boundary matters.** EOT determines *time*, and time determines *money*: an EOT award
relieves liquidated damages for the extended period, and — separately, and only if the contract so
provides — may found a claim for prolongation cost (extended preliminaries/site overheads). Under
FIDIC 1999 Sub-Clause 8.7 / 2017 Sub-Clause 8.8, Delay Damages accrue for the period by which the
Contractor fails to complete **within the Time for Completion as extended**; under the Thai standard
government construction contract the equivalent is งดหรือลดค่าปรับ. So a number produced by this
evaluator, if it were allowed to move a contractual date, would silently move a penalty. CM+ has no
contract-administration surface yet — no `ContractCompletionDate`, no LD rate, no claim document,
no employer-side determination. **Until those exist, the only defensible posture is that the
evaluator produces evidence and an opinion, and a human moves the date.** Ship it that way.

### 2.2 `EotEligibleDays` is a schedule fact, not an entitlement

The evaluator answers exactly one question:

> *Given the recorded stoppages and the schedule in force when they happened, by how many working
> days did the project's computed completion move?*

It does **not** answer whether the contract entitles the contractor to that time. Entitlement
additionally requires, depending on the contract:

1. that the weather qualify — "**exceptionally** adverse climatic conditions" (FIDIC 1999
   Sub-Clause 8.4(c); FIDIC 2017 Sub-Clause 8.5(c), which defines the test by reference to
   published climatic data for the Site's location), or **เหตุสุดวิสัย** under the Thai standard
   form. Note carefully: ordinary seasonal monsoon rain in Thailand is foreseeable and is generally
   **not** เหตุสุดวิสัย within ป.พ.พ. มาตรา 8. A system that reports "18 EOT days" for eighteen
   ordinary July rain days is reporting a schedule impact, not a legal right — §3.5 and open
   question **Q1**;
2. that **notice** was given in time. FIDIC 1999 Sub-Clause 20.1 and FIDIC 2017 Sub-Clause 20.2.1
   both impose a **28-day** Notice of Claim which operates as a condition precedent — miss it and
   the Time for Completion "shall not be extended". The Thai standard-form construction contract
   requires the contractor to notify within **15 days** of the cause ceasing, failing which the
   right is forfeited unless the agency already knew of the cause. *(Clause numbering varies by form
   version — verify against the executed contract.)* §3.6 gives CM+'s advisory-only treatment;
3. that concurrency has been assessed — §7.

Every `EotEvaluation` therefore carries `EntitlementBasisAssessed = false` and
`ConcurrencyAssessed = false`, and the UI (S11-FE-01) must render the disclosure verbatim:

> **ตัวเลขนี้คือผลกระทบต่อกำหนดแล้วเสร็จตามตารางงาน ไม่ใช่สิทธิ์ตามสัญญา**
> การได้รับสิทธิ์ขยายเวลาขึ้นกับเงื่อนไขสัญญา (ความ "ผิดปกติ" ของสภาพอากาศ, การแจ้งเหตุภายในกำหนด)
> และการพิจารณาความล่าช้าที่เกิดพร้อมกัน ซึ่งระบบยังไม่ได้ประเมินให้

The prototype's gold tile "สิทธิ์ขยายสัญญา (EOT) — 12 วัน" must be relabelled
**"ผลกระทบต่อกำหนดแล้วเสร็จ (EOT ที่ประเมินได้)"** for the same reason.

---

## 3. When does a stoppage day count at all?

> S11-BE-02 DoD: *"หยุดงานบนกิจกรรม critical → EOT-eligible; บน non-critical → กิน float เท่านั้น"* —
> but that rule only ever engages for days that clear the gates below.

A stoppage on activity $j$ on day $d$ is **countable** iff **all six** predicates hold. They are
evaluated in this order and the **first failure is recorded as the day's exclusion reason** — the
evaluator never silently drops a row (fixture W-09).

$$
\text{countable}(j,d) \iff
\underbrace{\text{InForce}(\ell)}_{\S3.1} \wedge
\underbrace{\text{Attributed}(\ell)}_{\S3.2} \wedge
\underbrace{\text{WorkingDay}(d)}_{\S3.3} \wedge
\underbrace{\text{Threshold}(\ell)}_{\S3.4,\ \S3.5} \wedge
\underbrace{\text{InWindow}(j,d)}_{\S3.7} \wedge
\underbrace{\text{InEvalRange}(d)}_{[w_0,w_1]}
$$

### 3.1 The entry must be in force

$\ell \in \mathcal{L}^{eff}$ — superseded and retracted entries are invisible to the evaluator
(§8.2). Exclusion reason: `Superseded` / `Retracted`.

### 3.2 The entry must name at least one activity

`AffectedActivityIds` non-empty, and every id must resolve to an `Activity` in the same tenant and
project. A stoppage that names nothing is real evidence of weather but is **not evaluable** — there
is no activity whose duration could be extended and no criticality to test.

- Contributes **0** EOT days.
- Counted in `UnattributedStoppageDayCount` and surfaced with reason `NoAffectedActivity`, so the
  site engineer can file a correction (§8.2) that names the activity.
- The evaluation is stamped `HasUnattributedDays = true`.

**Do not "spread it across all in-progress activities."** That invents evidence, and it would let a
careless entry inflate the answer without anyone signing for it.

### 3.3 The day must be a working day on the governing calendar

> *"a rain day on a Sunday the contractor was never going to work is not a delay"* — correct, and
> this is already computable with shipped code.

$$\text{WorkingDay}(d) \iff \texttt{WorkingCalendar.IsWorkingDay}(d,\ C.\textit{WorkingDays},\ C.\textit{Exceptions})$$

where $C$ is the project's `IsDefault` calendar. Three consequences that must be tested, not
assumed (fixtures W-04, W-04b):

1. **"Weekend" is whatever the calendar says.** Thai construction commonly runs a **Mon–Sat**
   week. Under such a calendar a Saturday stoppage **counts**. Hard-coding `DayOfWeek.Saturday ||
   DayOfWeek.Sunday` is a defect.
2. **A `CalendarException` wins outright, in both directions** — `WorkingCalendar.IsWorkingDay`
   already implements this. A public holiday (`IsWorkingDay = false`) makes an otherwise-working
   Tuesday non-countable; a scheduled Sunday pour (`IsWorkingDay = true`) makes an otherwise
   non-working Sunday **countable**. Fixture W-04b exists solely for the second direction, which
   implementations forget.
3. **No default calendar ⟹ block, never guess.** If the project has no `IsDefault` `Calendar`, the
   evaluation fails with **422 `ProjectCalendarNotConfigured`**. Assuming "all days are working
   days" over-grants; assuming Mon–Fri is a guess. Same fail-closed discipline as
   `ApprovalPolicyGap` (approval-workflow.md §5.3 step 6).

⚠ **Known limitation — activity-level calendars.** `Activity` has no `CalendarId`; there is one
calendar per project (`Calendar.IsDefault`). P6 assigns a calendar per activity, so a project where
concrete works run Mon–Sat and commissioning runs Mon–Fri cannot be modelled. Recorded as
open question **Q6**; until then, one calendar governs every activity and the reconciliation note
in §11 applies.

### 3.4 Full day vs partial day — `PartialDayPolicy`

The prototype's own data has partial days ("หยุดเทคอนกรีตโซน B **ครึ่งวัน**"), and
`docs/วิเคราะห์ฯ` §5 classifies impact three ways (หยุดงานกลางแจ้ง / งานหยุดบางส่วน / ไม่กระทบงาน).
So partial days are real and must be modelled, not flattened.

**Field shape (reconciling `docs/9.` §4 with `docs/วิเคราะห์ฯ` §5):** replace the bare
`WorkStoppageFlag bit` with

```
Impact      enum WeatherImpact { NoImpact = 0, PartialStoppage = 1, FullStoppage = 2 }   NOT NULL
ImpactNote  nvarchar(500) NULL      -- the prototype's free-text "หยุดเทคอนกรีตโซน B ครึ่งวัน"
HoursLost   decimal(4,2)  NULL      -- required by validation when Impact <> NoImpact (see below)
```

`WorkStoppage` is then **derived**, never independently stored: $\text{WorkStoppage} \iff
\textit{Impact} \ne \texttt{NoImpact}$ — so the flag and the classification can never disagree
(same discipline as `VariationOrder.Type` deriving from the sign of `Amount`).

Let $H$ = `FullDayHours` (`decimal(4,2)`, default **8.00**) and $h_{min}$ =
`MinHoursLostForCountableDay` (`decimal(4,2)`, default **4.00**). Let $h_{j,d}$ be the hours lost.
Three policies, exactly one active per project:

| `PartialDayPolicy` | Countable? | Days contributed $s_{j,d}$ | Reads $h_{min}$? |
| :-- | :-- | :-- | :--: |
| `FullDayOnly` | $h_{j,d} \ge H$ | $1$ | no |
| **`ThresholdWholeDay` (default)** | $h_{j,d} \ge h_{min}$ | $1$ | **yes** |
| `FractionalAccrual` | $h_{j,d} > 0$ | $h_{j,d} / H$ (see floor rule) | no |

$$
\text{Under FractionalAccrual:}\quad
\Delta D_j \;=\; \left\lfloor \frac{\sum_{d} \min\!\big(h_{j,d},\, H\big)}{H} \right\rfloor
$$

Five precision rules that decide fixtures:

- **One calendar day can never contribute more than one day of duration — $h_{j,d}$ is clamped at
  $H$ before accrual** (the $\min(h_{j,d}, H)$ in the formula above). `HoursLost` is validated to
  $[0, 24]$ while $H$ defaults to 8.00, so a site running two or three shifts can legitimately record
  `HoursLost = 24.00` for one lost calendar day. Un-clamped, `FractionalAccrual` turns that single
  date into $\lfloor 24/8 \rfloor = 3$ duration days — three days of project delay produced by one
  day of weather, which is physically impossible and breaches §5.3 (fixture **W-18**). $H$ *is* the
  definition of "one working day's worth of hours" — the `FullDayOnly` policy on the same table uses
  it exactly that way — so charging more than $H$ for one calendar day charges more than one day.
  Three consequences, all deliberate:
  - **The clamp is applied at evaluation, never at write time.** `HoursLost = 24.00` is a true
    statement about the site and must be recordable and stored verbatim (§8 — the log is immutable
    evidence). $H$ is per-project policy that can change *after* the entry is written, and each
    evaluation pins its own $H$ in `PolicySnapshotJson`, so the same entry may legitimately clamp
    differently in two evaluations. Validating `HoursLost ≤ FullDayHours` at write time would both
    destroy evidence and freeze a policy value into an immutable row.
  - **Report it, do not hide it.** The source row carries `HoursLostClampedToFullDay = true` when
    $h_{j,d} > H$ actually bit, so a reviewer can see that 24.00 was recorded and 8.00 was charged.
  - **A genuinely multi-shift project must set `FullDayHours` to its own shift length** (12.00 or
    24.00), not leave it at 8.00 — otherwise every full-shift stoppage is *under*-counted. See Q2.
- **Comparisons are inclusive (`≥`).** Exactly 4.00 h under `ThresholdWholeDay` **counts**
  (fixture W-05). This is the opposite convention to the approval matrix's exclusive `MaxAmount`,
  deliberately: a threshold that says "half a shift or more is lost" includes half a shift.
- **$h_{min}$ is read *only* under `ThresholdWholeDay`.** Applying it under `FractionalAccrual`
  would be a silent double gate that discards every short interruption before it can accrue.
- **The floor is per activity, and the remainder is discarded.** $\lfloor 13.50/8.00 \rfloor = 1$.
  The remaining 5.50 h is reported as `UnclaimedFractionalHours` and is **not** carried to another
  activity or to a later evaluation — carrying it would make the answer depend on evaluation order,
  which is not reproducible and therefore not evidence.
- **`HoursLost IS NULL` with `Impact <> NoImpact` ⟹ treat as $H$ (a full day), and flag it.** The
  impact classification is the recorder's primary assertion; the hours are a refinement. Treating a
  missing refinement as zero would delete the primary assertion. The source row is stamped
  `HoursLostAssumed = true` so a reviewer can see which days rest on an assumption. Going forward,
  `HoursLost` is **required by FluentValidation** when `Impact <> NoImpact`, so this path only ever
  fires for imported or legacy rows.

### 3.5 Contract thresholds are configuration, never code

Thai contracts and international forms both commonly gate a "rain day" on measured depth;
`docs/วิเคราะห์ฯ` §5 assumes exactly this ("ฝนตกหนักสะสมเกินค่าวิกฤตที่ยอมรับได้ เช่น … มม."). Practice
varies by contract, so it is **per-project data**, not a constant.

```
ProjectEotPolicy                                  -- 1:1 with Project (architect's call: own table or Project columns)
  TenantId, ProjectId
  CountingBasis      enum { AbsoluteStoppageDays = 1 (default), ExceedanceOverBaseline = 2 }
  PartialDayPolicy   enum { FullDayOnly = 1, ThresholdWholeDay = 2 (default), FractionalAccrual = 3 }
  FullDayHours                    decimal(4,2) NOT NULL default 8.00
  MinHoursLostForCountableDay     decimal(4,2) NOT NULL default 4.00
  MinRainfallMmForCountableDay    decimal(6,2) NULL        -- NULL = NOT CONFIGURED (never 0.00)
  CountUnmeasuredRainfallWhenThresholdSet bit NOT NULL default 0    -- fail-closed
  NoticePeriodDays                int NULL                 -- NULL = NOT CONFIGURED
  CalendarId                      Guid NULL                -- NULL = the project's IsDefault calendar

ProjectEotMonthlyAllowance                        -- only when CountingBasis = ExceedanceOverBaseline
  TenantId, ProjectId, Month tinyint (1-12), ExpectedStoppageDays int
  Source nvarchar(200)      -- e.g. "TMD สถานีบางนา ค่าเฉลี่ยวันฝนตก 2539-2568"
```

**`MinRainfallMmForCountableDay` defaults to `NULL`, and `NULL` means "no depth test configured",
never "0.00".** This follows the ADR-0015 precedent exactly: a seeded placeholder is
indistinguishable from a decision in production data. Rationale for defaulting it off: the
*stoppage* is the fact that matters and CM+ records it directly; a depth threshold is a contractual
*proxy* for "work was impossible", and applying both a direct record and its proxy is double-gating.
Where the contract does impose one, set it.

**Combination is AND.** When the depth test is configured, a day is countable only if
$\textit{RainfallMm} \ge \textit{MinRainfallMmForCountableDay}$ **and** the §3.4 hours gate passes.
Some contracts word it as OR ("≥20 mm **or** ≥4 hours lost") — open question **Q3**.

**Unmeasured rainfall is fail-closed.** If a depth threshold is configured and `RainfallMm IS NULL`,
the day is **not countable** (reason `RainfallNotMeasured`), because the contract's evidentiary
condition is unproven. `CountUnmeasuredRainfallWhenThresholdSet = 1` inverts this for tenants whose
sites have no gauge; default 0.

**`CountingBasis = ExceedanceOverBaseline`** implements the FIDIC 8.4(c)/8.5(c) reading, where only
weather *worse than could reasonably have been anticipated* qualifies. For each calendar month $m$
in the window, let $N_m$ be the countable days from §3.1–§3.4 and $\bar N_m$ the month's allowance:

$$
S^{exc}_m \;=\; \max\big(0,\ N_m - \bar N_m\big)
$$

The **allowance is consumed chronologically**: order the month's countable days ascending by date
and discard the first $\bar N_m$; the surviving $S^{exc}_m$ days keep their own activity attribution
and go forward to §5. Deterministic, explainable, and it never has to guess which activity to
"charge" the allowance to. A month with no `ProjectEotMonthlyAllowance` row while this basis is
active is a configuration error ⟹ **422 `EotMonthlyAllowanceMissing`**, not $\bar N_m = 0$.

Fixture W-13 gives **5 days under `AbsoluteStoppageDays` and 2 under `ExceedanceOverBaseline`** on
identical data. The basis is open question **Q1** and the human must answer it before a Thai
public-works pilot relies on the number.

### 3.6 Notice — advisory only, zero arithmetic effect

When `NoticePeriodDays` is configured, the evaluation reports

$$t^{notice} = \max\{\, d : d \text{ countable} \,\} + \textit{NoticePeriodDays}\ \text{calendar days}$$

as `LatestNoticeDate`, with `NoticeWindowExpired = (today > t^{notice})`. This **never** changes
$E$. Its purpose is to stop a user discovering at adjudication that a perfectly-computed 12 days was
time-barred. `NULL` ⟹ the fields are omitted entirely, not zeroed.

### 3.7 The activity must have been performable on that day

$$
\text{InWindow}(j,d) \iff
\big(d \ge \textit{ActualStart}_j \ \vee\ (\textit{ActualStart}_j = \varnothing \wedge d \ge \textit{PlannedStart}_j)\big)
\ \wedge\
\big(\textit{ActualFinish}_j = \varnothing \ \vee\ d \le \textit{ActualFinish}_j\big)
$$

- Stoppage **after** `ActualFinish` ⟹ excluded, reason `ActivityAlreadyComplete` (W-08a). Extending
  the duration of a finished activity is meaningless and would fabricate EOT.
- Stoppage **before** the activity could start ⟹ excluded, reason `ActivityNotYetScheduled` (W-08b).

⚠ This predicate is deliberately **permissive** for the not-yet-started case: if the activity was
*planned* to be running, weather is allowed to have stopped it, even without an `ActualStart`. The
tighter reading — "if the contractor had not started it, his own lateness caused the delay, not the
rain" — is a **concurrency** argument and is therefore out of scope (§7). If Q4 is later answered in
favour of assessing concurrency, this predicate tightens to require `ActualStart`.

> **Ruling (2026-08-10, on `qa-engineer`'s S11-QA-01 escalation) — §3.7 does *not* gain a
> network-topology test, and an entry naming an activity together with its own predecessor is
> valid input.** It was proposed that InWindow be tightened so that a stoppage cannot be charged to
> an activity *and* its transitive CPM predecessor on the same date. **Rejected**, for three
> reasons:
>
> 1. **A single storm genuinely stops both.** Excavation and the foundation behind it are one crew,
>    one site, one shutdown. The weather log is a contemporaneous evidentiary record (§8): a rule
>    that refuses to record what happened on site is a worse defect than the arithmetic it was
>    trying to fix, and it would push site engineers into recording something untrue in order to
>    get the entry accepted.
> 2. **Countability is a property of the day and the activity, not of the network.** All six §3
>    predicates are answerable from the entry, the calendar, the policy and the activity's own
>    dates. Comparability is a property of a *CpmRun*, and the governing run is not resolved until
>    §4 — so the test cannot even be evaluated at this point in the pipeline. Worse, the network
>    changes: two activities in an FS chain today may be re-sequenced in parallel tomorrow, so an
>    entry rejected at write time would have been legitimate against the run that actually governs
>    it. **Nothing that depends on the network or on policy may be enforced at write time** — the
>    same rule that puts §3.4's $H$-clamp at evaluation.
> 3. **It fixes the wrong thing.** The double-count is not that the day was counted; it is that the
>    day was charged into the same *path* twice. That is a §5 modelling question, and it is settled
>    in **§5.2a**. The entry stays; the day stays countable; the second charge is absorbed and
>    reported.
>
> Fixture **W-17** pins this: both activities remain countable, both keep a driver row, and $E$ is
> nevertheless capped. No `ExclusionReason` is ever raised for this shape.

---

## 4. Critical *at what point in time*? — **Ruling: contemporaneously**

This is the question that most changes the answer, and it has three candidate answers.

| | Reading | What it uses |
| :-- | :-- | :-- |
| **T1** | *Current schedule* | today's `Activity.IsCritical` / `TotalFloat` |
| **T2** | *Contemporaneous* — **RULED** | the schedule in force **on the stoppage date** |
| **T3** | *As-planned / baseline* | the original baseline network |

### 4.1 The ruling

> **The criticality and float used for a stoppage on day $d$ are those of the governing CPM run**
> $$r(d) = \arg\max_{\,r\,\in\,\mathcal{R}} \{\, r.\textit{CalculatedAt} \;:\; r.\textit{CalculatedAt} \le d \,\}$$
> **— the most recent schedule calculation at or before the stoppage date.**

### 4.2 Why T2, with authority

- **SCL Delay and Disruption Protocol, 2nd edition (February 2017), Core Principle 4** requires
  entitlement to be assessed **contemporaneously**, against the programme updated to immediately
  before the delay event, and warns against "wait and see". Core Principle 1 and Guidance Part B
  reinforce that the analysis is done on the schedule as it stood, not on one overtaken by events.
- **AACE International RP 29R-03, *Forensic Schedule Analysis*** classifies observational
  contemporaneous and modelled/additive methods (MIP 3.3/3.4 and 3.6/3.7) as those that use the
  contemporaneously-updated schedule; **Impacted As-Planned** (T3, MIP 3.6 against the baseline) is
  the method the RP and the SCL Protocol both single out as the least reliable, because it ignores
  actual progress.
- **T1 is indefensible in principle**, because it lets *later, unrelated* events change a past
  entitlement. If a February re-sequencing makes an activity non-critical, an owner using T1 can
  extinguish a properly-recorded January entitlement retroactively. Fixture **W-11b** shows the same
  weather day yielding **1 day under T2 and 0 under T1** on identical data.
- **T3 over-grants**, for the mirror reason: it credits float that the contractor had already
  consumed for his own reasons before the weather arrived. Fixture **W-15** shows **3 days under T2
  and 4 under T3**.

### 4.3 Consequence — Sprint 11 needs schedule history that does not exist yet

`Activity.IsCritical` / `TotalFloat` / `FreeFloat` are three mutable fields overwritten by the most
recent `RecalculateCpmCommand`. There is no history, so **T2 is not implementable against today's
schema.** `CpmEngine` already *computes* everything needed
(`CpmCalculationResult` carries ES/EF/LS/LF/TF/FF/`IsCritical` per activity plus
`ProjectDurationDays`); the handler discards all but three fields.

**Required (new work, beyond S11-DB-01's stated scope — escalate before committing the sprint):**

```
CpmRun                    -- append-only (IAppendOnly)
  Id, TenantId, ProjectId
  CalculatedAt DateTimeOffset, DataDate DateTimeOffset NULL
  ProjectDurationDays int, TriggeredByUserId, Trigger enum { Manual, Import, VoApproval, System }
CpmRunActivity            -- append-only; the run's per-activity results
  CpmRunId, ActivityId, DurationDays, EarlyStart, EarlyFinish, LateStart, LateFinish,
  TotalFloat, FreeFloat, IsCritical
CpmRunRelation            -- append-only; the run's network topology
  CpmRunId, PredecessorActivityId, SuccessorActivityId, RelationType, LagDays
```

`CpmRunRelation` is **not optional**: §5 re-runs `CpmEngine` on the governing run's network, so the
topology must be recoverable, not just the floats. Index `(TenantId, ProjectId, CalculatedAt DESC)`.
Volume is comparable to `ActivityProgressLog` (risk **R-11**, already accepted at ~350k rows/project):
10,000 activities + ~15,000 relations × ~35 weekly runs ≈ 875k rows/project — so a **retention
policy is required** (recommendation: keep every run for 24 months, then keep one run per month;
never delete a run referenced by an `EotEvaluation`).

### 4.4 Degraded modes — explicit, never silent

| Condition | `CriticalityBasis` | `Confidence` | Behaviour |
| :-- | :-- | :-- | :-- |
| A run exists at or before every countable day | `Contemporaneous` | `Substantiated` | normal |
| Some days have a run, some do not (use the **earliest** run for the orphans) | `Mixed` | `Provisional` | compute, flag every affected day |
| No run at or before **any** countable day → use the earliest run for all | `Retrospective` | `Provisional` | compute, flag prominently (W-11a) |
| No `CpmRun` at all | — | — | **422 `NoCpmRunAvailable`** — refuse |

An evaluation whose `Confidence = Provisional` must be labelled as such on screen and in any export.
It must never be presented as a substantiated figure.

**If the human declines the `CpmRun` scope addition (Q5):** S11-BE-02 still ships, reading the live
`Activity` fields as a single synthetic run stamped with the current time, and **every** evaluation
comes back `Retrospective` / `Provisional`. That is honest and useful for site awareness; it is not
claim-grade. Do not pretend otherwise by omitting the flag.

### 4.5 Two caveats on the definition of "critical" itself

1. **Negative float.** The shipped `CpmEngine` sets $LF_{project} = \max EF$, so $TF \ge 0$ always
   and `IsCritical` $\iff TF = 0$ is exactly right *today*. The moment a contract completion
   constraint is introduced, a late project produces $TF < 0$ and
   **`IsCritical` must become $TF \le 0$**. If it is left as $TF = 0$, a project running five days
   late shows **zero** critical activities and this evaluator silently grants **zero EOT** — on
   precisely the project where it matters most. Flagged to `system-architect` as a
   must-change-together item; recorded as open question **Q7**.
2. **Near-critical.** Some contracts and most P6 configurations treat $TF \le n$ (commonly 5 or 10
   days) as critical for management purposes. CM+ uses $n = 0$. This does **not** change $E$ —
   §5's network method never reads `IsCritical` to compute the number — but it does change the
   `WasCriticalAtRun` explanation flag. Leave at 0; note in §11.

---

## 5. Computing $E$ — a contemporaneous windows Time Impact Analysis

### 5.1 The method

Do **not** write a bespoke float ledger. Reuse `CpmEngine`, which is a pure function and already
handles shared float, criticality shift, parallel paths and all four relation types correctly.

1. Partition the countable stoppage days by governing run: $\mathcal{D} = \biguplus_r \mathcal{D}_r$.
2. For each run $r$, in chronological order:
   - $D^{as}_r$ = `CpmEngine.Calculate(run r's activities, run r's relations).ProjectDurationDays`
     (equivalently, the stored `CpmRun.ProjectDurationDays` — assert they agree; a mismatch means the
     stored run is corrupt and the evaluation must fail rather than proceed);
   - **apply the serial-chain collapse of §5.2a** to $\mathcal{D}_r$ — per date, at most one
     activity per path may be charged. This step is what makes §5.3's cap a theorem rather than a
     hope; skipping it produces $E$ greater than the number of days it rained;
   - build the **impacted** activity set: $D_j^{imp} = D_j + \Delta D_j$ where $\Delta D_j$ is
     activity $j$'s **charged** stoppage days from $\mathcal{D}_r$ after the collapse (§3.4, §5.2a);
   - $D^{imp}_r$ = `CpmEngine.Calculate(impacted activities, run r's relations).ProjectDurationDays`.
3. $$\boxed{\;E \;=\; \sum_{r} \max\big(0,\ D^{imp}_r - D^{as}_r\big)\;}$$

The outer $\max(0,\cdot)$ is defensive only — adding non-negative durations to a max-plus network
can never shorten it — but it makes the impossible case explicit rather than producing a negative
EOT.

**Why summing over runs is correct, and why it is *not* the same as slicing one run into windows.**
Each governing run already embodies the actual progress and re-sequencing up to its own
`CalculatedAt`, so the increments measure disjoint effects on successive baselines — this is the
windows/observational method of AACE RP 29R-03 (MIP 3.3/3.4) with an additive fragnet per window
(MIP 3.7). By contrast, splitting a *single* run's stoppages into arbitrary date windows and summing
is **wrong and understates**: with $TF_j = 3$ and two windows of 2 days each,
$\max(0,2{-}3) + \max(0,2{-}3) = 0$ whereas the correct combined figure is
$\max(0,4{-}3) = 1$. Rule: **one TIA per governing run, never per arbitrary date slice.**

In the common case — one governing run — this reduces to a single pair of `CpmEngine` calls (§5.2a's
collapse is pre-processing on the charge set, not a third call), which is exactly what fixtures W-01
through W-14 and W-17 through W-20 exercise.

### 5.2 Modelling the stoppage as duration, not as calendar

A stoppage is modelled as **+1 working day of duration** on the affected activity, not as a calendar
exception. Both push the finish, but the duration form is right here because `CpmEngine` is
deliberately calendar-agnostic (day-counts, day 0 = project start), and because a calendar exception
in P6 stops **every** activity on that calendar, whereas CM+ attributes the stoppage to the named
activities only (§11). The §3.3 working-day gate is what keeps the two consistent: a Sunday stoppage
adds zero *working* days, so it cannot extend anything.

### 5.2a One day, one path, one charge — the serial-chain collapse

> **Added 2026-08-10, ruling on `qa-engineer`'s S11-QA-01 escalation.** This subsection is the fix for
> the §5.3 breach they reproduced. It is deliberately numbered 5.2a rather than renumbering §5.3/§5.4,
> whose numbers are already cited from `EotEvaluator` and from the fixture table.

**The problem.** §5.1 builds $\Delta D_j$ independently per activity. When one weather entry names two
activities that lie on a **common path**, both increments land on that path and the same lost day is
charged to it twice. Charging N1's **A** and **C** — where `A → C` — on two dates gives
$\Delta D_A = \Delta D_C = 2$, hence $EF_A = 7$, $EF_C = 15$, $EF_D = 19$, $E = 4$ against **two**
days of weather. Four days of delay from two days of storm is not a modelling preference; it is wrong.

**What is actually double-counted.** Extending $A$ already delays $C$: $C$'s start is pushed by the
network logic, so in the impacted world $C$ was never going to be working on those two dates anyway.
Adding $C$'s own two days on top charges the same interruption a second time, once as lost production
and once as displaced logic. Exactly one of the two charges is real.

**The comparability test — edge-type aware, not "is it a predecessor".** For governing run $r$ with
activities $V$ and relations $R$, build the **start/finish graph** $G_r^{\pm}$: two nodes per activity,
$j^{S}$ and $j^{F}$, with

- an **internal edge** $j^{S} \to j^{F}$ for every $j \in V$ — this is where $D_j$, and therefore
  $\Delta D_j$, lives;
- one **relation edge** per $(p,s) \in R$, by type:
  FS $p^{F} \to s^{S}$ · SS $p^{S} \to s^{S}$ · FF $p^{F} \to s^{F}$ · SF $p^{S} \to s^{F}$.
  Lag does not affect reachability and is ignored here.

$$
u \prec_r v \iff v^{S} \text{ is reachable from } u^{F} \text{ in } G_r^{\pm}
$$

In words: *there is a route through the logic on which $u$ must finish before $v$ starts*, so both
durations add into the length of at least one common path. $\prec_r$ is a strict partial order —
transitive through the internal edges, irreflexive because `GraphValidator` has already proved the
activity graph acyclic.

**Do not substitute "$v$ is a transitive successor of $u$" for this test.** It is not the same
relation and the difference is a real defect in both directions:

- N1's **B** and **C** share a predecessor but neither reaches the other — **incomparable**, both
  charges stand (this is why W-07 was always right).
- $P \xrightarrow{\text{SS}} Q$ means $P$ and $Q$ genuinely **overlap**. The SS edge contributes
  $P^{S} \to Q^{S}$, so it puts $Q^{S}$ within reach of $P^{S}$ but **not** of $P^{F}$: where an SS
  (or FF) link is their only connection, the two durations never both enter one path's length, both
  charges are real, and $\prec_r$ correctly leaves them incomparable. A naive predecessor test
  collapses them and under-states $E$ by a day (fixture **W-19** exists to fail exactly that
  implementation). If some *other* route additionally makes $Q^{S}$ reachable from $P^{F}$, they are
  comparable after all and the collapse does apply — which is why the test is reachability over the
  whole start/finish graph, not a per-edge classification.

**The reduction.** For each countable date $d \in \mathcal{D}_r$, let $C_d$ be the activities charged
on $d$ after §3. Build $K_d \subseteq C_d$ greedily:

1. order $C_d$ ascending by $\big(TF_j^{(r)},\ \text{position in } r\text{'s topological order},\ \textit{ActivityCode ordinal}\big)$;
2. walk that order, adding $j$ to $K_d$ iff $j$ is $\prec_r$-incomparable with every activity already
   in $K_d$.

$K_d$ is a maximal antichain of $(C_d, \prec_r)$. Only $K_d$ contributes to $\Delta D_j$ for that date.
Activities in $C_d \setminus K_d$ have that day's charge **absorbed** — never deleted, never excluded,
never given an `ExclusionReason`.

**Why least-float-first, and not "keep the upstream one".** Where two serially-chained activities are
both charged, exactly one of them can be the one that truly lost production on that path, and the
model must choose. The activity with the smaller $TF_j^{(r)}$ sits on the more binding path, so
charging it extends the longest path instead of one with slack to spare. Fixture **W-20** separates
the two rules by the whole answer: charging the upstream activity (float 4) yields $E = 0$; charging
the least-float activity — here the *successor*, float 0 — yields $E = 2$, and 2 is correct, because
the storm did stop the critical activity for two days. A plain "keep the predecessor" rule would have
silently zeroed a genuine entitlement. Ties go **upstream-first**, because at equal float the
predecessor's delay propagates to every downstream branch while the successor's does not (**W-17**).

**Deliberately not a tie-break: "prefer the activity that had actually started."** Attractive, and
rejected — deciding that a not-yet-started activity should yield to its running predecessor is a
judgement about whose fault the non-start was, i.e. a concurrency argument, and §3.7/§7 keep those out
of Sprint 11. If **Q4** is answered and §3.7 tightens to require `ActualStart`, an activity that had
not started stops being charged at §3 and this question disappears by itself.

**Nothing is hidden.** The absorbed activity still gets its `EotEvaluationDriver` row, carrying
`StoppageDays = 0`, `SerialChainAbsorbedDays = n` and `AbsorbedIntoActivityCodes` (§5.4); the
evaluation carries `SerialChainAbsorbedDayCount`. The screen must say which activity absorbed which,
in words — e.g. *"2 วันของกิจกรรม C ถูกนับรวมกับกิจกรรม A ซึ่งอยู่ในสายงานเดียวกัน (วันเดียวกันนับซ้ำไม่ได้)"* —
because a driver row reading `StoppageDays = 0` next to a weather log that plainly names the activity
looks like a bug otherwise.

**What the collapse guarantees, and the one thing it does not.**

- **It never over-states.** For every path $\pi$, $\sum_{j \in \pi} \Delta D_j \le$ the number of dates
  touching $\pi$. For a delay analysis that is the correct direction to err in: an under-stated figure
  survives challenge, an over-stated one does not (§2.2, ADR-0020).
- **It is exact whenever each date's charged set is a chain** — which is every fixture here and, in
  practice, effectively every real entry, since a site engineer names the two or three activities his
  crews were actually on.
- **It is not exact in general.** The exact figure is
  $\max_\pi \big(\text{len}(\pi) + |\{ d : C_d \cap \pi \ne \varnothing \}|\big)$, whose objective is
  **not additive along the path** — a date already counted must not be counted again — so it cannot be
  read off a single longest-path pass, and evaluating it exactly requires path enumeration. Where the
  collapse under-states it does so by at most one day per date, and only on a path that reaches an
  absorbed activity without passing through the activity that was kept. Open question **Q12**; the
  absorbed days are on the record either way.

**Cost.** Charged activities per run are a handful, not 10,000. One BFS from $j^{F}$ over the run's own
relations per charged activity per run decides every pair. No transitive closure of the full network,
and no change to `CpmEngine`.

### 5.3 The cap: one calendar day, one EOT day — and the two hypotheses it rests on

$$
E \;\le\; \big|\{\, d \in [w_0,w_1] : d \text{ is countable for at least one activity} \,\}\big|
$$

**The same calendar day can never yield more than one EOT day, no matter how many activities it
stopped.** This is a statement about the physical world before it is a statement about the model: work
advances along a path one activity at a time, so a day on which the site stopped pushes any given path
by at most one day, and the project — a maximum over paths — by at most one day. Two days of storm
cannot delay completion by four.

> **Ruling (2026-08-10, `qa-engineer`'s S11-QA-01 escalation).** The alternative offered was to scope
> this cap to mutually-independent activities and accept $E = 4$ on two days of weather. **Declined.**
> The inequality is not an artefact of how CM+ happens to compute; it is the physics the computation
> is meant to model, and it errs in the one direction — over-granting — that destroys a claim on first
> challenge. **A computed number that breaches this cap is a defect in the computation, and the cap is
> never to be relaxed to accommodate it.**

It is an inequality, not an identity: $E$ is strictly **less** than the day count whenever float
absorbs part of the loss (W-02, W-03, W-18c), and **equal** to it when every countable day lands on
the longest path (W-01, W-07, W-17, W-19, W-20).

**The cap is a theorem, and these are its hypotheses.** It holds for exactly two reasons, each of
which must be enforced elsewhere in this document or the cap does not hold at all:

| | Hypothesis | Enforced by | Fixture |
| :-- | :-- | :-- | :-- |
| **H1** | No (activity, date) pair contributes more than **one** day of duration. | §3.4's clamp $\min(h_{j,d}, H)$ | **W-18** |
| **H2** | On any one date, at most **one** activity per path is charged. | §5.2a's serial-chain collapse | **W-17**, **W-19**, **W-20** |

*Proof.* Write $\Delta D_j = \sum_d w_{j,d}$. By **H1**, $w_{j,d} \le 1$. Let $\pi$ be any path of
governing run $r$. By **H2**, for a fixed date $d$ at most one $j \in \pi$ has $w_{j,d} > 0$, so
$\sum_{j \in \pi} \Delta D_j \le |\mathcal{D}_r|$. Therefore

$$
D^{imp}_r \;=\; \max_\pi \Big(\text{len}(\pi) + \sum_{j\in\pi}\Delta D_j\Big)
\;\le\; \max_\pi \text{len}(\pi) \;+\; |\mathcal{D}_r| \;=\; D^{as}_r + |\mathcal{D}_r|
$$

so $E_r \le |\mathcal{D}_r|$. Countable dates are partitioned by governing run (§5.1 step 1), so
summing over runs gives $E \le \sum_r |\mathcal{D}_r|$ = the distinct countable date count. $\blacksquare$

**Why W-07 never caught the H2 breach — and why that was a defect in this document, not in the code.**
W-07 charges **B** and **C**, which in network N1 sit on *parallel* branches and are $\prec_r$-
incomparable, so it satisfies H2 by accident and the cap held with nothing enforcing it. Across W-01 to
W-16 no fixture ever charged two activities standing in a predecessor/successor relationship, so the
hypothesis was invisible and the inequality was written down as if unconditional. `qa-engineer`
constructed that shape directly (A and C in N1, two dates, $E = 4$ against 2 countable dates) and
escalated rather than editing the assertion — which is the rule working as intended. **An invariant
published without its hypotheses is the specification's defect.** W-17 through W-20 now pin both
hypotheses and both directions of failure.

**Assert the inequality in every fixture** — it is cheap and it is the single most effective guard
here. An implementation that sums per-activity figures breaches it: in W-07 that sum is 7 against 5
distinct countable dates, where the network answer is 5. But the cap is **necessary, not sufficient**:
it bounds the answer, it does not verify it, so it never substitutes for a fixture's own expected
value — W-20's *wrong* answer of 0 satisfies the cap comfortably.

### 5.4 Explainability — "อธิบายได้ว่าอ้างกิจกรรมใด"

> S11-BE-02 DoD: *"ผลลัพธ์อธิบายได้ว่าอ้างกิจกรรมใด"*

Every evaluation emits one `EotEvaluationDriver` row per affected activity:

| Field | Meaning |
| :-- | :-- |
| `ActivityId`, `ActivityCode`, `Name` | who |
| `StoppageDays` $S_j$ | countable days **charged** to $j$ — i.e. after §5.2a's collapse. This is the $\Delta D_j$ that went into the network |
| `SerialChainAbsorbedDays` | countable days recorded against $j$ that §5.2a absorbed into a serially-chained activity. `0` for every fixture except W-17 and W-20 |
| `AbsorbedIntoActivityCodes` | `nvarchar(200) NULL` — the distinct codes that absorbed them, in topological order; `NULL` when `SerialChainAbsorbedDays = 0` |
| `TotalFloatAtRun` $TF_j^{(r)}$ | the float it had **at the governing run** — not the live value |
| `WasCriticalAtRun` | $TF_j^{(r)} = 0$ |
| `IsOnImpactedCriticalPath` | critical in the *impacted* network — this is how criticality **shift** becomes visible (W-03) |
| `IndicativeEotDays` | $\max\!\big(0,\ S_j - TF_j^{(r)}\big)$ — computed from the **charged** $S_j$, so a fully-absorbed activity reads 0 |
| `MarginalEotDays` | $D^{imp} - D^{imp \setminus j}$: re-run with **only** $j$'s charged stoppages removed. Necessarily 0 for a fully-absorbed activity, which is correct: removing a charge that was never applied changes nothing |
| `RemainingFloatAfter` | $\max\!\big(0,\ TF_j^{(r)} - S_j\big)$, and the impacted-network $TF_j$ alongside it |

$S_j$ + `SerialChainAbsorbedDays` = the countable days the *evidence* records against $j$; $S_j$ alone
is what the *schedule* charged. Both must be visible, and the screen must name the activity that
absorbed the difference — see §5.2a.

**Neither per-activity column is required to sum to $E$, and the network figure always governs.**
State this on the screen, because the arithmetic looks broken otherwise. In W-07:
$\sum_j \textit{Indicative} = 7$, $\sum_j \textit{Marginal} = 3$, $E = 5$. All three are correct
answers to three different questions. Two provable relations to assert:

$$\max_j \textit{MarginalEotDays}_j \;\le\; E \qquad\text{and}\qquad \textit{IndicativeEotDays}_j \;\ge\; \textit{MarginalEotDays}_j$$

The single-activity closed form $E = \max(0, S_j - TF_j^{(r)})$ — which is exactly the DoD's
"critical → EOT / non-critical → float only" rule, since a critical activity has $TF_j = 0$ — is
**exact when only one activity is affected** and is the arithmetic cross-check for fixtures W-01,
W-02, W-03. It is *not* the algorithm.

---

## 6. Float consumption, and the case everyone gets wrong

### 6.1 The rule

For a non-critical activity $j$, each countable stoppage day consumes one day of total float:

$$
TF_j^{rem} \;=\; \max\!\big(0,\ TF_j^{(r)} - S_j\big),
\qquad
\textit{IndicativeEotDays}_j \;=\; \max\!\big(0,\ S_j - TF_j^{(r)}\big)
$$

**The two halves are complementary and one of them is always zero.** As long as
$S_j \le TF_j^{(r)}$ the stoppage is absorbed and $E$ contributes nothing — the DoD's "กิน float
เท่านั้น (ไม่ให้ EOT)". The instant $S_j > TF_j^{(r)}$, the activity **has become critical** and every
further day is EOT-eligible.

> **This is the case most likely to be got wrong.** The naive implementation reads
> `Activity.IsCritical == false` once, returns 0, and is wrong from the $(TF_j{+}1)$-th day onward.
> Fixture **W-03** is built precisely to fail it: 5 stoppage days on an activity with 3 days of
> float. Naive answer **0**; "count every stoppage day" answer **5**; correct answer **2**.

Because §5 recomputes the whole network, this emerges automatically — including the *criticality
swap*: in W-03 the impacted critical path moves from **A→C→D** to **A→B→D**, C's float rises from 0
to 2, and B's falls from 3 to 0. Report that swap; it is the most useful single sentence the feature
produces.

### 6.2 Float is a property of the path, not of the activity

Two activities on the same non-critical chain each showing $TF = 5$ **share** those five days. If
the first consumes 3, the second has 2 left, not 5. Any implementation that keeps a per-activity
float counter and decrements it independently **overstates available float and understates $E$**.
The network method has no such counter and is immune by construction — which is the principal reason
it is mandated over hand-rolled float bookkeeping.

### 6.3 Float ownership

Whether float "belongs to" the project (first come, first served) or is apportioned is contested.
The SCL Protocol 2nd ed. **Core Principle 8** takes the orthodox position: unless the contract says
otherwise, **float is not owned by either party** and is available to the project, so an Employer
Risk Event that merely consumes float without delaying completion gives **no EOT**. That is exactly
the rule above, and it is what the DoD states. Adopted; no configuration offered. If a client's
contract contains an express float-ownership clause, raise it as a new decision — do not bend §6.1.

---

## 7. Concurrent delay — **explicitly out of scope for Sprint 11**

Two different things are called "concurrency" here, and only one of them is out of scope.

### 7.1 In scope: two weather stoppages on the same day, on different activities

This is **not** concurrent delay. It is one weather event with several effects, all owned by the
same risk-holder. §5 handles it in a single network run, and §5.3 caps the result at one EOT day per
calendar day. Fixture **W-07**. Nothing further is needed.

### 7.2 Out of scope: a weather day overlapping a contractor-caused delay

**Ruled out of scope. `ConcurrencyAssessed` is hard-coded `false` on every Sprint 11 evaluation, and
the disclosure in §2.2 must state it.** Three reasons, in order of weight:

1. **CM+ has no delay-event register to be concurrent with.** The only dated delay evidence in the
   system after Sprint 11 is the weather log itself. `IssueLog` is an action tracker — it has no
   risk-owner attribution, no delay duration, and no link to an activity. There is nothing to
   compare against, so any "concurrency" the code claimed to find would be invented.
2. **The law is genuinely unsettled and contract-dependent.** Under English law the *Malmaison*
   approach (Henry Boot v Malmaison [1999]) grants EOT but not prolongation cost, and is adopted by
   **SCL Protocol 2nd ed. Core Principle 10** ("the Contractor's concurrent delay should not reduce
   any EOT due") with **Core Principle 14** treating concurrency as a defence to *cost* rather than
   *time*. Scots law in *City Inn Ltd v Shepherd Construction Ltd* [2010] CSIH 68 permits
   **apportionment** — a materially different answer on the same facts. Thai courts have no settled
   equivalent doctrine. Choosing one in code, silently, would be the system taking a legal position
   on the user's behalf.
3. **Getting it wrong is invisible to the user.** An under-grant looks like a correct small number.

**What Sprint 11 does instead:** nothing implicit. The evaluation states that concurrency was not
assessed. **Do not** add a heuristic "possible concurrency" flag derived from open `IssueLog` rows —
a half-signal from a register that was never designed to carry risk-ownership is worse than an
honest absence, because users will act on it.

**What a future sprint needs** (open question **Q4**): a `DelayEvent` register with
`CauseParty enum { Employer, Contractor, Neutral }`, a dated window, affected activities, and a
per-project `ConcurrencyDoctrine enum { Malmaison, Apportionment, DominantCause }`. Only then can
concurrency be computed rather than guessed.

---

## 8. `DailyWeatherLog` immutability and the correction chain (S11-BE-01)

> S11-BE-01 DoD: *"ไม่มี update/delete endpoint; การแก้ = entry ใหม่ที่อ้างถึงของเดิม; ทุกการบันทึกมี audit"*

### 8.1 Entity shape

```
DailyWeatherLog                          -- append-only (IAppendOnly + INeverModified)
  Id, TenantId, ProjectId
  LogDate            DateTimeOffset      -- calendar-day identity (WorkingCalendar's convention)
  Condition          enum WeatherCondition { Clear, Cloudy, LightRain, ModerateRain, HeavyRain, Storm, Flood, Other }
  ConditionNote      nvarchar(200) NULL
  RainfallMm         decimal(6,2)  NULL  -- 24-hour depth at the site; NULL = not measured, never 0
  Impact             enum WeatherImpact { NoImpact = 0, PartialStoppage = 1, FullStoppage = 2 }
  ImpactNote         nvarchar(500) NULL
  HoursLost          decimal(4,2)  NULL  -- required by validation when Impact <> NoImpact
  RecordedByUserId   Guid
  RecordedAt         DateTimeOffset      -- server clock (IDateTimeProvider), never client-supplied
  EntryKind          enum { Original = 1, Correction = 2, Retraction = 3 }
  CorrectsWeatherLogId Guid NULL         -- required iff EntryKind <> Original
  CorrectionReason   nvarchar(500) NULL  -- required iff EntryKind <> Original
  -- NO SupersededBy column. NO UpdatedAt. NO IsDeleted. See §8.2.

DailyWeatherLogActivity                  -- append-only child; the named affected activities
  DailyWeatherLogId, ActivityId
```

Fields deliberately **absent**: any nullable back-pointer or status column that a later write would
have to set. Every column is written once, at insert.

### 8.2 Supersession is derived, never stamped

The obvious design — stamp `SupersededByWeatherLogId` on the original when a correction arrives —
requires an `UPDATE` on a table whose entire value is that it is never updated. Rejected.

**Design: a forward pointer only.** A correction points at its target; the target is untouched. The
effective set is computed:

$$
\mathcal{L}^{eff} \;=\;
\Big\{\, \ell \in \mathcal{L} \;:\;
\nexists\, \ell' \in \mathcal{L},\ \ell'.\textit{Corrects} = \ell.\textit{Id}
\;\wedge\; \ell.\textit{EntryKind} \ne \texttt{Retraction} \,\Big\}
$$

In words: an entry is in force iff nothing points at it **and** it is not itself a retraction. A
`Retraction` therefore removes both itself and its target from $\mathcal{L}^{eff}$ — which is what
"this entry should never have existed" means, and it is strictly different from a `Correction`
carrying zeroed content (which asserts "there was weather, but no lost time").

Chain integrity rules — all enforced, all 4xx, none silent:

| # | Rule | Violation |
| :-- | :-- | :-- |
| 1 | `CorrectsWeatherLogId` must resolve within the **same tenant and project** | 422 `WeatherLogCorrectionTargetNotFound` |
| 2 | **At most one entry may point at any given entry** — DB: `UNIQUE (TenantId, CorrectsWeatherLogId) WHERE CorrectsWeatherLogId IS NOT NULL` | 409 `WeatherLogAlreadySuperseded` |
| 3 | A second correction must target the **current tail** of the chain, not the original | consequence of (2) — W-10 asserts it |
| 4 | The target must already exist and be older: `target.RecordedAt < this.RecordedAt`, and `CorrectsWeatherLogId <> Id` | 422 `WeatherLogCorrectionOrdering` |
| 5 | `CorrectionReason` is **mandatory** on `Correction` and `Retraction` | 422 |
| 6 | A correction **may** change `LogDate`, `Impact`, `HoursLost`, `RainfallMm` and the affected-activity list — the correction's own values govern completely. It replaces; it does not patch. | — |
| 7 | Chains cannot cycle: (2) forbids branching and (4) forces strictly increasing `RecordedAt`, so the chain is a strictly-ordered list | structural |

Rule 2 is the load-bearing one. Without it a fork makes "which entry is current" ambiguous and the
EOT total becomes a function of iteration order.

### 8.3 API surface and the immutability test

Only two writes exist:

- `POST /api/v1/projects/{id}/weather-logs` → `EntryKind = Original`
- `POST /api/v1/projects/{id}/weather-logs/{logId}/corrections` → `EntryKind ∈ {Correction, Retraction}`,
  with `CorrectsWeatherLogId = {logId}` taken from the route, never from the body

There is **no** `PUT`, `PATCH` or `DELETE` route. S11-QA-01's *"พยายามแก้/ลบ weather log → ถูกปฏิเสธ"*
is satisfied by a deliberate **405** carrying ProblemDetails `WeatherLogIsImmutable` and a Thai
message pointing at the corrections endpoint — better than a bare routing 404/405, because the field
user needs to be told what to do instead. Defence in depth, all three layers required:

1. no route;
2. no mutator on the entity (constructor-only, `private set` throughout, `internal` constructor
   reachable only through a factory — the `ActivityProgressLog` pattern, which is already proven in
   this codebase);
3. `AppendOnlyGuardInterceptor` blocks `EntityState.Modified` and `Deleted` at `SavingChanges`
   (`IAppendOnly` / `INeverModified`, per the N-01 fix).

Every insert writes an `AuditLog` row with `Action = Create`. **Invariant for QA:** the `AuditLog`
table never contains an `Update` or `Delete` row for `EntityName = 'DailyWeatherLog'`, over the whole
database, ever. One query, permanent regression guard.

The offline PWA (S13-FE-01) queues weather writes; the `Idempotency-Key` (S13-BE-01) prevents a
retried submission from creating a duplicate entry. Until Sprint 13, a duplicate is a data issue to
be fixed with a `Retraction`, not a delete.

### 8.4 Who may correct

Any user with the weather-recording role, with a mandatory reason. **No approval chain** — see §8.6.
The correcting user and `RecordedAt` are on the row, so the chain is self-evidencing. Whether a PM
countersignature should be required on corrections is open question **Q8** (low stakes; the audit
trail already makes the change visible and attributable).

### 8.5 What a correction does to an EOT total already computed

> The question the human asked, answered precisely.

An `EotEvaluation` is an **immutable snapshot** — the `EvmPeriodSnapshot` / ADR-0009 pattern:

```
EotEvaluation                            -- append-only (IAppendOnly)
  Id, TenantId, ProjectId
  WindowStart, WindowEnd, EvaluatedAt, EvaluatedByUserId
  CriticalityBasis enum { Contemporaneous, Mixed, Retrospective }
  Confidence       enum { Substantiated, Provisional }
  AsScheduledDurationDays int, ImpactedDurationDays int
  EotEligibleDays  int                   -- E
  CountableStoppageDayCount int          -- stoppage days actually CHARGED into the network, i.e. sum of DeltaD_j after §5.2a
  SerialChainAbsorbedDayCount int        -- days that passed §3 but §5.2a absorbed into a serially-chained activity; 0 in every fixture except W-17/W-20
  DistinctCountableDateCount int, UnattributedStoppageDayCount int
  ConcurrencyAssessed bit (always 0), EntitlementBasisAssessed bit (always 0)
  PolicySnapshotJson nvarchar(max)       -- every ProjectEotPolicy value used, pinned (incl. FullDayHours, which §3.4's clamp reads)
  LatestNoticeDate DateTimeOffset NULL, NoticeWindowExpired bit NULL
EotEvaluationRun     (child)  EotEvaluationId, CpmRunId, WindowFrom, WindowTo, AsScheduled, Impacted, Delta
EotEvaluationSource  (child)  EotEvaluationId, DailyWeatherLogId, CountableDays decimal(5,2), ExclusionReason enum NULL,
                              HoursLostClampedToFullDay bit    -- §3.4: recorded HoursLost exceeded FullDayHours and was charged as FullDayHours (W-18)
EotEvaluationDriver  (child)  as §5.4
```

`CountableStoppageDayCount` counts what the schedule was charged; `SerialChainAbsorbedDayCount` counts
what the evidence recorded but the network could not accept twice. Their **sum** is the number of
(activity, day) stoppages that cleared all six §3 gates, and it is that sum — not the charged figure —
that must reconcile against the weather log when a reviewer audits the evaluation.

`PolicySnapshotJson` pins the configuration exactly as `ApprovalPolicyVersion` pins routing
(ADR-0008): a two-year-old EOT figure must remain explainable in a dispute even after the project's
thresholds were changed.

**Rules:**

1. **A correction never mutates a stored evaluation.** Evaluation #1's `EotEligibleDays` stays what
   it was, forever. It is the record of what was computed, and asserted, on that date.
2. **A correction makes the evaluation `Stale`** — computed, not stored:

   $$
   \textit{Stale}(V) \iff
   \Big(\exists\, \ell \in \textit{sources}(V) : \ell \notin \mathcal{L}^{eff}\Big)
   \ \vee\
   \Big(\exists\, \ell \in \mathcal{L}^{eff} : \ell.\textit{LogDate} \in [w_0,w_1] \wedge \ell \notin \textit{sources}(V)\Big)
   \ \vee\
   \Big(\exists\, r \in \mathcal{R} : r.\textit{CalculatedAt} > V.\textit{EvaluatedAt}\Big)
   $$

   i.e. a consumed entry was superseded/retracted, **or** a new entry landed in the window, **or**
   the schedule has been recalculated since. All three must be checked — the third is the one that
   is forgotten, and it is the one that matters after a VO approval re-runs CPM.
3. **Stale evaluations are displayed with the staleness reason** and a "คำนวณใหม่" action. The
   remedy is always a **new** evaluation record. Never overwrite.
4. **If a claim was already submitted on a now-stale evaluation**, the evaluation stays exactly as
   it is, as evidence of what was submitted, and the correction is disclosed alongside it.
   Rewriting history here would be the single worst thing this module could do — a tamper-evident
   log whose downstream figures are quietly restated is not tamper-evident.

Fixture **W-10** walks the whole cycle: 1 day → correction → stale → re-run → 0 days.

### 8.6 Weather logs and EOT do **not** route through the approval engine

The instruction asked whether weather-driven EOT should use `approval-workflow.md`'s engine.
**Ruling: no, not in Sprint 11.**

- The engine routes on a **money** value: $A^{route} = |Amount|$ for a VO, $G_k$ for a certificate
  (approval-workflow.md §5.3 step 1), matched against `MinAmount`/`MaxAmount` bands. A weather log
  has no amount, and an EOT evaluation has no amount. Feeding it 0.00 would route every entry to the
  lowest tier of the money matrix — meaningless authority, and it would pollute the
  `ApprovalAction` ledger, which is defined as "a human decision act" on a financial document.
- Nothing in Sprint 11 *is* a document requiring approval. A weather log is a contemporaneous
  record (its authority comes from being immutable and dated, not from being signed), and the
  evaluator's output is advisory (§2.1).

**When a formal EOT Claim document is built** (a later sprint — `docs/วิเคราะห์ฯ` §5 anticipates
"แบบฟอร์มคำขอสิทธิ์ขอขยายระยะเวลาก่อสร้าง"), it *should* use the engine, and at that point
`ApprovalPolicy` needs either a non-money `RoutingBasis` (e.g. banded on **days claimed**) or a
fixed chain for the document type. Flagged now so the schema decision is not made by accident:
open question **Q9**.

---

## 9. `IssueLog` state machine (S11-BE-03)

> S11-BE-03 DoD: *"`Open`→`Doing`→`Closed`; `ClosedAt` ถูกประทับเฉพาะตอนเข้า `Closed`; ตัวนับ tile ตรงกับข้อมูลจริง"*

### 9.1 States and transitions

```
  [Open] ──advance──> [Doing] ──advance──> [Closed]   (terminal)
     │                   │                     │
     └── advance ────────┘                     └── advance → 409 IssueAlreadyClosed
        (skip not permitted → 409)             └── reopen  → 409 IssueIsClosed
```

`IssueStatus { Open = 1, Doing = 2, Closed = 3 }`. The single verb is
`POST /api/v1/projects/{id}/issues/{issueId}/advance-status` (docs/9. §5).

**Skipping `Doing` is not permitted.** `advance-status` advances exactly one rung. Rationale:
(i) it matches the endpoint's own semantics and the DoD's literal sequence; (ii) the tile counts are
the point of the feature and `Doing` is what makes "someone is on it" visible; (iii) closing straight
from `Open` erases the information that nobody ever worked the issue — which for an action log is
exactly the signal a PM is looking for. Cost of being wrong: one extra click. Low-stakes; recorded
as **Q10**.

**Reopening is not permitted.** `Closed` is terminal. A recurrence is a **new issue** carrying
`RelatedIssueId = <original>`. Three reasons:

1. The DoD requires `ClosedAt` to be stamped **only on entry to `Closed`**. Reopening forces a
   choice between clearing it (destroying the record that the issue was closed on 14 July) and
   keeping it on a non-closed issue (a contradictory row). Neither is acceptable.
2. Tile counts stop being monotone and "closed this month" reporting silently breaks.
3. It is consistent with the codebase's own established rule — approval-workflow.md §2:
   terminal states are terminal, corrections are new records, never edits. Weather logs (§8) work
   the same way. One rule across the product is worth more than local convenience here.

Unlike a weather log, an issue is not a legal instrument, so this is a **convention** choice rather
than an evidentiary one. Recorded as **Q11** with the default stated.

### 9.2 Timestamps

| Field | Rule |
| :-- | :-- |
| `StartedAt DateTimeOffset NULL` *(recommended addition)* | set exactly once, on entry to `Doing`, from `IDateTimeProvider`. Enables cycle-time metrics; symmetric with `ClosedAt`. |
| `ClosedAt DateTimeOffset NULL` | set exactly once, on entry to `Closed`, from `IDateTimeProvider`. **Never client-supplied** — a field device with a wrong clock (or a client that sets it deliberately) would back-date a closure. |

Invariants, both enforceable as DB `CHECK` constraints and asserted by QA:

$$\textit{Status} = \texttt{Closed} \iff \textit{ClosedAt} \ne \texttt{NULL}$$
$$\textit{Status} \in \{\texttt{Doing},\texttt{Closed}\} \iff \textit{StartedAt} \ne \texttt{NULL}$$
$$\textit{StartedAt} \le \textit{ClosedAt} \quad\text{when both are set}$$

Every transition writes an `AuditLog` row and takes the `RowVersion` optimistic-concurrency token —
two users advancing simultaneously means the second gets **409**, never a double-advance (the same
discipline as the approval handlers).

### 9.3 Tile counts must match the table

> *"ตัวนับ tile ตรงกับข้อมูลจริง"*

Two classic bugs, both ruled out by one rule:

> **Counts are computed server-side over the *unpaginated*, *identically-filtered* set and returned
> in the same response as the page. The client never derives tiles from the rows it received.**

The counts query and the table query must differ **only in projection** — same tenant scope, same
project, same filters, same date range. Response shape:

```
GET /api/v1/projects/{id}/issues?status=&owner=&page=1&pageSize=20
{ "items": [ ...20 rows... ], "totalCount": 24,
  "statusCounts": { "open": 7, "doing": 3, "closed": 14 } }
```

Invariants: $n_{Open} + n_{Doing} + n_{Closed} = \textit{totalCount}$, there is no fourth status, and
applying a filter must move **both** the rows and the tiles. Fixture **W-12c** asserts exactly this.

### 9.4 Relationship to the weather log

The prototype's ISS-024 ("พบคราบน้ำหลังฝนตกหนัก 8 ก.ค.") references weather narratively. An optional
`RelatedWeatherLogId Guid NULL` is a reasonable convenience. It carries **no** semantics for §5 — in
particular it does **not** make the issue a delay event (§7.2).

---

## 10. Fixtures — S11-QA-01 builds directly from these

### 10.0 Shared setup (all fixtures unless stated)

**Network N1** — `cpm-method.md`'s canonical fixture **verbatim**, so these tests tie to Sprint 5's
existing golden values. All relations FS, lag 0:

$$A(5) \to B(3) \to D(4); \qquad A(5) \to C(6) \to D(4)$$

| | A | B | C | D |
| :-- | --: | --: | --: | --: |
| ES / EF | 0 / 5 | 5 / 8 | 5 / 11 | 11 / 15 |
| LS / LF | 0 / 5 | 8 / 11 | 5 / 11 | 11 / 15 |
| **TF** | **0** | **3** | **0** | **0** |
| Critical | yes | **no** | yes | yes |

$D^{as} = 15$ working days. Critical path **A → C → D**.

**Calendar `TH-6Day`:** `WorkingDays = Mon|Tue|Wed|Thu|Fri|Sat`; Sunday non-working.
One `CalendarException(2026-07-28, IsWorkingDay = false, "วันเฉลิมพระชนมพรรษา")`.
July 2026 days of week: **1 Wed, 4 Sat, 5 Sun, 8 Wed, 11 Sat, 12 Sun, 13 Mon, 19 Sun, 20 Mon,
21 Tue, 26 Sun, 28 Tue**.

**Policy (defaults):** `CountingBasis = AbsoluteStoppageDays`, `PartialDayPolicy = ThresholdWholeDay`,
`FullDayHours = 8.00`, `MinHoursLostForCountableDay = 4.00`,
`MinRainfallMmForCountableDay = NULL`, `NoticePeriodDays = NULL`.

**CPM run:** one run `R1`, `CalculatedAt = 2026-07-01T00:00:00+07:00`, network N1
⟹ `CriticalityBasis = Contemporaneous`, `Confidence = Substantiated`.
**Window:** `[2026-07-01, 2026-07-31]`. All activities in progress (`ActualStart` set, `ActualFinish`
null) unless the fixture says otherwise.

---

### W-01 — critical activity, one full stoppage day ⟹ 1 EOT day

| Input | |
| :-- | :-- |
| 2026-07-08 (Wed) | activity **C**, `Impact = FullStoppage`, `HoursLost = 8.00`, `RainfallMm = 61.00` |

- Countable days: **1**. $S_C = 1$, $TF_C^{(R1)} = 0$.
- Impacted: $C: 6 \to 7$ ⟹ $EF_C = 12$, $ES_D = 12$, $EF_D = 16$ ⟹ $D^{imp} = 16$.
- $$E = 16 - 15 = \mathbf{1}$$
- Driver C: `WasCriticalAtRun = true`, `Indicative = max(0, 1−0) = 1`, `Marginal = 1`,
  `IsOnImpactedCriticalPath = true`, `RemainingFloatAfter = 0`.
- Cap check: $E = 1 \le 1$ distinct countable date. ✓

### W-02 — non-critical activity within float ⟹ 0 EOT, float consumed

| Input | |
| :-- | :-- |
| 2026-07-08 (Wed) | activity **B**, `FullStoppage`, `HoursLost = 8.00` |

- $S_B = 1$, $TF_B^{(R1)} = 3$.
- Impacted: $B: 3 \to 4$ ⟹ $EF_B = 9$, $ES_D = \max(9, 11) = 11$, $EF_D = 15$ ⟹ $D^{imp} = 15$.
- $$E = 15 - 15 = \mathbf{0}$$
- Driver B: `Indicative = max(0, 1−3) = 0`, `Marginal = 0`, `WasCriticalAtRun = false`,
  `IsOnImpactedCriticalPath = false`, **`RemainingFloatAfter = 2`** (impacted $TF_B = 7-5 = 2$).
- **Assert the persisted `Activity.TotalFloat` for B is still 3** — the consumption is reported, not
  written (§2.1).

### W-03 ★ — repeated stoppages exhaust float; the activity becomes critical

**The fixture where the naive reading and the correct reading disagree. Build this one first.**

| Input | |
| :-- | :-- |
| 2026-07-08 (Wed), 07-09 (Thu), 07-10 (Fri), 07-11 (Sat), 07-13 (Mon) | activity **B**, all `FullStoppage`, `HoursLost = 8.00` |

All five are working days under `TH-6Day` (note **Saturday 11 July counts**; Sunday 12 July is not
in the list). $S_B = 5$, $TF_B^{(R1)} = 3$.

| Reading | Answer | Why it is wrong |
| :-- | --: | :-- |
| Naive "B is not critical ⟹ no EOT" | **0** | reads `IsCritical` once and stops |
| Naive "count the stoppage days" | **5** | ignores float entirely |
| **Correct** | **2** | float absorbs 3, the excess 2 pushes completion |

- Impacted: $B: 3 \to 8$ ⟹ $EF_B = 13$, $ES_D = \max(13, 11) = 13$, $EF_D = 17$ ⟹ $D^{imp} = 17$.
- $$E = 17 - 15 = \mathbf{2}, \qquad \textit{Indicative}_B = \max(0,\ 5 - 3) = 2 \ \checkmark$$
- **Criticality swap — assert all of it:** impacted $TF_B = 5-5 = \mathbf{0}$ (now critical),
  impacted $TF_C = 7-5 = \mathbf{2}$ (no longer critical), $TF_A = 0$, $TF_D = 0$.
  **Impacted critical path = A → B → D.**
- `RemainingFloatAfter`$_B = \max(0, 3-5) = 0$.
- **Assert no persistence:** `Activity.IsCritical` for B is still `false`; for C still `true`;
  `Activity.TotalFloat` still 3 and 0 respectively (§2.1, W-14).

### W-04 — non-working days and holidays are not countable

| Input | Expected |
| :-- | :-- |
| 2026-07-11 (**Sat** — working under TH-6Day), **C**, `FullStoppage`, 8.00 h | **countable** |
| 2026-07-12 (**Sun**), **C**, `FullStoppage`, 8.00 h | excluded — `NonWorkingDay` |
| 2026-07-28 (**Tue, holiday exception**), **C**, `FullStoppage`, 8.00 h | excluded — `CalendarHoliday` |

- $S_C = 1$ ⟹ $C: 6 \to 7$ ⟹ $D^{imp} = 16$ ⟹ $$E = \mathbf{1}$$
- Three source rows persisted; two carry an `ExclusionReason`. **Nothing is silently dropped.**

**W-04b — the exception overrides in the *other* direction.** Same three entries, plus
`CalendarException(2026-07-12, IsWorkingDay = true, "เทคอนกรีตวันอาทิตย์ตามแผน")`.

- Now 11 and 12 July both count: $S_C = 2$ ⟹ $C: 6 \to 8$ ⟹ $EF_C = 13$, $EF_D = 17$ ⟹
  $$E = \mathbf{2}$$
- This is the direction implementations forget. It must pass.

### W-05 — partial-day policy: one dataset, three answers

Activity **C** (critical), all working days, `RainfallMm` irrelevant (threshold NULL):

| Date | HoursLost |
| :-- | --: |
| 2026-07-06 (Mon) | 3.50 |
| 2026-07-07 (Tue) | **4.00** (exactly the threshold) |
| 2026-07-08 (Wed) | 6.00 |

| `PartialDayPolicy` | $S_C$ | $C$ | $D^{imp}$ | $E$ |
| :-- | --: | --: | --: | --: |
| **`ThresholdWholeDay`** (default) — 4.00 counts, `≥` inclusive | **2** | 8 | 17 | **2** |
| `FractionalAccrual` — $\lfloor (3.50{+}4.00{+}6.00)/8.00 \rfloor = \lfloor 1.6875 \rfloor$ | **1** | 7 | 16 | **1** |
| `FullDayOnly` — needs $\ge 8.00$ | **0** | 6 | 15 | **0** |

Under `FractionalAccrual`, `UnclaimedFractionalHours = 5.50` is reported and **not** carried
forward. Whichever policy the human picks, the other two become negative tests.

### W-06 — contract rainfall-depth threshold

`MinRainfallMmForCountableDay = 20.00`. Activity **C**, all working days, all `FullStoppage`,
`HoursLost = 8.00`:

| Date | RainfallMm | With threshold = 20.00 | With threshold = NULL (default) |
| :-- | --: | :-- | :-- |
| 2026-07-06 | 18.40 | excluded — `BelowRainfallThreshold` | countable |
| 2026-07-07 | **20.00** | **countable** (`≥` inclusive) | countable |
| 2026-07-08 | 42.50 | countable | countable |
| 2026-07-09 | **NULL** | excluded — `RainfallNotMeasured` (fail-closed) | countable |

- Threshold 20.00: $S_C = 2$ ⟹ $C: 6 \to 8$ ⟹ $D^{imp} = 17$ ⟹ $$E = \mathbf{2}$$
- Threshold NULL: $S_C = 4$ ⟹ $C: 6 \to 10$ ⟹ $EF_C = 15$, $EF_D = 19$ ⟹ $$E = \mathbf{4}$$
- With `CountUnmeasuredRainfallWhenThresholdSet = 1` and threshold 20.00: 9 July counts ⟹ $S_C = 3$
  ⟹ $C: 6 \to 9$ ⟹ $D^{imp} = 18$ ⟹ $E = \mathbf{3}$.

### W-07 ★ — two activities stopped on the same days: the double-count guard

Five dates — 2026-07-08, 07-09, 07-10, 07-11, 07-13 — each entry naming **both B and C**,
`FullStoppage`, 8.00 h. $S_B = S_C = 5$; $TF_B = 3$, $TF_C = 0$.

**B and C are $\prec_r$-incomparable** — they sit on parallel branches of N1, neither reaching the
other — so §5.2a's collapse does not engage and **both** activities are charged in full. That
independence is a *property of this fixture*, not a general licence: W-07 satisfies §5.3's hypothesis
H2 by accident, which is precisely why it never detected the H2 breach that W-17 was written for.
Assert `SerialChainAbsorbedDayCount = 0` here, so the two fixtures cannot be confused.

- Impacted: $B: 3 \to 8$, $C: 6 \to 11$ ⟹ $EF_B = 13$, $EF_C = 16$, $ES_D = 16$, $EF_D = 20$.
- $$E = 20 - 15 = \mathbf{5}$$
- **Cap:** 5 distinct countable dates; $E = 5 \le 5$. ✓ **Tight** — this is why the cap is a useful
  guard.

| Quantity | B | C | Sum | Verdict |
| :-- | --: | --: | --: | :-- |
| `IndicativeEotDays` $= \max(0, S_j - TF_j)$ | 2 | 5 | **7** | correct per activity, but the **sum is not $E$**. An implementation that reports the sum reports 7 — above the 5-day cap, hence detectably wrong. |
| `MarginalEotDays` $= D^{imp} - D^{imp\setminus j}$ | **0** | **3** | **3** | correct but non-additive — C's extension dominates, so removing B's changes nothing |
| **Network $E$** | — | — | **5** | **governs** |

Working for the marginals: without B's stoppages ($B{=}3, C{=}11$): $EF_C = 16$, $EF_D = 20$ ⟹ 20 ⟹
$\textit{Marginal}_B = 0$. Without C's ($B{=}8, C{=}6$): $EF_B = 13$, $EF_D = 17$ ⟹ 17 ⟹
$\textit{Marginal}_C = 3$. Assert $\max_j \textit{Marginal}_j = 3 \le E = 5$. ✓

### W-08 — stoppages outside the activity's performance window

| # | Setup | Expected |
| :-- | :-- | :-- |
| **W-08a** | **A** has `ActualFinish = 2026-07-03`. Stoppage 2026-07-08 on **A**, `FullStoppage`, 8.00 h | excluded — `ActivityAlreadyComplete`; $E = \mathbf{0}$; $D^{imp} = D^{as} = 15$ |
| **W-08b** | **D** has `PlannedStart = 2026-07-20`, no `ActualStart`. Stoppage 2026-07-08 on **D** | excluded — `ActivityNotYetScheduled`; $E = \mathbf{0}$ |

Both entries are persisted as sources with their exclusion reason. Neither is deleted.

### W-09 — unattributed stoppage

2026-07-08, `Impact = FullStoppage`, `HoursLost = 8.00`, `RainfallMm = 55.00`,
**`AffectedActivityIds = []`**.

- $E = \mathbf{0}$; `UnattributedStoppageDayCount = 1`; `HasUnattributedDays = true`;
  source row reason `NoAffectedActivity`.
- The entry is **valid and stored** — it is legitimate weather evidence. It simply cannot be
  evaluated. The UI must prompt "บันทึกนี้ยังไม่ได้ระบุกิจกรรมที่ได้รับผลกระทบ — ยื่นบันทึกแก้ไขเพื่อระบุ".
- Negative assertion: the evaluator must **not** distribute it across in-progress activities.

### W-10 ★ — correction, supersession, retraction, and a stale evaluation

| Step | Action | Expected |
| :-- | :-- | :-- |
| 1 | Insert **E1**: 2026-07-08, **C**, `FullStoppage`, `HoursLost = 8.00`, `EntryKind = Original` | stored |
| 2 | Run evaluator → **V1** | $E = \mathbf{1}$; `sources = [E1]`; `Stale(V1) = false` |
| 3 | `PUT /weather-logs/{E1}` and `DELETE /weather-logs/{E1}` | **405 `WeatherLogIsImmutable`** both; E1 byte-identical |
| 4 | Insert **E2**: `Correction`, `Corrects = E1`, same date, **C**, `FullStoppage`, `HoursLost = 3.00`, reason "ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง" | stored; **E1 unchanged — no `SupersededBy` column was written** |
| 5 | $\mathcal{L}^{eff}$ | $= \{E2\}$; E1 excluded because E2 points at it |
| 6 | `Stale(V1)` | **`true`**, reason `SourceSuperseded`; **`V1.EotEligibleDays` is still 1** |
| 7 | Re-run → **V2** | $3.00 < 4.00$ ⟹ not countable ⟹ $E = \mathbf{0}$; `sources = [E2]`; V1 untouched |
| 8 | Insert **E3**: `Correction`, `Corrects = **E1**` | **409 `WeatherLogAlreadySuperseded`** — must target E2, the chain tail |
| 9 | Insert **E3′**: `Correction`, `Corrects = E2`, `HoursLost = 7.00` | accepted; $\mathcal{L}^{eff} = \{E3'\}$; re-run → $E = \mathbf{1}$ |
| 10 | Insert **E4**: `Retraction`, `Corrects = E3′`, reason "บันทึกผิดวัน" | $\mathcal{L}^{eff} = \varnothing$; re-run → $E = \mathbf{0}$; **both E3′ and E4 are out** |
| 11 | Insert a correction with `CorrectionReason = null` | **422** |
| 12 | Whole-DB assertion | `AuditLog` contains **zero** `Update`/`Delete` rows for `DailyWeatherLog` |

### W-11 ★ — *critical at what point in time*

**W-11a — no run at or before the stoppage date.** Only run: R1 `CalculatedAt = 2026-07-20`.
Stoppage 2026-07-08 on **C**, full day.

- Governing run = earliest available = R1 ⟹ `CriticalityBasis = Retrospective`,
  **`Confidence = Provisional`**.
- $E = \mathbf{1}$ (same arithmetic as W-01). **Assert the two flags, not only the number** — a test
  that checks 1 alone passes an implementation that has quietly dropped the contemporaneity rule.
- With **no** `CpmRun` at all ⟹ **422 `NoCpmRunAvailable`**.

**W-11b — T1 and T2 disagree.** Two runs:

| Run | CalculatedAt | Network | $D^{as}$ | $TF_C$ | $TF_B$ |
| :-- | :-- | :-- | --: | --: | --: |
| **R1** | 2026-07-01 | N1: $A(5){\to}B(3){\to}D(4)$, $A(5){\to}C(6){\to}D(4)$ | 15 | **0** | 3 |
| **R2** | 2026-07-15 | N1′: $A(5){\to}B(\mathbf{7}){\to}D(4)$, $A(5){\to}C(6){\to}D(4)$ | 16 | **1** | 0 |

*(N1′ working: $EF_B = 12$, $EF_C = 11$, $ES_D = 12$, $EF_D = 16$; $LS_B = 5 \Rightarrow TF_B = 0$;
$LS_C = 6 \Rightarrow TF_C = 1$.)*

Single stoppage: **2026-07-08**, activity **C**, full day. Governing run = **R1** (latest at or
before 8 July).

| Reading | Computation | $E$ |
| :-- | :-- | --: |
| **T2 contemporaneous — RULED CORRECT** | R1: $C{:}6{\to}7$ ⟹ 16 vs 15 | **1** |
| T1 current schedule | R2: $C{:}6{\to}7$ ⟹ $EF_C = 12$, $ES_D = \max(12,12) = 12$, $EF_D = 16$ ⟹ 16 vs 16 | **0** |

**Same weather, same activity, entitlement 1 day or 0 days depending purely on which schedule you
ask.** This is the fixture that pins §4's ruling. Assert $E = 1$ **and**
`EotEvaluationRun.CpmRunId = R1`.

### W-12 — `IssueLog` state machine and tiles

| # | Sequence | Expected |
| :-- | :-- | :-- |
| **W-12a** | Create ISS-024 | `Open`; `StartedAt = null`; `ClosedAt = null` |
| | `advance-status` at 2026-07-09T03:00:00Z | `Doing`; `StartedAt = 2026-07-09T03:00:00Z`; **`ClosedAt` still null** |
| | `advance-status` at 2026-07-14T08:30:00Z | `Closed`; **`ClosedAt = 2026-07-14T08:30:00Z`**; `StartedAt` unchanged |
| | `advance-status` again | **409 `IssueAlreadyClosed`**; `ClosedAt` **unchanged** (assert the exact value, not just the status) |
| **W-12b** | New issue `Open` → attempt direct close | **409 `IssueStatusSkipNotPermitted`** |
| | `Doing` → attempt `Open` | **409** |
| | `Closed` → attempt reopen | **409 `IssueIsClosed`**; recurrence path = new issue with `RelatedIssueId` |
| | Client supplies `closedAt` in the body | ignored; server clock wins (assert the persisted value equals `IDateTimeProvider.UtcNow`, not the body) |
| **W-12c** | Project with **7 Open, 3 Doing, 14 Closed** (24 total); `GET ...?page=1&pageSize=20` | `items.length = 20`, `totalCount = 24`, `statusCounts = {7, 3, 14}` — **tiles are not derived from the 20 rows returned** |
| | Same, `?owner=วิศวกรโครงสร้าง` matching 4/1/2 | `statusCounts = {4, 1, 2}`, `totalCount = 7` — **tiles honour the filter** |
| | Any result | $n_{Open}+n_{Doing}+n_{Closed} = \textit{totalCount}$; no fourth status |

### W-13 ★ — counting basis: absolute vs FIDIC exceedance

`ProjectEotMonthlyAllowance(Month = 7, ExpectedStoppageDays = 3, Source = "…")`.
Activity **C** (critical), five countable days: 2026-07-06, 07-07, 07-08, 07-09, 07-10, all
`FullStoppage`, 8.00 h.

| `CountingBasis` | Days used | $C$ | $D^{imp}$ | $E$ |
| :-- | :-- | --: | --: | --: |
| **`AbsoluteStoppageDays`** (default) | all 5 | 11 | 20 | **5** |
| `ExceedanceOverBaseline` | allowance consumes 6, 7, 8 July chronologically; **9 and 10 July survive** | 8 | 17 | **2** |

*(Absolute working: $C = 11$ ⟹ $EF_C = 16$, $ES_D = 16$, $EF_D = 20$.)*

**5 days vs 2 days on identical facts.** This is open question **Q1**, and it is the difference
between "eighteen rainy July days" and "the rain that was genuinely worse than anyone could have
priced". Under a Thai government contract relying on เหตุสุดวิสัย, the exceedance reading is the
more defensible one — but it is the human's call, not the code's.
Missing allowance row while this basis is active ⟹ **422 `EotMonthlyAllowanceMissing`**, never
$\bar N_m = 0$.

### W-14 — the evaluator writes nothing (§2.1)

Run W-03's data. Capture the **full column set** of every row of `Activity`, `ActivityRelation`,
`Project`, `Calendar`, `CalendarException`, `ProjectFinanceLedger`, `EvmPeriodSnapshot`,
`VariationOrder`, `PaymentCertificate` before and after. Assert:

- byte-equality on all of them, **including `RowVersion`** — not just the fields you expect to move;
- specifically: B's `IsCritical` is still `false` and `TotalFloat` still `3`, though the impacted
  network makes it critical with float 0;
- rows created: exactly one `EotEvaluation`, its `EotEvaluationRun`/`Source`/`Driver` children, and
  the `AuditLog` entry. Nothing else, in any table.

### W-15 — two governing runs: windows are summed, and the baseline reading over-grants

Runs R1 (2026-07-01, network N1, $D^{as} = 15$) and R2 (2026-07-15, network N1′, $D^{as} = 16$),
as in W-11b. Stoppages on **C**, all full days: **2026-07-08, 07-09** (governed by R1) and
**2026-07-20, 07-21** (governed by R2).

| Window | Network | Impacted | $D^{imp}$ | $\Delta$ |
| :-- | :-- | :-- | --: | --: |
| R1 (2 days) | N1, $D^{as}=15$ | $C: 6 \to 8$ ⟹ $EF_C = 13$, $EF_D = 17$ | 17 | **+2** |
| R2 (2 days) | N1′, $D^{as}=16$ | $C: 6 \to 8$ ⟹ $EF_C = 13$, $EF_B = 12$, $ES_D = 13$, $EF_D = 17$ | 17 | **+1** |

$$E = 2 + 1 = \mathbf{3}, \qquad \texttt{CriticalityBasis} = \texttt{Mixed}$$

Comparison readings on the same data:

| Method | $E$ | Note |
| :-- | --: | :-- |
| **T2 contemporaneous windows (ruled)** | **3** | two `EotEvaluationRun` child rows, +2 and +1 |
| T3 Impacted As-Planned (all 4 days against R1) | **4** | $C: 6 \to 10$ ⟹ $EF_C = 15$, $EF_D = 19$ ⟹ 19 − 15. **Over-grants by 1** — it credits float C no longer had by 20 July. |
| T1 latest schedule (all 4 days against R2) | 3 | happens to coincide here; **do not read this as validating T1** — W-11b shows it diverging |

Assert two `EotEvaluationRun` rows with the right `CpmRunId`s and deltas, not merely the total.

### W-16 — degenerate and zero cases

| # | Input | Expected |
| :-- | :-- | :-- |
| a | Window with **no weather entries at all** | $E = \mathbf{0}$; `Confidence = Substantiated`; a valid `EotEvaluation` is still written (an evidenced nil return is a result) |
| b | Entries exist but all have `Impact = NoImpact` | $E = \mathbf{0}$; every source row reason `NoStoppageRecorded` |
| c | Project has **no `IsDefault` Calendar** | **422 `ProjectCalendarNotConfigured`** — no evaluation written |
| d | Project has **zero activities** (`CpmEngine` returns duration 0) | $E = \mathbf{0}$; no division, no crash — mirrors `CpmEngine`'s own empty-graph guard |
| e | Entry dated **outside** $[w_0, w_1]$ | excluded, reason `OutsideWindow`; not counted in `CountableStoppageDayCount` |
| f | Stoppage naming an activity in **another project** (same tenant) | **422 `ActivityNotInProject`** at write time — never at evaluation time |
| g | Stoppage naming an activity in **another tenant** | **404** (ADR-0002 — a cross-tenant id must not even confirm existence) |

---

> **W-17 to W-20 were added 2026-08-10**, ruling on `qa-engineer`'s S11-QA-01 escalation of the §5.3
> cap breach. They exist to pin §5.3's two hypotheses (H1, H2) and **both** directions in which an
> implementation can get §5.2a wrong — collapsing too little (W-17, W-18) and collapsing too much
> (W-19, W-20). The escalated case is W-17; the skipped test
> `QaIndependentVerificationTests.Cap_Chain_Predecessor_And_Successor_Charged_The_Same_Two_Dates_…`
> is to be un-skipped and re-pointed at W-17's expected values once S11-BE-02 implements §5.2a.

### W-17 ★ — predecessor and successor charged on the same days: the serial-chain collapse

**The escalated fixture. The one that proves §5.3's H2. Build it immediately after W-07.**

Network N1, default policy, run R1. Two entries, each naming **both A and C**:

| Input | |
| :-- | :-- |
| 2026-07-08 (Wed) | activities **A** *and* **C**, `Impact = FullStoppage`, `HoursLost = 8.00` |
| 2026-07-09 (Thu) | activities **A** *and* **C**, `Impact = FullStoppage`, `HoursLost = 8.00` |

Both dates are working days under `TH-6Day`; every §3 gate passes for **both** activities on **both**
dates — §3.7 raises no exclusion, by ruling (§3.7's note). $C_d = \{A, C\}$ for each date.

**Collapse (§5.2a).** N1 contains `A → C` (FS), so $A^{F} \to C^{S}$ and $A \prec_r C$ — comparable.
$TF_A^{(R1)} = TF_C^{(R1)} = 0$, so the float key ties and the topological tie-break keeps the
upstream activity: $K_d = \{A\}$. $C$'s charge is **absorbed** on both dates.

$$\Delta D_A = 2, \qquad \Delta D_C = 0 \ (\text{2 absorbed})$$

- Impacted: $A: 5 \to 7$ ⟹ $EF_A = 7$; $EF_B = 10$; $EF_C = 7 + 6 = 13$; $ES_D = \max(10,13) = 13$;
  $EF_D = 17$ ⟹ $D^{imp} = 17$.
- $$E = 17 - 15 = \mathbf{2}$$
- **Cap: 2 distinct countable dates; $E = 2 \le 2$. ✓ Tight.**
- Impacted floats — assert them, the network shape must not move: $TF_A = 0$, $TF_B = 3$,
  $TF_C = 0$, $TF_D = 0$; impacted critical path still **A → C → D**. No criticality swap here
  (contrast W-03, where there is one).

| Driver | $S_j$ charged | `SerialChainAbsorbedDays` | `AbsorbedInto` | $TF_j^{(R1)}$ | `WasCriticalAtRun` | `IsOnImpactedCriticalPath` | `Indicative` | `Marginal` | `RemainingFloatAfter` |
| :-- | --: | --: | :-- | --: | :-- | :-- | --: | --: | --: |
| **A** | **2** | 0 | — | 0 | true | true | 2 | **2** | 0 |
| **C** | **0** | **2** | `"A"` | 0 | true | true | 0 | **0** | 0 |

Counts: `DistinctCountableDateCount = 2`, `CountableStoppageDayCount = 2`,
**`SerialChainAbsorbedDayCount = 2`**, `UnattributedStoppageDayCount = 0`.
Relations: $\max_j \textit{Marginal}_j = 2 \le E = 2$ ✓; $\textit{Indicative}_j \ge \textit{Marginal}_j$ ✓.

**Negative assertions — this is where implementations fail:**

| Wrong answer | How it is produced | Why it is wrong |
| --: | :-- | :-- |
| **4** | charge both activities independently ($\Delta D_A = \Delta D_C = 2$ ⟹ $EF_A = 7$, $EF_C = 15$, $EF_D = 19$) | **the reproduced defect.** $4 \le 2$ is false — four days of delay from two days of storm |
| **0** | reject or exclude the entry because it names a predecessor and its successor | destroys evidence a storm genuinely produced (§3.7 ruling) |
| **2**, but with C excluded | collapse implemented as an `ExclusionReason` instead of an absorption | the number is right and the explanation is a lie — C **was** stopped; assert C still has a driver row |

### W-18 — one calendar day cannot buy three: the `FullDayHours` clamp (H1)

Network N1, activity **C** (critical), `FullDayHours = 8.00`. Tests §3.4's
$\min(h_{j,d}, H)$ clamp, which is the *other* hypothesis §5.3 rests on. This shape is reachable in
production: `HoursLost` is validated to $[0, 24]$, and a two- or three-shift site legitimately records
a whole lost day as 24.00 h.

| # | Policy | Input | Charged hours | $\Delta D_C$ | $C$ | $D^{imp}$ | $E$ | Dates | Cap |
| :-- | :-- | :-- | --: | --: | --: | --: | --: | --: | :-- |
| **a** | `FractionalAccrual` | 07-08, `HoursLost = 24.00` | $\min(24,8) = 8.00$ | **1** | 7 | 16 | **1** | 1 | $1 \le 1$ ✓ tight |
| **b** | `ThresholdWholeDay` (default) | same entry | n/a — day weight is 1.00 | **1** | 7 | 16 | **1** | 1 | $1 \le 1$ ✓ |
| **c** | `FractionalAccrual` | 07-08 `24.00` + 07-09 `6.00` | $8.00 + 6.00 = 14.00$ | $\lfloor 14/8 \rfloor = $ **1** | 7 | 16 | **1** | 2 | $1 \le 2$ ✓ slack |

- **W-18a** without the clamp gives $\lfloor 24/8 \rfloor = 3$ ⟹ $C: 6 \to 9$, $EF_C = 14$,
  $EF_D = 18$, $E = \mathbf{3}$ against **one** countable date — $3 \le 1$ is false. That is the
  assertion the fixture exists to make fail.
- **W-18b** proves the clamp is a `FractionalAccrual` concern only and perturbs nothing under the
  default policy — $h_{min}$/$H$ comparisons are unaffected, exactly as §3.4's "read only under"
  discipline requires. Assert `HoursLostClampedToFullDay = false` here and `true` in (a) and (c).
- **W-18c**: $\textit{UnclaimedFractionalHours} = 14.00 - (1 \times 8.00) = \mathbf{6.00}$, reported
  and **not** carried forward. Note the clamp is applied **per date, before summation** — clamping the
  summed hours instead would silently discard the second day.
- Assert the recorded `HoursLost` on `DailyWeatherLog` is still **24.00** in every case. The clamp is
  an evaluation-time modelling step; it must never write back to the immutable log.

### W-19 — an SS-overlapped pair must **not** be collapsed

The negative of W-17: two activities joined by a relation, both charged, and **both charges stand**.
A "collapse anything with a transitive predecessor" implementation under-states here, and no other
fixture catches it.

**Network N2** (this fixture only): $P(5) \xrightarrow{SS,\,0} Q(6)$; $P(5) \xrightarrow{FS,\,0} R(4)$;
$Q(6) \xrightarrow{FS,\,0} R(4)$.

| | P | Q | R |
| :-- | --: | --: | --: |
| ES / EF | 0 / 5 | 0 / 6 | 6 / 10 |
| LS / LF | 0 / 5 | 0 / 6 | 6 / 10 |
| **TF** | **0** | **0** | **0** |

$D^{as} = 10$; all three critical. *(Backward working, per `RelationConstraints`: $LF_R = 10$,
$LS_R = 6$; FS ⟹ $LF_Q = 6$, $LS_Q = 0$; for P, SS gives $LF_P \le LS_Q - 0 + D_P = 5$ and FS gives
$LF_P \le LS_R = 6$, so $LF_P = 5$, $LS_P = 0$.)*

Two entries — 2026-07-08 and 07-09 — each naming **both P and Q**, `FullStoppage`, 8.00 h.

**Collapse test.** $P \xrightarrow{SS} Q$ contributes $P^{S} \to Q^{S}$, **not** $P^{F} \to Q^{S}$.
From $P^{F}$ the only route is $R^{S} \to R^{F}$, and R has no successors, so $Q^{S}$ is unreachable
from $P^{F}$: $P \not\prec_r Q$ and $Q \not\prec_r P$ — **incomparable, no collapse.** This is
correct in substance as well as form: an SS link says the two activities genuinely run side by side,
so a storm really did cost each of them a day, and their durations never add along a common path.

- $\Delta D_P = \Delta D_Q = 2$. Impacted: $EF_P = 7$; $ES_Q = 0$, $EF_Q = 8$;
  $ES_R = \max(7,8) = 8$, $EF_R = 12$ ⟹ $D^{imp} = 12$.
- $$E = 12 - 10 = \mathbf{2}$$
- **Cap: 2 distinct dates; $2 \le 2$ ✓ tight.** `SerialChainAbsorbedDayCount` = **0**.
- Drivers: $\textit{Indicative}_P = \textit{Indicative}_Q = 2$ (sum **4** $> E$ — another
  non-additivity data point); $\textit{Marginal}_P = 12 - 12 = \mathbf{0}$,
  $\textit{Marginal}_Q = 12 - 11 = \mathbf{1}$; $\max_j \textit{Marginal}_j = 1 \le E = 2$ ✓.
- **Negative assertion:** an implementation testing plain transitive precedence collapses to $P$
  alone ⟹ $EF_P = 7$, $EF_Q = 6$, $ES_R = 7$, $EF_R = 11$ ⟹ $E = \mathbf{1}$. **Under-states by a
  day**, and satisfies the cap while doing so — which is exactly why the cap is necessary but not
  sufficient (§5.3).

### W-20 ★ — the collapse keeps the **more binding** activity, not merely the upstream one

Pins §5.2a's least-float-first rule. Charging the upstream activity here yields **0** and charging the
least-float one yields **2**; the whole answer turns on the tie-break, so a "keep the predecessor"
implementation zeroes a real entitlement and no other fixture notices.

**Network N3** (this fixture only): $A(5) \xrightarrow{FS,\,0} C(6)$; $X(9) \xrightarrow{FS,\,0} C(6)$;
$C(6) \xrightarrow{FS,\,0} D(4)$.

| | A | X | C | D |
| :-- | --: | --: | --: | --: |
| ES / EF | 0 / 5 | 0 / 9 | 9 / 15 | 15 / 19 |
| LS / LF | 4 / 9 | 0 / 9 | 9 / 15 | 15 / 19 |
| **TF** | **4** | **0** | **0** | **0** |

$D^{as} = 19$; critical path **X → C → D**. A is a predecessor of C but carries 4 days of float.

Two entries — 2026-07-08 and 07-09 — each naming **both A and C**, `FullStoppage`, 8.00 h.

**Collapse.** $A^{F} \to C^{S}$ ⟹ $A \prec_r C$, comparable. Float key: $TF_A = 4$, $TF_C = 0$ ⟹
**keep C**, absorb A's two days into C. (Note the kept activity is the *successor* here.)

$$\Delta D_C = 2, \qquad \Delta D_A = 0 \ (\text{2 absorbed})$$

- Impacted: $C: 6 \to 8$ ⟹ $EF_A = 5$, $EF_X = 9$, $ES_C = \max(5,9) = 9$, $EF_C = 17$,
  $ES_D = 17$, $EF_D = 21$ ⟹ $D^{imp} = 21$.
- $$E = 21 - 19 = \mathbf{2}$$
- **Cap: 2 distinct dates; $2 \le 2$ ✓ tight.**
- Impacted floats: $TF_C = 0$, $TF_D = 0$, $TF_X = 0$, $TF_A = 4$ (unchanged — A was absorbed, so it
  keeps its float).

| Driver | $S_j$ charged | `SerialChainAbsorbedDays` | `AbsorbedInto` | $TF_j^{(r)}$ | `WasCriticalAtRun` | `IsOnImpactedCriticalPath` | `Indicative` | `Marginal` | `RemainingFloatAfter` |
| :-- | --: | --: | :-- | --: | :-- | :-- | --: | --: | --: |
| **C** | **2** | 0 | — | 0 | true | true | 2 | **2** | 0 |
| **A** | **0** | **2** | `"C"` | 4 | false | false | 0 | **0** | **4** |

- **Negative assertion — the point of the fixture:** keeping the upstream activity instead gives
  $\Delta D_A = 2$ ⟹ $EF_A = 7$, $ES_C = \max(7, 9) = 9$ (X still governs C's start), $EF_C = 15$,
  $EF_D = 19$ ⟹ $E = \mathbf{0}$. A two-day storm that stopped the critical activity would be
  reported as no impact at all. **$0$ satisfies the cap** — only this fixture's expected value
  catches it.
- Counts: `CountableStoppageDayCount = 2`, `SerialChainAbsorbedDayCount = 2`,
  `DistinctCountableDateCount = 2`.

---

## 11. Reconciliation with P6, MS Project, and the delay-analysis standards

**Name the method in writing.** CM+ performs a **contemporaneous, windows-based Time Impact
Analysis with additive duration fragnets** — in AACE RP 29R-03 terms, an observational/dynamic
windows framing (MIP 3.3–3.4) with modelled additive impacts per window (MIP 3.7). Any claim
submitted from CM+ output should say so; a claim that does not state its method invites the first
challenge to be about the method.

**Neither P6 nor MSP has a weather log or an EOT object.** The reconciliation is with *how a P6
scheduler does the same job by hand*:

| CM+ | P6 equivalent | Agreement |
| :-- | :-- | :-- |
| Stoppage as `+1 working day` on the named activity | Delay fragnet inserted into a schedule update, or the activity's Remaining Duration extended | **Exact**, when the same activities are impacted |
| Stoppage as a non-working `CalendarException` | The usual P6 "rain day" technique: mark the date non-working on the calendar | **Differs — deliberately.** A P6 calendar exception stops **every** activity on that calendar; CM+ stops only the activities the site engineer named. CM+ therefore returns **≤** P6's figure whenever the P6 user takes the site-wide reading. Neither is wrong; they answer different questions. Reconcile by comparing CM+'s named-activity set against the P6 calendar's membership before concluding either is defective. |
| $E = D^{imp} - D^{as}$ | Difference in Project Finish between the impacted and un-impacted copies of the update | **Exact** |
| `IsCritical` $\iff TF = 0$ | P6: **Longest Path** (common default) **or** `Total Float ≤ n` | Coincide today — CM+ has one calendar per project, no date constraints, no multiple-float-path calculation. **They diverge in P6 the moment multiple activity calendars are used**, where an activity can carry $TF = 0$ and still not be on the Longest Path. Relevant to open questions Q6 and Q7, not to today's numbers. |
| $TF \ge 0$ always | P6 shows **negative float** against a Must-Finish-By project constraint | **CM+ cannot represent this yet** — $LF_{project} = \max EF$. See §4.5: this is a real gap, and the fix (`IsCritical` $\iff TF \le 0$) must land with any contract-completion-date feature. |
| Working-day durations, `WorkingCalendar` | P6 activity calendars | Same semantics for a single calendar |

**MS Project:** `Total Slack` = Total Float; MSP's `Critical` flag uses a configurable threshold
(*Tools ▸ Options ▸ Schedule ▸ "Tasks are critical if slack is less than or equal to N days"*,
default 0). Same caveat as P6's `TF ≤ n`. MSP has no weather or EOT concept; the manual equivalent is
again a duration extension in a saved interim plan.

**Deliberate CM+ divergence — schedule history.** Neither P6 nor MSP keeps a queryable history of
per-activity float across updates (P6 keeps *baselines*, which are snapshots taken deliberately, not
a log of every calculation). CM+'s `CpmRun` history (§4.3) is therefore **richer** than what an
imported `.XER` can supply: an import provides exactly one run, as of the import date. Rule for the
importer: **never back-fill `CpmRun` history from a single file**, and mark evaluations that rest on
an imported run as `Retrospective` unless the file's own data date precedes the stoppage.

---

## 12. Open questions for the human — [ต้องยืนยัน]

The R-table pattern (`docs/10.` §11) and the ADR-0015 / ADR-0016 precedent apply: **nothing below is
guessed, and no placeholder is seeded.** Where a default is stated it is a real, defensible default;
where it says "none", the field is nullable and `NULL` means *not configured*, never a value.

| # | Question | Blocking | Default until answered |
| :-- | :-- | :-- | :-- |
| **Q1** | **Counting basis — `AbsoluteStoppageDays` or `ExceedanceOverBaseline`?** Fixture W-13: **5 days vs 2 days** on identical data. Ordinary Thai monsoon rain is foreseeable and generally not เหตุสุดวิสัย (ป.พ.พ. ม.8); FIDIC 8.4(c)/8.5(c) requires the weather to be *exceptional* benchmarked against published climatic data. So the absolute basis may overstate contractual entitlement even while correctly stating schedule impact. If exceedance is chosen, **the monthly allowances must be sourced** (TMD station data for the site, or the figure written into the contract). | S11-BE-02 | **`AbsoluteStoppageDays`** — it is what the DoD describes and what the recorded data supports; §2.2's disclosure is what keeps it honest. |
| **Q2** | **Partial-day policy and the 4.00 h threshold.** Fixture W-05: **2 / 1 / 0 days** on identical data. Does the pilot contract define a lost-time threshold? **And what is one working day at this site?** $H$ = `FullDayHours` is not cosmetic: §3.4 clamps each date's `HoursLost` at $H$, so a two- or three-shift site left at the 8.00 default **under-counts** a genuine 24-hour loss to one day, while a single-shift site that sets $H = 24.00$ makes every ordinary stoppage a fraction (W-18). Set $H$ to the site's actual shift length. | S11-BE-02 | **`ThresholdWholeDay`, $h_{min} = 4.00$, $H = 8.00$** (half a shift, single shift). Configurable per project. |
| **Q3** | **Rainfall depth threshold** — is one imposed by the pilot contract, at what mm, is it a 24-hour or multi-day cumulative figure, and does it combine with the hours test as **AND** (assumed) or **OR**? | no | **`MinRainfallMmForCountableDay = NULL`** (no depth test), combination **AND**. `NULL` ≠ 0.00 (ADR-0015 precedent). |
| **Q4** | **Concurrent delay** — ruled out of scope for Sprint 11 (§7.2). When it lands, which doctrine: **Malmaison** (SCL 2nd ed. CP 10 — EOT granted, prolongation cost refused), **apportionment** (*City Inn* [2010] CSIH 68), or **dominant cause**? And is a `DelayEvent` register with `CauseParty` in scope for a later sprint? | no (for S11) | **Not assessed.** `ConcurrencyAssessed = false` on every evaluation, disclosed on screen. |
| **Q5** | **`CpmRun` history — approve the scope addition?** §4.3. Without it the contemporaneous ruling (§4.1) is unimplementable and every evaluation is `Retrospective`/`Provisional`. Cost: 3 tables, ~875k rows/project, a retention policy, and a change to `RecalculateCpmCommandHandler` — **beyond S11-DB-01's stated scope**. | **S11-BE-02, S11-DB-01** | **Escalate before sprint commit.** Degraded fallback fully specified in §4.4 if declined. |
| **Q6** | **Per-activity calendars.** `Activity` has no `CalendarId`; one project calendar governs every activity (§3.3). A site where structural works run Mon–Sat and MEP runs Mon–Fri cannot be modelled, and the §3.3 gate will be wrong for one of them. | no | **Project `IsDefault` calendar for all activities.** |
| **Q7** | **Negative float / contract completion date.** Once a Must-Finish-By constraint exists, `IsCritical` must become $TF \le 0$ or a late project shows zero critical activities and this evaluator returns zero EOT (§4.5). Is a `Project.ContractCompletionDate` planned? | no (today) | **$TF = 0$**, matching the shipped engine. Must change **together with** any completion-date feature. |
| **Q8** | **Do weather-log corrections need a countersignature?** The entry is legal evidence; a mandatory reason + attributed author may or may not be enough for the client's audit regime. | no | **No countersignature**; `CorrectionReason` mandatory, author and timestamp recorded. |
| **Q9** | **Future EOT Claim document routing.** When a formal claim document is built, it must route through the approval engine — which today routes only on **money** (§8.6). Add a `RoutingBasis` (days claimed) or a fixed chain per document type? Flagged now so the `ApprovalPolicy` schema decision is deliberate. | no | Out of scope for S11; the engine is **not** used by weather logs or evaluations. |
| **Q10** | **May an issue skip `Doing`?** (§9.1) Low stakes — one extra click either way. | no | **No skipping**; `advance-status` advances one rung. |
| **Q11** | **May a closed issue be reopened?** (§9.1) Recurrence is currently a new issue with `RelatedIssueId`. | no | **No reopening**; `Closed` is terminal, consistent with every other terminal state in the product. |
| **Q12** | **Should the serial-chain collapse ever be replaced by the exact per-path figure?** §5.2a's antichain reduction never over-states and is exact whenever a date's charged activities form a chain — which is every fixture here and, realistically, every real entry. It can under-state by at most one day per date, and only when a path reaches an absorbed activity without passing through the kept one. The exact quantity $\max_\pi\big(\text{len}(\pi) + \lvert \{d : C_d \cap \pi \ne \varnothing\} \rvert\big)$ is not additive along a path and needs path enumeration, so it is only affordable on small networks. Worth revisiting only if the absorbed-day disclosures show it biting in real data. | no | **Collapse accepted**, with `SerialChainAbsorbedDayCount` and the per-driver absorbed days disclosed on screen and in exports. |

**Not open — recorded rulings of this document:** contemporaneous criticality (§4.1); TIA over
`CpmEngine` rather than a bespoke float ledger (§5.1); one EOT day maximum per calendar day, as an
unconditional physical bound (§5.3); the serial-chain collapse, least-float-first, as the place that
bound is enforced — **not** §3.7's gate, which stays permissive so a real storm stays recordable
(§5.2a, §3.7); `HoursLost` clamped at `FullDayHours` per date, at evaluation and never at write
(§3.4); float is not owned by either party, per SCL CP 8 (§6.3); supersession by forward pointer with
no back-stamp (§8.2); corrections invalidate but never mutate a stored evaluation (§8.5); the
evaluator is advisory and writes nothing outside its own tables (§2.1).

---

## 13. Traceability — rule → task → fixture

| Rule | Sprint 11 task | Fixture(s) | Test artifact |
| :-- | :-- | :-- | :-- |
| Immutability, correction chain, retraction, no `PUT`/`DELETE` (§8) | **S11-BE-01**, S11-DB-01 | **W-10** | `CMPlus.Domain.Tests/DailyWeatherLogTests`, `CMPlus.Integration.Tests/Weather/` |
| Countability gates: calendar, holidays, partial day, rainfall, activity window (§3) | **S11-BE-02** | **W-04, W-04b, W-05, W-06, W-08a/b, W-09, W-16** | `CMPlus.Application.Tests/Eot/CountabilityTests` |
| **`HoursLost` clamped at `FullDayHours` per date — §5.3's hypothesis H1** (§3.4) | **S11-BE-02** | **W-18a/b/c** | `EotCountabilityGateTests` + `EotEvaluatorTests` |
| **§3.7 stays permissive: a predecessor+successor entry is valid input, never an exclusion** (§3.7) | **S11-BE-02** | **W-17** (negative assertions) | `EotCountabilityGateTests` |
| Critical → EOT; non-critical → float only (the DoD's core rule) (§5, §6) | **S11-BE-02** | **W-01, W-02** | `CMPlus.Application.Tests/Eot/EotEvaluatorTests` |
| **Float exhaustion ⟹ criticality shift ⟹ EOT** (§6.1) | **S11-BE-02** | **W-03** ★ | ditto — **build this first** |
| Contemporaneous criticality; degraded modes (§4) | **S11-BE-02** (+ Q5) | **W-11a, W-11b** ★, **W-15** | ditto + `CMPlus.Integration.Tests/Eot/` |
| No double-count across **parallel** activities; per-activity vs network figures (§5.3, §5.4) | **S11-BE-02** | **W-07** ★ | `EotEvaluatorTests` — assert the cap on **every** fixture |
| **Serial-chain collapse — §5.3's hypothesis H2** (§5.2a): comparability is edge-type aware, least-float-first, absorbed days disclosed | **S11-BE-02** | **W-17** ★, **W-19**, **W-20** ★ | `EotEvaluatorTests` + a dedicated `SerialChainCollapseTests`; un-skip `QaIndependentVerificationTests.Cap_Chain_Predecessor_And_Successor_…` against W-17's values |
| Counting basis (absolute vs exceedance) (§3.5) | **S11-BE-02** (blocked by Q1) | **W-13** ★ | parameterised theory over `CountingBasis` |
| Evaluator has no side effects (§2.1) | **S11-BE-02** | **W-14** | `CMPlus.Integration.Tests/Eot/NoSideEffectsTests` |
| `IssueLog` states, `ClosedAt`, tile counts (§9) | **S11-BE-03** | **W-12a/b/c** | `CMPlus.Domain.Tests/IssueLogTests` + `CMPlus.Integration.Tests/Issues/` |
| Immutability warning before submit; EOT tile relabelled; `Provisional` badge (§2.2, §8.3) | **S11-FE-01** | W-10 step 3, W-11a | `web/src/features/weather/*.test.tsx` |
| Tenant isolation on every weather/issue/evaluation query (ADR-0002) | S11-BE-01..03, S11-DB-01 | **W-16f, W-16g** | the standard parameterised tenant-filter suite |
| Index `(TenantId, ProjectId, LogDate)`; date-range query is a seek | **S11-DB-01** | — | execution-plan assertion, per S11-DB-01's DoD |

**Rule from `docs/10.` §10 binding every consumer of this file:** an agent claiming a test passes
must show the real run output, and **a fixture that has passed may never be edited to make code
pass** — if a computed value disagrees with a fixture here, escalate to `domain-expert` for a ruling
first.
