# Man/Equipment Log & Productivity Index (PI) — Domain Rules (Sprint 12)

**Stage-2 artifact.** Author: `domain-expert` · Date: 2026-08-10 · Feature: `manpower-equipment`
**This document is decision D8.** `docs/10.` §8 Sprint 12 (S12-BE-02) and §11 R-15 both make it a
hard gate: *"สูตร PI ต้องได้คำตอบจาก domain-expert (D8) ก่อนเริ่ม task นี้ — **ห้ามคิดสูตรเอง**"*.
Everything below is normative. No prior definition of PI exists anywhere in this repository — verified
against `docs/1.`–`docs/10.`, `.claude/knowledge/**`, `docs/specs/**` and `backend/src/**`.

**Consumers:** `system-architect` (design.md), `backend-developer` (S12-BE-02),
`database-engineer`, `frontend-developer` (S12-FE-02), `qa-engineer`.
`knowledge-curator` should promote §5–§8 into `.claude/knowledge/domain/productivity-index.md`
after this sprint.

**Upstream sources this document is bound by** (read them; do not paraphrase from here):

| Source | What it fixes |
| :-- | :-- |
| `docs/10.` §8 Sprint 12 | S12-BE-02 (*"บันทึกรายวันตามหมวดงาน"*) and S12-FE-02 (*"histogram + PI ตามสูตรที่ได้รับอนุมัติ; แสดง '—' เมื่อไม่มีค่า planned"*) |
| `docs/9.` §4 | the **provisional** `ManpowerEquipmentLog` field list (`LogDate`, `WorkCategory`, `ManCount`, `EquipmentCount`, `PlannedManCount`) — §4.6 below rejects two things in it, with reasons |
| `docs/ECM Planning Prototype.dc.html` lines 534–579, 719–727, 866–879 | the authoritative UI (ADR-0006): the four KPI tiles, the daily table, the 7-day histogram, the ±5% legend, and the tile subtitle **"EV ต่อ ชม.แรงงาน / แผน"** — which §5.2 shows is algebraically the formula ruled here |
| `docs/วิเคราะห์ฯ` §5 "Manpower & Equipment Tracking" | daily resource logs, plant hours, and the (conditional) feed into $AC$ |
| `.claude/knowledge/domain/evm-formulas.md` | $EV$, $CPI$, the null-on-undefined convention, rounding (`MidpointRounding.AwayFromZero`, risk R-12) |
| `.claude/knowledge/domain/actual-cost.md` §3, §4.3, §7.5 | control-account granularity, `Source = DerivedFromResourceLog`, the half-open $(a,b]$ period convention |
| `docs/specs/weather-eot/domain-rules.md` §8 | the append-only + correction-chain pattern reused verbatim in §4.7 |
| ADR-0002 · ADR-0009 · ADR-0013 · ADR-0015 | tenant filter · progress step function · null-vs-zero in `ActualCostResult` · nullable config with no seeded default |

**Precision.** Man-hours and equipment-hours `decimal(9,2)`; head/unit counts `int`; ratios and
percentages `decimal(5,2)`; dates `DateTimeOffset` (calendar-day identity). Keep full `decimal`
precision through every sum and the division; round **once**, at the response boundary, with
`MidpointRounding.AwayFromZero` (fixture **M-16**). **No money value appears anywhere in this
feature** — see §3.

---

## 1. The one-paragraph answer (for anyone who reads nothing else)

$$\boxed{\;PI \;=\; \frac{\text{Earned man-hours}}{\text{Actual man-hours}} \;=\; \frac{\sum_i BMH_i \cdot \Delta P_i}{\sum \textit{ManHours}}\;}$$

**Higher is better. $PI = 1.00$ is exactly on budget. $PI < 1.00$ means the work consumed more
hours than it was worth.** It is dimensionless (hours ÷ hours), rounded to 2 dp, and it is `null`
— rendered **"—"** — whenever earned hours are unknown, never `0` and never silently substituted.
It aggregates as a **ratio of sums**, never as an average of ratios. It is a read-only indicator:
it never feeds $EV$, $AC$, $CPI$, $SPI$, $EAC$ or any money figure.

**`ManCount / PlannedManCount` — the ratio implied by `docs/9.` §4 and by US-12.2's acceptance
criterion — is NOT a productivity index.** It is a manning ratio. §5.1 and fixture **M-02** show
the two answering *opposite* on the same day. Labelling it "PI" is the single most likely defect
in this feature.

---

## 2. Definitions

| Term | Symbol | Definition | Unit |
| :-- | :-- | :-- | :-- |
| Man-hour | MH (ชม.-คน) | One worker present and available for work for one hour. Includes overtime; includes paid waiting/standby time on site. Excludes travel and off-site time. | h |
| Actual man-hours | $AMH_{s}(a,b]$ | Sum of `ManHours` over in-force log rows for scope $s$ with `LogDate` $\in (a,b]$. | h |
| Budgeted man-hours | $BMH_i$ | `Activity.BudgetManHours` — the man-hours the estimate allowed for activity $i$'s **whole** scope. `NULL` = not estimated in hours (**never** 0). | h |
| Physical percent complete | $P_i(t)$ | The activity's progress at $t$, read with the ADR-0009 step function — the *same* read `EvmDataReader` uses for $EV$. | % |
| Earned man-hours | $EMH_i(t)$ | $BMH_i \cdot P_i(t)/100$ — the man-hours the estimate says the completed work was *worth*. The man-hour analogue of $EV$. | h |
| Productivity Index | $PI$ | $EMH / AMH$. §5. | — (ratio) |
| Lost man-hours | $L$ | $AMH - EMH$. Positive = hours consumed beyond budget. The money-free statement of the labour efficiency variance. | h |
| Manning ratio | $MR$ | $\textit{WorkerCount} / \textit{PlannedWorkerCount}$ (or the hours equivalent). **A staffing-compliance measure, not productivity.** | — |
| Work category | $c$ | หมวดงาน — a trade/discipline taxonomy (งานโครงสร้าง, งานสถาปัตยกรรม, งานระบบ …). Orthogonal to the WBS. §4.3. | — |
| Control account | $n$ | The `WBSNode` at which budget, progress and resources all meet (actual-cost.md §3). | — |
| Scope | $s$ | A (control account, work category) pair, or any roll-up of them. The unit PI is computed for. | — |
| In-scope hours | — | Hours attributable to at least one activity with a non-null $BMH$. Only these enter PI's denominator. §5.6. | h |
| Coverage | $\kappa$ | $AMH^{in\text{-}scope} / AMH^{total}$ — how much of the reported effort PI actually speaks for. | % |
| Effective log set | $\mathcal{M}^{eff}$ | The log rows in force after applying the correction chain (§4.7). The only set any query reads. | — |
| Equipment operating hours | $EOH$ | Hours a unit was actually working. | h |
| Equipment standby hours | $ESH$ | Hours a unit was on site, charged, and not working (idle, waiting, under repair). | h |
| Equipment utilisation | $U$ | $EOH / (EOH + ESH)$. **Not** productivity. §8. | % |

---

## 3. The boundary — PI is an indicator, and it moves no money

> **Hard invariant (assertable — fixture M-11c).** Computing or displaying PI writes nothing except
> its own read model. **`ManpowerEquipmentLog` is not a source of $AC$ in Sprint 12.**

Specifically, the Man/Equipment module must **not**:

- write an `ActualCostEntry`, a `ProjectFinanceLedger` row, or an `EvmPeriodSnapshot`;
- change `Activity.ProgressPercentage`, `BudgetCost`, or any `WBSNode` weight;
- appear anywhere in the computation of $EV$, $AC$, $CV$, $CPI$, $SV$, $SPI$, $EAC$, $ETC$, $VAC$
  or $TCPI$.

**Why.** `docs/วิเคราะห์ฯ` §5 says daily resource logs accumulate into $AC$, and it is right *in
principle* — but converting hours to money needs a labour-rate table that does not exist, and
`actual-cost.md` §4.3 already ruled on what happens if it is built carelessly:

> *"entries must carry `Source = DerivedFromResourceLog`, be visually distinguished as estimated, and
> be **superseded (reversed) when the accounting figure lands** — never summed alongside it.
> Double-counting labour is the most likely defect in this whole module."*

Sprint 12 has neither the rate table nor the reversal machinery, and the payroll rates it would need
are PDPA-sensitive (§4.9). So the log records **hours, not money**, and PI is a pure efficiency
ratio. This also makes PI *safe*: a wrong PI misinforms a PM; a wrong AC misstates $CPI$, $EAC$, and
every forecast built on them. Open question **Q5** carries the hours→cost path forward.

---

## 4. `ManpowerEquipmentLog`

### 4.1 What is counted — headcount, man-hours **and** equipment-hours. All three.

`docs/9.` §4 proposes `ManCount` + `EquipmentCount` only. That is not sufficient for any
productivity measure, for a reason that is arithmetic rather than aesthetic: **a head is not a unit
of work.** 75 workers on a 4-hour Saturday and 75 workers on a 10-hour Thursday are the same
`ManCount` and 2.5× the effort. Every industry productivity definition — CII's, AACE's, P6's — is
denominated in **hours**, never in bodies (§11).

```
ManpowerEquipmentLog                      -- append-only (IAppendOnly); §4.7
  Id, TenantId, ProjectId
  LogDate                 DateTimeOffset  -- calendar-day identity, project timezone, 00:00 normalised
  Shift                   enum { Day = 1, Night = 2 }        NOT NULL, default Day
  WorkCategoryId          Guid            NOT NULL           -- §4.3
  WbsNodeId               Guid NULL       -- control account; NULL = project-level / unattributed
  ActivityId              Guid NULL       -- only when genuinely known
  LabourType              enum { OwnDirect = 1, Subcontract = 2, Hired = 3 }  NOT NULL   -- Q7
  SubcontractorRef        nvarchar(100) NULL                 -- ผู้รับเหมาช่วง, when LabourType <> OwnDirect
  WorkerCount             int             NOT NULL   >= 0    -- the prototype's "คน"
  ManHours                decimal(9,2)    NOT NULL   >= 0    -- §4.2
  OvertimeHours           decimal(9,2)    NOT NULL   >= 0    -- subset of ManHours; explains rate variance (§7.2)
  ManHoursDerived         bit             NOT NULL           -- true = computed, not measured (§4.2)
  EquipmentCount          int             NOT NULL   >= 0
  EquipmentOperatingHours decimal(9,2)    NOT NULL   >= 0
  EquipmentStandbyHours   decimal(9,2)    NOT NULL   >= 0    -- idle / waiting / under repair
  WorkDescription         nvarchar(500) NULL                 -- the prototype's "โครงสร้าง ชั้น 9 + Curtain Wall"
  RelatedWeatherLogId     Guid NULL                          -- §9.4; annotation only, zero arithmetic effect
  RecordedByUserId        Guid, RecordedAt DateTimeOffset    -- server clock (IDateTimeProvider), never client-supplied
  EntryKind               enum { Original = 1, Correction = 2, Retraction = 3 }
  CorrectsLogId           Guid NULL       -- required iff EntryKind <> Original
  CorrectionReason        nvarchar(500) NULL                 -- required iff EntryKind <> Original
  -- NO PlannedManCount. NO SupersededBy. NO UpdatedAt. NO IsDeleted. See §4.6, §4.7.
```

Validation (FluentValidation, 422 on breach):

| Rule | Why |
| :-- | :-- |
| $\textit{ManHours} \le \textit{WorkerCount} \times 24.00$ | catches the 6,000-for-600 typo; a 24 h ceiling accommodates any shift pattern |
| $\textit{ManHours} > 0 \Rightarrow \textit{WorkerCount} > 0$ | hours without people is incoherent |
| $\textit{OvertimeHours} \le \textit{ManHours}$ | OT is a subset, not an addition |
| $EOH + ESH \le \textit{EquipmentCount} \times 24.00$ | same class of guard |
| `WorkCategoryId`, `WbsNodeId`, `ActivityId` all resolve **in the same tenant and project** | 422 `…NotInProject`; cross-tenant → **404** (ADR-0002) |
| `ActivityId` set ⟹ its `WbsNodeId` must equal the row's `WbsNodeId` when that is also set | otherwise the row attributes hours to two places |

A row with `WorkerCount = 0` and `ManHours = 0.00` is **valid and meaningful**: it records
"งานหยุด — ไม่มีคนเข้างาน" on a day the site was shut. It is *not* the same as no row at all — see
§5.7(b) and fixture **M-06b**. This is the `ActualCostResult(Amount, EntryCount)` distinction of
ADR-0013(f), applied to hours.

### 4.2 Man-hours may be measured or derived — but never confused

Two capture modes, per project (`Project.ManHourCaptureMode`):

| Mode | `ManHours` | `ManHoursDerived` |
| :-- | :-- | :-- |
| **`Explicit` (recommended)** | entered by the recorder; need not equal $\textit{WorkerCount} \times H$ (part-days, OT, staggered shifts) | `false` |
| `DerivedFromHeadcount` | computed as $\textit{WorkerCount} \times H$ at write time and **stored**, so the row is self-contained and cannot silently change when $H$ changes | `true` |

$H$ is **`ProjectEotPolicy.FullDayHours`** — the `decimal(4,2)` default-8.00 field Sprint 11 already
shipped. **Do not add a second "hours in a working day" constant.** Two such constants will drift,
and the day a site sets its EOT `FullDayHours` to 12.00 for a two-shift operation while the manpower
module keeps 8.00, every derived hour figure and every PI silently changes by 50%. Flagged to
`system-architect`: if that field is ever moved out of the EOT policy table it must remain **one**
project-level value that both modules read.

`ManHoursDerived = true` must be visible on screen (a small "≈" or a tooltip). A derived hour is an
assumption; a PI built on assumptions is still useful, but the user must know which it is.

### 4.3 Work category ↔ WBS — two orthogonal axes, both on the row

They are not alternatives and neither substitutes for the other:

- **WBS** answers *where in the deliverable structure* — it carries budget, weight and progress.
- **Work category (หมวดงาน)** answers *which trade* — it is how a foreman actually reports
  ("งานโครงสร้าง 75 คน, งานระบบ 5 คน") and it is how the histogram stacks.

```
WorkCategory                              -- per-tenant taxonomy, project-scoped override allowed
  Id, TenantId, ProjectId NULL            -- NULL = tenant-wide default catalogue
  Code nvarchar(16), NameTh nvarchar(100), NameEn nvarchar(100), DisplayOrder int, IsActive bit
```

**The prototype's `work` column ("โครงสร้าง ชั้น 9 + Curtain Wall") is free text and must not become
the category.** Free text cannot be grouped, cannot be rolled up, and cannot be joined to a budget —
so no PI could ever be computed per category. It survives as `WorkDescription`; the *category* is a
foreign key.

**Where budgeted hours live: on `Activity`, not on a new category-budget table.** `BudgetCost`
already lives there (`Activity.BudgetCost`); `WBSNode` carries only `WeightPercentage`. So:

```
Activity.BudgetManHours   decimal(9,2) NULL    -- NULL = not estimated in hours; NEVER 0 as a default
Activity.WorkCategoryId   Guid         NULL    -- Tier 2 only (Q3)
```

That is **one nullable column** for the required Tier-1 behaviour, and it inherits the whole existing
progress machinery for free. Two tiers, and the DoD's "—" behaviour is what the lower tier renders:

| Tier | Requires | PI available at |
| :-- | :-- | :-- |
| **Tier 1 — required for S12** | `Activity.BudgetManHours` | project, WBS node (and subtree) |
| **Tier 2 — optional (Q3)** | + `Activity.WorkCategoryId` | additionally per work category |

Under Tier 1 the per-category PI column renders **"—"** with reason `ActivitiesNotCategorised`. That
is honest and it satisfies S12-FE-02 without inventing a number.

**Matching hours to earned hours** (the join that makes PI computable):

$$
\text{scope}(\ell) = \begin{cases}
\{\ell.\textit{ActivityId}\} & \text{if } \textit{ActivityId} \ne \varnothing\\[2pt]
\{i : i \in \text{subtree}(\ell.\textit{WbsNodeId}) \wedge i.\textit{WorkCategoryId} = \ell.\textit{WorkCategoryId}\} & \text{Tier 2}\\[2pt]
\{i : i \in \text{subtree}(\ell.\textit{WbsNodeId})\} & \text{Tier 1}
\end{cases}
$$

An empty match set ⟹ the row's hours are **unmatched**: excluded from PI, reported in
`ExcludedManHours` with reason `NoMatchingBudgetedScope`, and still plotted on the histogram. Never
silently dropped (the weather-eot §3.2 discipline: nothing disappears without a reason code).

### 4.4 Granularity

**One row per (LogDate, Shift, WorkCategoryId, WbsNodeId, LabourType, SubcontractorRef).** A single
crew's day is one row. Two subcontractors of the same trade on the same node on the same day are two
rows — deliberately, because merging them by hand destroys the only detail that lets a PM see which
crew is the problem.

Consequence: **no unique index on the natural key.** Corrections and retractions create multiple
rows for the same key by design, and legitimate crew splits do too. Duplicate protection is instead:

1. a **write-time warning** — an in-force `Original` already exists for the same key ⟹ **409
   `ManpowerLogAlreadyExists`** unless the request carries `allowDuplicate: true` (which is recorded
   on the row). Warn-and-confirm rather than block: see **Q8**;
2. the `Idempotency-Key` middleware (S13-BE-01) for the offline-replay case, which is the *actual*
   duplicate risk once S12-FE-01's outbox exists.

### 4.5 Time bucketing — half-open, lower-exclusive, identical to $AC$

$$AMH_s(a,b] \;=\; \sum_{\ell \,\in\, \mathcal{M}^{eff}_s\,:\,a < \ell.\textit{LogDate} \le b} \ell.\textit{ManHours}$$

Same convention as `actual-cost.md` §7.5 and the same $\le t$ boundary as ADR-0009's progress read,
so hours and earned hours can never straddle a period boundary differently. Fixture **M-09** shows a
naive inclusive-both-ends implementation reading **0.78** where the answer is **0.90**.

### 4.6 Two rejections from `docs/9.` §4

**(a) `PlannedManCount` must not live on this entity.** The log is immutable (§4.7); a *plan* is not.
Manning plans are revised weekly — that is what planning is. Putting a mutable value on an
append-only row forces one of two bad outcomes: either the plan can never be revised, or the row is
not really append-only. Ruling: a separate, editable, audited table.

```
ManpowerPlan                              -- editable; every change writes an AuditLog row
  Id, TenantId, ProjectId, WorkCategoryId NULL, WbsNodeId NULL
  EffectiveFrom, EffectiveTo DateTimeOffset
  PlannedWorkerCount int NULL             -- NULL = no manning plan; NEVER 0 (ADR-0015 discipline)
  PlannedManHours    decimal(9,2) NULL
```

**Preferred source of the plan is not manual entry at all.** When `Activity.BudgetManHours` exists,
the planned manning curve is *derivable*, exactly as P6 derives its resource histogram — spread each
activity's $BMH_i$ over its planned working days using the **same time-phasing function the S-Curve
already uses for $PV$**:

$$\textit{PlannedManHours}(d) \;=\; \sum_{i \,:\, d \in [\textit{PlannedStart}_i, \textit{PlannedFinish}_i]} \frac{BMH_i}{\textit{WorkingDays}_i}$$

Priority: (1) derived from $BMH$; (2) manual `ManpowerPlan`; (3) **none → "—"**, no overlay on the
histogram, no variance colour. That third case is precisely S12-FE-02's *"แสดง '—' เมื่อไม่มีค่า planned"*.

**(b) `WorkCategory` as a string must not survive.** §4.3.

### 4.7 Immutability — **ruled: append-only, with a correction chain**

> This project has been bitten twice by "append-only by convention, unenforced in fact" —
> `ApprovalAction` (Sprint 9 finding M-01, execution-verified) and `ActivityProgressLog`, whose own
> XML comment claimed immutability while a raw `DbContext` could still `UPDATE` it. The rule now is
> that the marker does the work, not the comment.

`ManpowerEquipmentLog : Entity, ITenantOwned, IAppendOnly`. Three layers, all required, exactly as
`DailyWeatherLog` (weather-eot §8.3):

1. **No route.** Only `POST /api/v1/projects/{id}/manpower-logs` and
   `POST /api/v1/projects/{id}/manpower-logs/{logId}/corrections`. A `PUT`/`PATCH`/`DELETE` returns a
   deliberate **405** with ProblemDetails `ManpowerLogIsImmutable` and a Thai message naming the
   corrections endpoint — a field user must be told what to do instead, not merely refused.
2. **No mutator on the entity.** `private set` throughout; `internal` constructor behind a factory.
3. **`AppendOnlyGuardInterceptor`** blocks `EntityState.Modified` and `Deleted` at `SavingChanges`.

**Justification — four reasons, in order of weight.**

1. **It is claim evidence.** A daily labour allocation sheet is the primary record in a
   loss-of-productivity / disruption claim: AACE RP 25R-03's *measured mile* compares an unimpacted
   period's productivity against an impacted one, and both figures come from exactly these rows. In
   Thai practice the same numbers appear on the รายงานประจำวัน submitted to the ผู้ควบคุมงาน. A
   record that can be edited after the event has no evidentiary weight — and the party who benefits
   from editing it is the party who owns the system.
2. **It is a feeder to an append-only ledger.** If Q5 is ever answered yes, these rows produce
   `ActualCostEntry` rows, which are append-only (ADR-0013). A mutable source feeding an immutable
   ledger cannot be reconciled after the first edit.
3. **The correction is the interesting fact.** "186 คน became 168 คน" is a data-quality signal a PM
   should see. Silent edits destroy exactly the information that makes a productivity trend
   trustworthy.
4. **One rule across the product.** `DailyWeatherLog`, `ActivityProgressLog`, `ActualCostEntry`,
   `ApprovalAction`, `CpmRun` are all append-only. A sixth site-log entity that is not would be the
   exception a future developer generalises from.

**The cost, stated honestly.** A typo now costs a correction row plus a mandatory reason. Two
mitigations, and one deliberately rejected:

- The **outbox is where typos get fixed.** S12-FE-01's IndexedDB draft is client-side and pre-domain;
  editing a queued, not-yet-submitted entry is free and creates no row.
- The corrections endpoint returns the **full replacement row**, so the UI can present the action as
  "แก้ไข" even though storage is append-only. The user's mental model does not have to match the
  storage model.
- **Rejected: a "same-day, same-author grace window" for true edits.** It reintroduces mutability,
  its boundary (23:59? +2 h? server or device clock?) becomes a defect source, and it is exactly the
  loophole an adverse party would probe first. Recorded as **Q6** so the human can overturn it.

**The correction chain** is `DailyWeatherLog`'s, unchanged (weather-eot §8.2), so it is one shared
pattern and one shared test suite:

$$
\mathcal{M}^{eff} = \Big\{\, \ell \in \mathcal{M} \;:\; \nexists\, \ell' \in \mathcal{M},\ \ell'.\textit{CorrectsLogId} = \ell.\textit{Id} \;\wedge\; \ell.\textit{EntryKind} \ne \texttt{Retraction} \,\Big\}
$$

| # | Rule | Violation |
| :-- | :-- | :-- |
| 1 | `CorrectsLogId` resolves in the same tenant **and** project | 422 `ManpowerLogCorrectionTargetNotFound` |
| 2 | At most one entry may point at any entry — `UNIQUE (TenantId, CorrectsLogId) WHERE CorrectsLogId IS NOT NULL` | 409 `ManpowerLogAlreadySuperseded` |
| 3 | A second correction targets the chain's **current tail** | consequence of (2); **M-13** asserts it |
| 4 | `target.RecordedAt < this.RecordedAt`, and `CorrectsLogId <> Id` | 422 `ManpowerLogCorrectionOrdering` |
| 5 | `CorrectionReason` mandatory on `Correction` and `Retraction` | 422 |
| 6 | A correction **replaces**; it does not patch. Its own values govern completely, including `LogDate` and `WorkCategoryId` | — |

**Audit invariant for QA (one query, permanent regression guard):** `AuditLog` never contains an
`Update` or `Delete` row for `EntityName = 'ManpowerEquipmentLog'`, over the whole database, ever.

### 4.8 Who may record and correct

Any user holding the site-recording role on that project (ADR-0018's per-project assignment applies
once it lands). **No approval chain** — weather-eot §8.6's reasoning transfers unchanged: the
approval engine routes on a **money** value, and this entity has none. Feeding it 0.00 would route
every daily log to the lowest authority tier and pollute the `ApprovalAction` ledger, which is
defined as a human decision act on a financial document.

### 4.9 PDPA — counts, never names

The entity records **`WorkerCount`, not workers.** No name, no national-ID number, no biometric, no
photo of an individual. This keeps the log outside ข้อมูลส่วนบุคคล under
พ.ร.บ.คุ้มครองข้อมูลส่วนบุคคล พ.ศ. 2562 and keeps it safe to cache in IndexedDB on a site phone —
which is exactly what S12-SEC-01's DoD asks for (*"ข้อมูลบนอุปกรณ์ (IndexedDB) ไม่เก็บ token/PII เกินจำเป็น"*).
`SubcontractorRef` is a company, not a person. **If anyone later proposes per-worker attendance,
biometric time clocks, or wage rates on this row, that is a new PDPA lawful-basis question and needs
`security-auditor` plus a human decision — it is not an incremental field.** Recorded as **Q10**.

---

## 5. The Productivity Index

### 5.1 What productivity is, and why headcount cannot express it

Productivity is **output per unit of input**. AACE RP 10S-90 (*Cost Engineering Terminology*) defines
productivity as a relative measure of labour efficiency, good or bad, against an established base or
norm — the *norm* is what makes it an index rather than a raw rate.

A headcount ratio has no output term at all. $\textit{ManCount}/\textit{PlannedManCount}$ compares an
input against a planned input; it can be 1.00 on a day when nothing whatsoever was built. It also has
**no agreed direction** — over-manning may be recovery or waste, under-manning may be efficiency or
abandonment — which is itself proof that it is not a performance index: a performance index has a
good end.

> **Ruling.** `ManCount / PlannedManCount` is the **manning ratio** ($MR$), it is displayed under the
> label **"อัตราส่วนกำลังคนเทียบแผน"**, and the identifier `productivityIndex` must never be bound to
> it in any API, DTO, chart or column header. Fixture **M-02**: $MR = 1.25$ (a naive green) on a day
> when $PI = 0.60$ (a real red).

### 5.2 The formula

For scope $s$ over the half-open period $(a,b]$:

$$
\boxed{\;
PI_s(a,b] \;=\; \frac{EMH_s(a,b]}{AMH_s(a,b]}
\;=\; \frac{\displaystyle\sum_{i \in s} BMH_i \cdot \frac{P_i(b) - P_i(a)}{100}}
            {\displaystyle\sum_{\ell \in \mathcal{M}^{eff}_s,\; a < \ell.\textit{LogDate} \le b} \ell.\textit{ManHours}}
\;}
$$

and cumulative to the data date $t$ (project start $t_0$):

$$
PI^{cum}_s(t) \;=\; \frac{\sum_{i \in s} BMH_i \cdot P_i(t)/100}{AMH_s(t_0,\,t]}
$$

**This is exactly the prototype's own tile subtitle, "EV ต่อ ชม.แรงงาน / แผน".** Write the money form
out and the money cancels:

$$
\frac{EV_s / AMH_s}{BAC_s / BMH_s}
= \frac{EV_s}{BAC_s} \cdot \frac{BMH_s}{AMH_s}
= \frac{\sum BC_i P_i/100}{\sum BC_i} \cdot \frac{BMH_s}{AMH_s}
\;\overset{\ast}{=}\; \frac{EMH_s}{AMH_s}
$$

($\ast$ holds exactly when the scope is a single activity or when $BMH_i \propto BC_i$ across the
scope; the earned-hours form is the correct general one and is what CM+ computes.) So this document
does not overrule the prototype — it states precisely what the prototype's tile already means, and
fixture **M-01** verifies both expressions land on **0.90**.

**Equivalent readings, all of which must give the same number** (assert in tests):

$$PI = \frac{EMH}{AMH} = \frac{\text{planned unit rate}}{\text{actual unit rate}} = \frac{BMH/Q_{tot}}{AMH/Q_{done}} \quad\text{when a quantity }Q\text{ exists}$$

### 5.3 Direction — say it plainly

| $PI$ | Meaning | Colour (design tokens) |
| :-- | :-- | :-- |
| $> 1.00$ | Better than budget: fewer hours consumed than the work was worth | Success green `#1F7A4D` |
| $= 1.00$ | Exactly on the estimate | Success green |
| $< 1.00$ | Worse than budget: hours consumed exceed the work's worth | see band below |

**Higher is better.** The neutral point is exactly 1.00. Default display bands, matching the
prototype (which renders **0.97 in green** and legends the histogram *"ตามแผน ±5%"*):

| Band | Colour | Label |
| :-- | :-- | :-- |
| $PI \ge 0.95$ | Success green `#1F7A4D` | ตามแผน / ดีกว่าแผน |
| $0.85 \le PI < 0.95$ | Gold `#C9A227` | ต่ำกว่าแผนเล็กน้อย |
| $PI < 0.85$ | Critical red `#B23A3A` | ต่ำกว่าแผนมาก |
| `null` | Muted grey, text **"—"** | ไม่มีข้อมูลแผน (reason in tooltip) |

Thresholds are per-project configuration with these defaults (**Q4**). Outside $[0.20, 3.00]$ the API
attaches an advisory `ImplausiblePi` data-quality warning — it does **not** change the colour or the
value; a genuine 3.5 exists and hiding it would be worse than explaining it.

⚠ **UI defect to fix, found in the prototype** (`ECM Planning Prototype.dc.html:872`):
`dColor: d >= -10 ? '#1F7A4D' : '#B23A3A'` colours the manning delta **green whenever actual ≥ plan −
10**, so **+30 คน over plan renders green**. Over-manning is not good news; at best it is neutral and
at worst it is the most expensive failure mode this module exists to reveal. $MR$ must use a
**neutral variance palette** (§9.2), never a good/bad one.

### 5.4 Where "planned" comes from — and why "unknown" is not zero

$PI$'s numerator needs a budget of **hours**. Sources, in strict priority:

| # | Source | Status |
| :-- | :-- | :-- |
| 1 | `Activity.BudgetManHours` — from the estimator's build-up (ราคากลาง / BoQ labour content) | **Ruled: the source of truth.** One new nullable column (§4.3) |
| 2 | Imported from **P6 `Budgeted Labor Units`** (`TASKRSRC` in the XER) — same quantity, same semantics (§11) | Ruled: valid, maps onto (1) |
| 3 | Norms library (MH per unit × installed quantity) | Out of scope for S12; would need a quantity ledger. Recorded, not built |
| 4 | **Derived from `BudgetCost ÷ an assumed labour rate`** | **Forbidden.** `BudgetCost` includes material, plant, subcontract and prelims; dividing it by a guessed rate produces a number with the shape of a budget and none of its meaning |

**`Activity.BudgetManHours IS NULL` means *not estimated in hours*. It never means 0.00, and it is
never seeded with a placeholder** — the ADR-0015 discipline, for the identical reason: a seeded
default is indistinguishable from a decision once it is in production data.

Consequently PI is a nullable result, and the reason must travel with it. Follow ADR-0013(f)'s
`ActualCostResult` precedent exactly — the reader returns counts, so two different zeros can be
worded differently:

```csharp
public sealed record ProductivityIndexResult(
    decimal? Value,                     // null => render "—"; NEVER 0m as a stand-in
    PiNullReason? Reason,
    decimal EarnedManHours,             // 0.00 is a real value here
    decimal ActualManHoursInScope,
    decimal ActualManHoursTotal,        // includes excluded scopes; drives Coverage
    decimal ExcludedManHours,
    decimal CoveragePercentage,
    int     LogEntryCount,              // 0 vs >0 distinguishes "not reported" from "reported as zero"
    IReadOnlyList<PiDataQualityWarning> Warnings);

public enum PiNullReason
{
    NotReported = 1,          // no log rows at all in the period
    NoActualManHours = 2,     // rows exist, hours sum to 0.00
    NoBudgetManHours = 3,     // no in-scope activity has BudgetManHours
    NoProgressInPeriod = 4,   // no progress observation in the bucket (§5.5)
    NoMatchingBudgetedScope = 5,
    ActivitiesNotCategorised = 6,  // Tier 1 asked for a category PI
}
```

### 5.5 The reporting-cadence rule — the bucket must contain a progress observation

`ActivityProgressLog` is a **step function** keyed on `PeriodEndDate` (ADR-0009). Hours arrive daily;
progress commonly arrives weekly. A daily PI computed naively against a weekly progress report gives
five days of $0.00$ and one day of $8.00$ — fixture **M-08**.

> **Ruling.** A PI bucket is valid only if it contains at least one progress observation for the
> in-scope activities. Otherwise $PI = $ `null`, reason `NoProgressInPeriod`, and the hours are still
> plotted on the histogram. When the caller asks for a bucket finer than the progress-observation
> interval, the API returns **progress-aligned buckets** and says so:
> `{"requestedBucket":"Day","bucketingApplied":"ProgressAligned","bucketAdjusted":true}`.
> It must **never** silently return a day-labelled bucket whose numerator spans a week.

The interval is detected from the data — the distinct `PeriodEndDate` values in the window. No
configuration, nothing to get wrong at setup.

**Corollary — the headline is cumulative.** The KPI tile shows $PI^{cum}(t)$, which is always
computable from the step function and is what the prototype's "Productivity เฉลี่ย 0.97" tile is. The
trend chart shows period PI on progress-aligned buckets. They will differ (**M-15**: cumulative
**0.95**, that week **0.80**) and both are correct — label them distinctly or the first user reports
it as a bug.

### 5.6 Scope — what is in PI's denominator

> **Ruling.** PI is computed over **matched scope only**: hours that map (§4.3) to at least one
> activity with a non-null `BudgetManHours`. Unmatched and unbudgeted hours are excluded from **both**
> numerator and denominator, are reported in `ExcludedManHours`, and always appear on the histogram.

Coverage is disclosed on the tile whenever $\kappa < 100\%$:

$$\kappa \;=\; \frac{AMH^{in\text{-}scope}}{AMH^{total}} \times 100$$

The alternative — put all hours in the denominator and only budgeted work in the numerator — is
**rejected**: it mixes a measured ratio with an unmeasured input and reports "productivity" that
falls purely because someone has not estimated part of the job in hours. That is the same error
`actual-cost.md` §3 forbids when it refuses to pro-rate a parent's AC down to its children.
Fixture **M-05** gives three candidate answers — **0.80** (correct), 0.57 (unknown-as-zero), 0.86
(unknown-as-on-plan) — and only one is defensible.

### 5.7 Degenerate cases — deterministic, no exceptions thrown

| # | Condition | Result | Note |
| :-- | :-- | :-- | :-- |
| a | $EMH > 0$, $AMH = 0$, **no log rows** | `null`, `NotReported` | + warning `ProgressWithoutManHours` |
| b | $EMH > 0$, $AMH = 0$, **rows exist** (all zero) | `null`, `NoActualManHours` | **different Thai copy** from (a) — this is why `LogEntryCount` is on the result (ADR-0013(f)) |
| c | $EMH = 0$, $AMH > 0$ | **`0.00` — defined, not null** | Hours spent, nothing earned. Mirrors evm-formulas' $EV=0 \wedge AC>0 \Rightarrow CPI = 0$ |
| d | $EMH = 0$, $AMH = 0$, no rows | `null`, `NotReported` | |
| e | Every in-scope $BMH$ is `NULL` | `null`, `NoBudgetManHours` | The DoD's "—" case |
| f | $BMH = 0.00$ **explicitly** with $AMH > 0$ | in scope; numerator 0 ⟹ $PI = 0.00$ + warning `UnbudgetedLabourHours` | An explicit zero is a decision ("no labour budgeted here"); those hours are real and unplanned. Distinct from `NULL`. **Q6** |
| g | $EMH > AMH$ by a large factor | compute as-is; advisory `ImplausiblePi` if $> 3.00$ | Never clamp — clamping hides the progress-overstatement that causes it |
| h | Progress *decreases* in the bucket (a correction) ⟹ $\Delta EMH < 0$ | compute as-is; $PI$ may be negative | Return it, flag `NegativeEarnedHours`. Same posture as `actual-cost.md`'s negative-AC rule: do not clamp, do not hide |
| i | Partially-reported day (§10 **M-07**) | compute over reported scope; `IsPartiallyReported = true` | Never impute 0 hours for the silent categories |

**Division is guarded by the numerator's existence, not by a try/catch.** Check
`AMH == 0` → null *before* dividing. No `NaN`, no `Infinity`, no exception, ever.

---

## 6. Aggregation — ratio of sums, never average of ratios

> **Ruling.** PI rolls up across categories, WBS levels and time as
> $$PI_{\text{agg}} = \frac{\sum_s EMH_s}{\sum_s AMH_s}$$
> i.e. sum the numerators, sum the denominators, divide **once**. Equivalently — and this is the form
> to state on screen — it is the **actual-man-hour-weighted** mean of the component indices:
> $$PI_{\text{agg}} = \frac{\sum_s PI_s \cdot AMH_s}{\sum_s AMH_s}$$
> The two are identical because $EMH_s = PI_s \cdot AMH_s$. An **unweighted** mean of category PIs is
> wrong, and it is wrong in a way that is invisible until a small scope has a spectacular ratio.

**Weighted by hours, not by money, not by headcount, not by node weight.** $\textit{WeightPercentage}$
is budget-proportional and is right for progress rollup (evm-formulas.md §"Progress rollup"); it is
wrong here, because PI's denominator is hours and the weight of a scope in a ratio of sums *is* its
denominator.

**The same rule governs time.** Averaging daily PIs weights every day equally regardless of how many
hours it contained — so a 50-hour rain day counts as much as a 600-hour production day
(**M-04**: naive **0.71** vs correct **0.92**).

**Worked example where naive and correct disagree — fixture M-03, in full:**

| Scope | $\Delta P$ | $BMH$ | $EMH$ | $AMH$ | $PI_s$ |
| :-- | --: | --: | --: | --: | --: |
| N1 / งานโครงสร้าง | 3.50% | 12,000.00 | 420.00 | 600.00 | 0.70 |
| N2 / งานสถาปัตยกรรม | 2.00% | 6,000.00 | 120.00 | 100.00 | 1.20 |
| N3 / งานระบบ | 2.00% | 4,000.00 | 80.00 | 40.00 | 2.00 |
| **Project** | | | **620.00** | **740.00** | |

- **Naive (unweighted mean):** $(0.70 + 1.20 + 2.00)/3 = 3.90/3 = \mathbf{1.30}$ → **green, "ดีกว่าแผน"**.
- **Correct (ratio of sums):** $620.00 / 740.00 = 0.837837\ldots = \mathbf{0.84}$ → **red, "ต่ำกว่าแผนมาก"**.

The naive answer is driven by a 40-hour MEP scope — 5.4% of the site's effort — carrying a third of
the weight. The two answers sit in different colour bands and imply opposite management action.

---

## 7. Relationship to EVM — deliberately independent, and reconcilable

### 7.1 What PI shares with EVM, exactly

PI is $CPI$ computed in **hour units, over labour only**. It shares one input with $EV$ and must
share it *literally*, not approximately:

> **Binding rule.** $EMH$ reads progress through the **same** ADR-0009 step function, at the **same**
> $\le t$ boundary, that `EvmDataReader` uses for $EV$. Two different progress reads on one screen is
> a defect, not a nuance. The only difference between the $EV$ loop and the $EMH$ loop is the
> multiplier: `BudgetCost` vs `BudgetManHours`.

PI shares **nothing** with $AC$. $AMH$ comes from `ManpowerEquipmentLog`; $AC$ comes from
`ActualCostEntry`. They are separate ledgers on purpose (§3).

### 7.2 Why PI and $CPI$ may legitimately disagree — and by exactly how much

Let $r^{plan}$ and $r^{act}$ be the planned and actual average labour cost per hour. Then labour-only
$CPI$ decomposes exactly:

$$
CPI_L \;=\; \frac{EV_L}{AC_L} \;=\; \frac{EMH \cdot r^{plan}}{AMH \cdot r^{act}} \;=\; PI \times \underbrace{\frac{r^{plan}}{r^{act}}}_{\textstyle RF}
$$

so $CPI_L = PI \cdot RF$, and the money variance splits into the two classical components:

$$
\underbrace{CV_L}_{EV_L - AC_L} \;=\; \underbrace{(EMH - AMH)\,r^{plan}}_{\text{efficiency (usage) variance}} \;+\; \underbrace{(r^{plan} - r^{act})\,AMH}_{\text{rate (price) variance}}
$$

**PI is the efficiency half only.** A crew can be genuinely efficient ($PI = 1.05$) while its hours
are expensive ($RF = 0.87$ from overtime and a wage rise), giving $CPI_L = 0.92$. Both numbers are
correct and they are *supposed* to differ. Fixture **M-11** pins the arithmetic; the screen must show
the decomposition rather than leaving a user to assume one of the two is broken. `OvertimeHours` is
on the log row (§4.1) precisely so the rate half can be explained.

### 7.3 Against **project** $CPI$ there is no identity at all

Project $CPI$ covers material, subcontract, plant and site overhead. PI covers labour hours. They are
not two views of one quantity and **must not be presented as if reconciling**. Required screen copy
where both appear:

> **PI วัดประสิทธิภาพชั่วโมงแรงงานเท่านั้น — ไม่ใช่ตัวเดียวกับ CPI ซึ่งรวมค่าวัสดุ ผู้รับเหมาช่วง และเครื่องจักร
> ทั้งสองค่าต่างกันได้โดยไม่ถือว่าผิด**

CM+ does **not** compute a labour-only $CPI$ in Sprint 12: `Activity.BudgetCost` is not split by cost
category, so $EV_L$ does not exist. §7.2 is the specification for it when it does.

### 7.4 The circular-earning-basis trap

If percent complete is itself derived from hours expended, then $EMH \equiv AMH$ and $PI \equiv 1.00$
identically — the index measures nothing while looking perfectly healthy. This is not hypothetical:
P6's **Units % Complete** is `Actual Units / At Completion Units`, and a foreman reporting "we're 50%
done, we've used half the hours" does the same thing by hand.

> **Rule.** PI is meaningful only where progress is **physical** — quantity installed, milestone
> achieved, or an independent supervisor assessment. Progress imported from a P6 project configured
> with `% Complete Type = Units` must not be trusted for PI, and the reconciliation note in §11 says
> so. Fixture **M-12**.

Recommended (not required, and nothing may be gated on it): raise an advisory
`CircularEarningBasisRisk` when $|PI - 1.00| < 0.01$ for the same scope across three or more
consecutive buckets.

### 7.5 PI never feeds anything

Assertable invariant, and the counterpart of weather-eot §2.1's no-side-effects rule: no code path
computing $EV$, $AC$, $CV$, $CPI$, $SV$, $SPI$, $EAC$, $ETC$, $VAC$, $TCPI$, a payment certificate, a
VO, or an S-Curve point may read `ManpowerEquipmentLog` or a PI value. Fixture **M-11c**.

---

## 8. Equipment — utilisation, not productivity

**Do not merge man-hours and equipment-hours into one denominator.** They are different resources
with different scarcity and different costs; their sum has no meaning, and a project that hires one
extra excavator would see its "productivity" fall. Fixture **M-10** shows the error: **0.71** instead
of **0.84** on M-03's data.

Two equipment metrics, both dimensionless, both `decimal(5,2)` percentages:

$$
U \;=\; \frac{\sum EOH}{\sum (EOH + ESH)} \times 100
\qquad\qquad
A \;=\; \frac{\textit{units operating}}{\textit{units on site}} \times 100
$$

$U$ (utilisation, hours-based) is the one that matters for cost — standby hours on a hired plant item
are paid and produce nothing, and `docs/วิเคราะห์ฯ` §5 names *"การบริหารจอดรอเครื่องจักร"* as a primary
cause of budget overrun. $A$ (availability, count-based) is the prototype's **"14 / 16"** tile.

**Equipment PI is not computed in Sprint 12.** It would need budgeted equipment-hours per activity,
which no source supplies. If added later it is the identical formula
$EPI = \textit{earned equipment-hours} / \textit{actual operating hours}$, reported separately and
never blended into $PI$. Until then the equipment column renders "—" for PI, which is correct.

Zero cases: $EOH + ESH = 0 \Rightarrow U = $ `null` ("—"), not 0.00.
$\textit{units on site} = 0 \Rightarrow A = $ `null`.

---

## 9. The histogram and the screen (S12-FE-02)

### 9.1 Three charts, not one — because they have different X-bucketing rules

| | Chart | Y | X buckets | Bars/points mean |
| :-- | :-- | :-- | :-- | :-- |
| 1 | **Manpower histogram** (prototype's "Histogram กำลังคน 7 วันล่าสุด") | **Man-hours** (default), with a คน/ชม. toggle | **Calendar** day (default, last 7), week, month | Effort actually expended in the bucket, **stacked by work category** |
| 2 | **PI trend** | $PI$, fixed 0 – 2.0 axis, reference line at 1.00 | **Progress-aligned** (§5.5) | Efficiency of the bucket |
| 3 | Equipment (optional) | Hours, stacked operating / standby | Same as chart 1 | Utilisation, with $U$ as an overlaid line |

**Y = man-hours, not headcount.** Hours are additive across shifts and overtime and are the quantity
PI actually uses; heads are not comparable between a 4-hour Saturday and a 10-hour Thursday. The
prototype's คน view is kept as a toggle and as the bar's data label, because it is what a foreman
recognises. When `ManHourCaptureMode = DerivedFromHeadcount` the two views are proportional and the
toggle is cosmetic — say so in the tooltip rather than hiding it.

### 9.2 Bars, plan overlay, colour

- **Plan overlay:** a target line (or ghost bar) at $\textit{PlannedManHours}$, drawn in gold
  `#C9A227` — the design system's baseline-marker role. Source priority per §4.6.
- **Bar colour** follows the total-vs-plan variance, using a **neutral** palette (§5.3's defect note):

| Condition | Colour | Label |
| :-- | :-- | :-- |
| within ±5% of plan | Secondary slate-blue `#33507A` | ตามแผน ±5% |
| more than 5% **below** plan | Critical red `#B23A3A` | ต่ำกว่าแผน |
| more than 5% **above** plan | Gold `#C9A227` (**never green**) | สูงกว่าแผน |
| **no plan** | Neutral slate, **no** variance colour, delta column **"—"** | — |

That last row is S12-FE-02's *"แสดง '—' เมื่อไม่มีค่า planned"*, applied to the histogram. Absence of a
plan must not render as red (a shortfall that was never asserted) nor as green.

- **Non-working days** (per the project calendar, weather-eot §3.3) are shaded but **shown**. A
  Sunday pour is legitimate and hiding the column hides real overtime.
- **Partially-reported bucket** (§10 M-07) is hatched, carries a badge, and its tooltip names the
  categories that have not reported.
- **PI trend nulls are gaps.** Never plot 0, never interpolate across a gap, never drop the bucket
  silently — draw the point as a hollow marker at the axis break with the reason in the tooltip.
- **Weather annotation:** buckets containing a `DailyWeatherLog` with `Impact = FullStoppage` carry a
  small marker (§9.4). Annotation only — **zero arithmetic effect on PI**, exactly like weather-eot
  §3.6's notice dates.

### 9.3 KPI tiles (the prototype's four, corrected)

| Tile | Value | Rule |
| :-- | :-- | :-- |
| กำลังคนวันนี้ | `WorkerCount` today + delta vs plan | delta uses the neutral palette (§5.3 defect) |
| เครื่องจักรทำงาน | $A$ as "14 / 16" | "—" when no units on site |
| ชม.ทำงานสะสม (เดือนนี้) | $AMH$ month-to-date | half-open bucket (§4.5) |
| **Productivity Index** | $PI^{cum}(t)$, 2 dp | **"—"** + reason tooltip when null; coverage badge when $\kappa < 100\%$ |

### 9.4 Relationship to the weather log

A stoppage day legitimately produces a very low PI (**M-04**, 2026-07-08 → 0.24). That is a *fact*
about the day, not a crew failure, and a trend line that does not say so will be misread. The link is
an annotation, and a foundation for a future measured-mile analysis (AACE RP 25R-03), which needs the
unimpacted-period baseline that Sprint 12 does not build. Recorded as **Q9**.

---

## 10. Fixtures — `qa-engineer` and `backend-developer` build directly from these

### 10.0 Shared setup

**Project `P-MEQ`.** Calendar **`TH-6Day`** (Mon–Sat working, Sunday non-working), reused verbatim
from weather-eot §10.0 so the two suites tie together. July 2026: **5 Sun, 6 Mon, 7 Tue, 8 Wed,
9 Thu, 10 Fri, 11 Sat**. `ManHourCaptureMode = Explicit`. `FullDayHours = 8.00`.

| WBS node | Activity | Work category | `BudgetCost` | `BudgetManHours` |
| :-- | :-- | :-- | --: | --: |
| N1 `01` งานโครงสร้าง | A-STR | C-STR | 3,600,000.00 | **12,000.00** |
| N2 `02` งานสถาปัตยกรรม | A-ARC | C-ARC | 1,200,000.00 | **6,000.00** |
| N3 `03` งานระบบ | A-MEP | C-MEP | 1,600,000.00 | **4,000.00** |

**Each fixture is independent** — it starts from this setup and states its own progress and log rows.
Every fixture asserts, in addition to its stated expectation: `Value`, `Reason`, `EarnedManHours`,
`ActualManHoursInScope`, `ActualManHoursTotal`, `CoveragePercentage`, `LogEntryCount`.

---

### M-01 — the base case, and the money-form identity

2026-07-07 (Tue), scope N1/C-STR. `WorkerCount = 25`, `ManHours = 200.00`.
A-STR progress: log `PeriodEndDate = 2026-07-06` at 30.00%; log `PeriodEndDate = 2026-07-07` at 31.50%.

- $\Delta P = 1.50\%$ ⟹ $EMH = 12{,}000.00 \times 0.0150 = \mathbf{180.00}$ h
- $$PI = \frac{180.00}{200.00} = \mathbf{0.90}$$
- Lost man-hours $L = 200.00 - 180.00 = \mathbf{20.00}$ h
- **Money cross-check (the prototype's "EV ต่อ ชม.แรงงาน / แผน"):** $\Delta EV = 3{,}600{,}000 \times
  0.0150 = 54{,}000.00$; actual = $54{,}000/200 = 270.00$ THB/h; planned = $3{,}600{,}000/12{,}000 =
  300.00$ THB/h; ratio $= 270/300 = \mathbf{0.90}$ ✓ — **both expressions must return the same value.**
- Coverage 100.00%. Colour band: $0.85 \le 0.90 < 0.95$ → **gold** ("ต่ำกว่าแผนเล็กน้อย").

### M-02 ★ — the manning ratio is not the Productivity Index

**The naive-vs-correct fixture. Build this one first.**

2026-07-09 (Thu), scope N1/C-STR. `ManpowerPlan.PlannedWorkerCount = 20` for that date.
Actual `WorkerCount = 25`, `ManHours = 200.00`. A-STR progress 31.50% → 32.50%.

- $\Delta P = 1.00\%$ ⟹ $EMH = \mathbf{120.00}$ h; $$PI = \frac{120.00}{200.00} = \mathbf{0.60}$$
- $$MR = \frac{25}{20} = \mathbf{1.25}$$
- $L = 80.00$ h lost — **40% of the day's effort produced nothing**.

| Reading | Value | Colour it would get | Verdict |
| :-- | --: | :-- | :-- |
| `ManCount / PlannedManCount` labelled "PI" | **1.25** | green "ดีกว่าแผน" | **the defect** — docs/9. §4 and US-12.2 both invite it |
| $EMH/AMH$ | **0.60** | red | correct |

**Assertions:** the response contains `productivityIndex = 0.60` **and** `manningRatio = 1.25` under
distinct names; no field named `productivityIndex` ever equals 1.25 for this input; the manning delta
(+5 คน) is **not** coloured green (§5.3).

### M-03 ★ — cross-scope aggregation: weighted, not unweighted

2026-07-10 (Fri). Full table in §6.

| Scope | `WorkerCount` | `ManHours` | $\Delta P$ | $EMH$ | $PI_s$ |
| :-- | --: | --: | --: | --: | --: |
| N1/C-STR | 75 | 600.00 | 3.50% | 420.00 | 0.70 |
| N2/C-ARC | 13 | 100.00 | 2.00% | 120.00 | 1.20 |
| N3/C-MEP | 5 | 40.00 | 2.00% | 80.00 | 2.00 |

- **Correct:** $620.00 / 740.00 = 0.837837\ldots \Rightarrow \mathbf{0.84}$ (red band)
- **Naive unweighted mean:** $3.90/3 = \mathbf{1.30}$ (green band)
- **Weighted-mean identity (assert):** $(0.70{\times}600 + 1.20{\times}100 + 2.00{\times}40)/740 = 620/740$ ✓
- Note `ManHours` is not `WorkerCount × 8.00` for N2 and N3 — part-days and OT. Under
  `Explicit` capture that is legal, and it is exactly why hours are the denominator.

### M-04 ★ — time aggregation, and a stoppage day

Scope N1/C-STR over three working days.

| Date | `ManHours` | $\Delta P$ | $EMH$ | daily $PI$ |
| :-- | --: | --: | --: | --: |
| 2026-07-06 (Mon) | 600.00 | 5.00% | 600.00 | 1.00 |
| 2026-07-07 (Tue) | 600.00 | 4.50% | 540.00 | 0.90 |
| 2026-07-08 (Wed) | 50.00 | 0.10% | 12.00 | 0.24 |

- **Correct period PI:** $1{,}152.00 / 1{,}250.00 = 0.9216 \Rightarrow \mathbf{0.92}$
- **Naive mean of daily PIs:** $2.14/3 = 0.71333\ldots \Rightarrow \mathbf{0.71}$
- 2026-07-08 is weather-eot fixture **W-01**'s heavy-rain full-stoppage date. The bucket carries
  `HasRecordedWorkStoppage = true`; **PI is unchanged by it** (annotation only, §9.4).
- Direction note for reviewers: naive **under**-states here and **over**-states in M-03 — the error
  has no consistent sign, so "it's roughly right" is not available as a defence.

### M-05 ★ — no planned value ⟹ "—", never 0, never a silent fallback

2026-07-10, project rollup, two scopes:

| Scope | `BudgetManHours` | `ManHours` | $\Delta P$ | $EMH$ |
| :-- | --: | --: | --: | --: |
| N1/C-STR | 12,000.00 | 300.00 | 2.00% | 240.00 |
| N3/C-MEP | **NULL** | 120.00 | 2.00% | **unknown** |

- Category C-MEP: $PI =$ **`null`**, `Reason = NoBudgetManHours` → UI **"—"**.
- **Project (correct):** $240.00/300.00 = \mathbf{0.80}$;
  `ActualManHoursInScope = 300.00`, `ActualManHoursTotal = 420.00`, `ExcludedManHours = 120.00`,
  `CoveragePercentage = 71.43` (from $300/420 = 71.4285\ldots$, away-from-zero).

| Wrong reading | Value | Why it is wrong |
| :-- | --: | :-- |
| unknown ⟹ $EMH = 0$ | $240/420 = \mathbf{0.57}$ | charges 120 h against a budget that does not exist — the exact ADR-0013 null-vs-zero failure |
| unknown ⟹ "assume on plan" | $360/420 = \mathbf{0.86}$ | invents 120 earned hours |
| fall back to $MR$ | (varies) | silently changes what the number *means* mid-screen |

### M-06 — degenerate numerators and denominators

| # | Inputs | Expected |
| :-- | :-- | :-- |
| a | $EMH = 150.00$, no log rows | `null`, `NotReported`, `LogEntryCount = 0`, warning `ProgressWithoutManHours` |
| b | $EMH = 150.00$, **3 rows** all `WorkerCount = 0, ManHours = 0.00` | `null`, `NoActualManHours`, `LogEntryCount = 3`, **different Thai message from (a)** |
| c | $EMH = 0.00$, $AMH = 160.00$ | **`0.00`**, `Reason = null` — a defined value, red |
| d | $EMH = 0.00$, $AMH = 0.00$, no rows | `null`, `NotReported` |
| e | All in-scope $BMH$ `NULL` | `null`, `NoBudgetManHours` |
| f | $BMH = 0.00$ explicit, $AMH = 40.00$ | **`0.00`** + warning `UnbudgetedLabourHours` (**Q6**) |
| g | Hours logged against a node with no matching activity | `null`/excluded, `NoMatchingBudgetedScope`; hours still on the histogram |
| h | Progress corrected downward: $\Delta P = -1.00\%$, $AMH = 200.00$ | $EMH = -120.00$, $PI = \mathbf{-0.60}$, warning `NegativeEarnedHours`. **Do not clamp** |
| i | Cross-tenant `WbsNodeId` | **404** (ADR-0002), never 422 — a cross-tenant id must not confirm existence |

### M-07 — a partially-reported day

2026-07-10, 18:00 cut-off. Three categories are expected (each has an in-progress activity **and** a
`ManpowerPlan` row). Planned heads: C-STR 70, C-ARC 12, **C-MEP 45**. Only C-STR and C-ARC report.

| Reported scope | `WorkerCount` | `ManHours` | $EMH$ |
| :-- | --: | --: | --: |
| N1/C-STR | 75 | 600.00 | 420.00 |
| N2/C-ARC | 13 | 100.00 | 120.00 |

- $$PI = \frac{540.00}{700.00} = 0.771428\ldots \Rightarrow \mathbf{0.77}$$
- `IsPartiallyReported = true`, `ReportedCategoryCount = 2`, `ExpectedCategoryCount = 3`.
- Manning delta must be computed **over reported scope only**: planned 82 vs actual 88 = **+6**.
- **Negative assertion:** imputing 0 for C-MEP gives a manning delta of $88 - 127 = \mathbf{-39}$ —
  "ขาดคน 39" for a shortfall that was never reported, on a day nobody was absent. The histogram bar is
  hatched; the tooltip names C-MEP.

### M-08 ★ — the reporting-cadence trap (hours daily, progress weekly)

Scope N1/C-STR, week 2026-07-06 (Mon) … 2026-07-11 (Sat), `TH-6Day`.

Hours: Mon 200.00 · Tue 200.00 · Wed 200.00 · Thu 240.00 · Fri 240.00 · Sat 120.00 ⟹ **1,200.00** h.
Progress: **one** `ActivityProgressLog`, `PeriodEndDate = 2026-07-11`, 30.00% → 38.00%
⟹ $EMH = 12{,}000.00 \times 0.08 = \mathbf{960.00}$ h.

- **Correct:** one progress-aligned bucket (2026-07-05, 2026-07-11],
  $$PI = \frac{960.00}{1{,}200.00} = \mathbf{0.80}$$
  Response carries `requestedBucket = "Day"`, `bucketingApplied = "ProgressAligned"`,
  `bucketAdjusted = true`.

| Wrong reading | Result | Why |
| :-- | :-- | :-- |
| daily buckets, $EMH = 0$ where no progress log | Mon–Fri **0.00** (five red days), Sat **8.00** | treats "not measured" as "nothing earned" |
| average of that daily series | $8.00/6 = \mathbf{1.33}$ | compounds M-04's error with this one |
| daily bucket for Sat with the week's earned hours | **8.00** | numerator spans 6 days, denominator 1 |

### M-09 — half-open bucket boundary

A-STR progress logs: `2026-05-31` → 10.00%; `2026-06-30` → 25.00%; `2026-07-31` → 40.00%.
Hours: 2026-06-30 = **300.00**; 2026-07-01 … 2026-07-31 = **2,000.00**.

- July bucket $(2026\text{-}06\text{-}30,\ 2026\text{-}07\text{-}31]$:
  $EMH = 12{,}000 \times 15.00\% = 1{,}800.00$; $AMH = 2{,}000.00$
  $$PI_{\text{July}} = \frac{1{,}800.00}{2{,}000.00} = \mathbf{0.90}$$
- **Negative assertion:** an inclusive `[a,b]` implementation pulls 2026-06-30's 300.00 h into July as
  well, giving $1{,}800/2{,}300 = 0.782608\ldots \Rightarrow \mathbf{0.78}$, and double-counts it in June.

### M-10 — equipment metrics, and the mixed-denominator error

2026-07-10 (M-03's data). Equipment: 16 units on site, **14 operating**;
$EOH = 112.00$, $ESH = 16.00$ (idle) $+\ 8.00$ (breakdown) $= 24.00$.

- $$A = \frac{14}{16} = \mathbf{87.50\%}\qquad U = \frac{112.00}{136.00} = 0.823529\ldots = \mathbf{82.35\%}$$
- **PI is unchanged: 0.84.**
- **Negative assertion:** summing man-hours and equipment-hours gives
  $620.00 / (740.00 + 136.00) = 620/876 = 0.707762\ldots \Rightarrow \mathbf{0.71}$. Assert the API
  never returns this.
- $EOH + ESH = 0 \Rightarrow U = $ `null` ("—"), not 0.00.

### M-11 — PI vs $CPI$: legitimate disagreement, and the variance split

Supplied directly (no schema dependency); this fixture is a **reconciliation reference** and the
specification for a future labour-$CPI$ feature.

$EMH = 1{,}000.00$ h · $AMH = 950.00$ h · $r^{plan} = 400.00$ THB/h · $r^{act} = 460.00$ THB/h.

- $$PI = \frac{1{,}000.00}{950.00} = 1.052631\ldots \Rightarrow \mathbf{1.05}\ \text{(green)}$$
- $EV_L = 400{,}000.00$; $AC_L = 437{,}000.00$;
  $$CPI_L = \frac{400{,}000.00}{437{,}000.00} = 0.915331\ldots \Rightarrow \mathbf{0.92}\ \text{(red)}$$
- **Identity (assert):** $PI \times RF = 1.052631\ldots \times \frac{400}{460} = 0.915331\ldots = CPI_L$ ✓
- **Variance split (assert, to the baht):**
  efficiency $= (1{,}000 - 950) \times 400 = \mathbf{+20{,}000.00}$;
  rate $= (400 - 460) \times 950 = \mathbf{-57{,}000.00}$;
  sum $= \mathbf{-37{,}000.00} = EV_L - AC_L$ ✓
- **M-11c (side effects):** run the whole PI pipeline and assert **nothing** outside the PI read model
  and `AuditLog` changed — no `ActualCostEntry`, no `EvmPeriodSnapshot`, no `Activity` field, no
  `ProjectFinanceLedger` row. The weather-eot **W-14** pattern.

### M-12 — the circular earning basis

$BMH = 12{,}000.00$; hours to date $6{,}000.00$; progress reported as **50.00% because the hours are
50% of budget**.

- $EMH = 6{,}000.00$; $PI = \mathbf{1.00}$ — the engine computes it and **cannot** know it is
  meaningless. Assert 1.00.
- Assert the advisory `CircularEarningBasisRisk` fires after three consecutive such buckets, and that
  **nothing is gated on it** — the value is still 1.00, the colour is still green.
- Documentation/UI assertion: the PI panel states the progress basis in use.

### M-13 — immutability and the correction chain

Starting state: M-03's 2026-07-10.

| Step | Action | Expected |
| --: | :-- | :-- |
| 1 | `POST` original: N1/C-STR, 75 คน, 600.00 h | `EntryKind = Original`; N1 $PI = 420.00/600.00 = \mathbf{0.70}$ |
| 2 | `PUT` / `PATCH` / `DELETE` that row | **405** `ManpowerLogIsImmutable`, Thai message naming the corrections endpoint |
| 3 | `POST .../corrections` — 75 คน, **660.00 h** (60 h of OT omitted), reason mandatory | new row `EntryKind = Correction`, `CorrectsLogId` = original; **original row byte-identical afterwards** |
| 4 | Recompute | N1 $PI = 420.00/660.00 = 0.636363\ldots \Rightarrow \mathbf{0.64}$ |
| 5 | Second correction targeting the **original** | **409** `ManpowerLogAlreadySuperseded` |
| 6 | `Retraction` of the chain tail | N1 leaves $\mathcal{M}^{eff}$ ⟹ N1 excluded with `NoActualManHours` (+ `ProgressWithoutManHours`); project $PI = (120+80)/(100+40) = 200/140 = 1.428571\ldots \Rightarrow \mathbf{1.43}$, `IsPartiallyReported = true` |
| 7 | Query `AuditLog` | zero `Update`/`Delete` rows for `EntityName = 'ManpowerEquipmentLog'`, **database-wide** |

Step 6 is the one implementations get wrong: after retraction the scope has earned hours and no
actual hours, which is **M-06a**, not a division by zero and not a PI of 0.

### M-14 — tenant and project isolation

| # | Input | Expected |
| :-- | :-- | :-- |
| a | `WorkCategoryId` belonging to another project, same tenant | **422** `WorkCategoryNotInProject`, at **write** time |
| b | `WbsNodeId` / `ActivityId` in another tenant | **404** (ADR-0002) |
| c | PI query as tenant B | tenant A's hours invisible; no leak through the totals or the coverage figure |
| d | `ActivityId` whose `WbsNodeId` ≠ the row's `WbsNodeId` | **422** — one row, one attribution |

### M-15 — cumulative and period PI differ, and both are right

Consistent with M-08. Before the week: $AMH^{cum} = 3{,}600.00$ h, A-STR at 30.00%
⟹ $EMH^{cum} = 3{,}600.00$ ⟹ $PI^{cum} = \mathbf{1.00}$.
After the week (+1,200.00 h, +8.00%): $EMH^{cum} = 12{,}000 \times 0.38 = 4{,}560.00$;
$AMH^{cum} = 4{,}800.00$

$$PI^{cum} = \frac{4{,}560.00}{4{,}800.00} = \mathbf{0.95} \qquad\text{while}\qquad PI_{\text{week}} = \mathbf{0.80}\ \text{(M-08)}$$

Assert both appear, under distinct labels (tile = "สะสม", trend point = "สัปดาห์นี้"). A screen that
shows 0.95 and 0.80 without saying which is which will be reported as a bug.

### M-16 — rounding is away-from-zero, and happens once

$EMH = 169.00$, $AMH = 200.00 \Rightarrow 0.845$ exactly.

- Expected **0.85** (`MidpointRounding.AwayFromZero`). .NET's default `Math.Round(x, 2)` is banker's
  rounding and returns **0.84** — risk **R-12**, and it would also diverge from SQL Server's `ROUND()`.
- Assert the sums are **not** rounded before the division: rounding $EMH$ and $AMH$ to whole hours
  first, or rounding each scope's PI before aggregating, both shift the second decimal (M-03: rounding
  the three scope PIs to 2 dp before weighting still gives 0.84 here, but the property must be tested
  as *ratio computed on unrounded sums*, not asserted by coincidence).

---

## 11. Reconciliation with industry practice, P6 and MS Project

### 11.1 Sources relied on — and how far each is verified

| Source | What it supports here | Confidence |
| :-- | :-- | :-- |
| **AACE RP 10S-90**, *Cost Engineering Terminology* — productivity as a relative measure of labour efficiency against an established base/norm | §5.1's definition; why an index needs a norm | Substance confident; **[ต้องยืนยัน]** exact wording before external quotation |
| **AACE RP 25R-03**, *Estimating Lost Labor Productivity in Construction Claims* — the *measured mile* method | §4.7's evidentiary justification; §9.4's future work | Confident |
| **CII Benchmarking & Metrics** — construction productivity as **work-hours ÷ installed quantity** (a **unit rate**: *lower is better*) | §11.2's warning that a second, inverse convention exists and is widely used | Substance confident; **[ต้องยืนยัน]** current CII definition before citing in a client deliverable |
| **PMI PMBOK / EIA-748** EVM — $EV$, $CPI$, and performance indices as earned ÷ actual | §5.2, §7 | Confident |
| **Standard-costing variance analysis** — labour efficiency (usage) and labour rate (price) variances | §7.2's decomposition | Confident; the algebra in M-11 is self-verifying |
| **Primavera P6** — `Budgeted Labor Units`, `Actual Labor Units`, `Earned Value Labor Units = Budgeted Labor Units × Performance % Complete`, and a labour-units CPI | §11.3 | Substance confident; **[ต้องยืนยัน]** exact column names against the target P6 version at first XER import |
| **`docs/ECM Planning Prototype.dc.html`** — the tile "Productivity เฉลี่ย 0.97 · EV ต่อ ชม.แรงงาน / แผน" | §5.2 — the ruled formula is what this tile already says | Direct, in-repo |

### 11.2 The convention conflict — this is the real open question

**Two conventions are both current in construction, and they point in opposite directions.**

| | Definition | Good is | Common in |
| :-- | :-- | :-- | :-- |
| **A — output/input (ruled)** | $\dfrac{\text{earned hours}}{\text{actual hours}}$ — also called *Performance Factor* / *Productivity Factor* | **> 1** | EVM practice, US industrial construction, P6 |
| **B — unit rate** | $\dfrac{\text{actual MH per unit}}{\text{budget MH per unit}}$ | **< 1** | CII-style benchmarking, QS/estimating, many company monthly reports |

They are reciprocals: $A = 1/B$. **CM+ ships convention A**, because it is the one that reconciles
with the $EV$/$CPI$ machinery already in the codebase, it is what the prototype tile states, and it is
the one P6 computes natively. But **if the pilot contractor's existing monthly report uses B, then a
green 1.15 in CM+ and a red 1.15 in their report are the same job** — and that is a live
misinterpretation risk on a management screen, not a theoretical one. See **Q1**.

Mitigation shipped regardless of the answer: the tile carries the formula in its tooltip
(**"ชม.แรงงานที่ได้ (earned) ÷ ชม.แรงงานที่ใช้จริง — สูงกว่า 1.00 = ดีกว่าแผน"**), and the unit rate is
displayed **alongside** PI wherever a quantity exists — never instead of it, and never called "PI".

### 11.3 Primavera P6

| CM+ | P6 | Agreement |
| :-- | :-- | :-- |
| `Activity.BudgetManHours` | `Budgeted Labor Units` (XER `TASKRSRC.target_qty`) | **Exact** — this is the import path (§5.4 source 2) |
| $AMH$ from `ManpowerEquipmentLog` | `Actual Labor Units` (`act_reg_qty + act_ot_qty`) | **Same quantity, different provenance.** P6's actual units come from timesheets against resource assignments; CM+'s come from the daily site log. They agree only if the site log is the timesheet — otherwise reconcile the totals before comparing any index |
| $EMH$ | `Earned Value Labor Units = Budgeted Labor Units × Performance % Complete` | **Exact**, *provided* the P6 EV technique is one that reads physical progress |
| $PI$ | labour-units CPI (`Earned Value Labor Units / Actual Labor Units`) | **Exact** under the same proviso |
| Physical progress (ADR-0009) | `% Complete Type = Physical` | **Aligned.** A P6 project set to **`Duration`** % complete measures elapsed time, and one set to **`Units`** measures hours expended — the latter makes $PI \equiv 1.00$ identically (§7.4, **M-12**). **Check this setting before comparing a single number** |
| Man-hours only | P6 splits `Labor` and `Nonlabor` units | **Aligned by design** — CM+'s equipment hours are the Nonlabor analogue and are kept out of $PI$ (§8) |

### 11.4 MS Project

MSP has `Work`, `Baseline Work` and `Actual Work` in hours, and `BCWP` in money, but **no
labour-units CPI field**. The equivalent is computed by hand as
$\textit{Baseline Work} \times \textit{Physical \% Complete} \div \textit{Actual Work}$, which is this
document's formula. MSP's `Work Variance = Work − Baseline Work` is a *forecast* variance and is **not**
a productivity index — do not map PI onto it.

### 11.5 Deliberate CM+ divergence

- **CM+ measures productivity from a site log, not from timesheets.** That is a choice: it captures
  subcontractor labour a payroll system never sees (`LabourType`, §4.1), at the cost of being an
  estimate where the site log is casual. `ManHoursDerived` is what makes the distinction visible.
- **CM+ has no resource-assignment model.** P6 assigns named resources to activities with units and
  spreads; CM+ records aggregate hours per category per day. Consequence: CM+ cannot produce a
  resource-levelled plan, and the planned histogram of §4.6 is a *derivation*, not an assignment
  spread. State it on the chart.

---

## 12. Open questions for the human — [ต้องยืนยัน]

The ADR-0015 / ADR-0016 / ADR-0017 / ADR-0019–0020 precedent applies: **nothing below is guessed and
no placeholder is seeded.** Where a default is stated it is defensible and shippable; where it says
"none", the field is nullable and `NULL` means *not configured*, never a value.

| # | Question | Blocking | Default until answered |
| :-- | :-- | :-- | :-- |
| **Q1** ★ | **Which PI convention does the client actually use?** §11.2 — output/input (>1 good, ruled) vs unit rate (<1 good). **This is genuinely company- and contract-specific, not settled by any standard**, and getting it backwards inverts every colour on the screen. Ask for one page of the pilot contractor's existing monthly productivity report. | **No** (A ships) | **Convention A**, $PI = EMH/AMH$, higher is better, formula in the tooltip, unit rate shown alongside where quantities exist |
| **Q2** ★ | **Does the pilot project have budgeted man-hours at all?** If the estimate is money-only — common where most trades are lump-sum subcontracts — then $BMH$ is `NULL` everywhere, **PI renders "—" on every row**, and S12-BE-02 delivers a manning histogram with no index. That is an honest outcome but it must not be a surprise on demo day. Needs one look at the estimator's build-up. | **Yes — the PI half of S12-BE-02** | Build the engine; it degrades correctly to "—" (M-05, M-06e) |
| **Q3** | **Tier 2 — add `Activity.WorkCategoryId` for per-category PI?** (§4.3) The screen is organised by หมวดงาน, so a category PI is what users will expect; without it the category column is permanently "—". One nullable column + a backfill. | no | **Tier 1**: node/project PI only; category PI = "—" with reason `ActivitiesNotCategorised` |
| **Q4** | **PI colour bands and the ±5% manning tolerance.** Defaults are read off the prototype (0.97 green; legend "ตามแผน ±5%"). Does the client have its own thresholds? | no | $\ge 0.95$ green · $0.85$–$0.95$ gold · $< 0.85$ red; manning ±5% |
| **Q5** | **Should the resource log post to $AC$?** (§3) Needs a labour/plant rate table, `Source = DerivedFromResourceLog`, reversal when the accounting figure lands (actual-cost.md §4.3) — and the rates are payroll data (Q10). Double-counting labour is named there as the most likely defect in the AC module. | no | **No.** Hours only; no money anywhere in this feature |
| **Q6** | **Immutability — accept it, and accept there is no edit path?** (§4.7) A typo costs a correction row plus a reason. The rejected alternative is a same-day same-author grace window. Related: does an explicit `BudgetManHours = 0.00` mean "no labour budgeted" (in scope, **M-06f**) or "unknown" (out of scope)? | no | **Append-only** with `IAppendOnly` + interceptor + 405; explicit `0.00` is **in scope** with an `UnbudgetedLabourHours` warning |
| **Q7** | **Subcontractor labour.** On a subcontract-heavy Thai project the main contractor sees heads on site but neither pays nor budgets their hours, so those scopes have actual hours and no $BMH$ — permanently depressing coverage. Should PI default to `LabourType = OwnDirect` only, with subcontract hours shown but excluded? | no | **All labour types in PI**, `LabourType` recorded and filterable; unbudgeted subcontract hours land in `ExcludedManHours` and are disclosed via coverage |
| **Q8** | **Duplicate daily entries** — warn-and-confirm (`allowDuplicate`) or hard block? Two crews of one trade on one node in one day is legitimate; blocking it pushes users to merge numbers by hand and destroys the detail. | no | **409 + explicit `allowDuplicate` override**, recorded on the row |
| **Q9** | **Measured-mile / disruption baseline.** Should CM+ compute an unimpacted-period baseline productivity and a loss-of-productivity figure (AACE RP 25R-03)? It is the natural next step from this data and it is claim-grade output — which means it carries the whole §2/§3 advisory-vs-entitlement problem weather-eot §2 had to solve. | no | **Out of scope.** Stoppage days are annotated on the trend only, with zero arithmetic effect |
| **Q10** | **PDPA — will per-worker data ever be recorded?** (§4.9) Counts are outside ข้อมูลส่วนบุคคล; names, ID numbers, biometric clock-ins and wage rates are not. This determines whether the whole module stays PDPA-light and IndexedDB-cacheable. | no | **Counts only.** No personal data on the row; `SubcontractorRef` is a company |

**Not open — recorded rulings of this document:** $PI = EMH/AMH$, higher is better (§5.2, §5.3); the
manning ratio is not PI and must never bind to `productivityIndex` (§5.1); planned hours come from
`Activity.BudgetManHours` and never from money ÷ an assumed rate (§5.4); `NULL` ≠ `0` throughout, with
reason codes on a result record (§5.4, ADR-0013(f)); ratio-of-sums aggregation, hour-weighted (§6);
PI never feeds any money figure (§3, §7.5); man-hours and equipment-hours are never summed (§8); the
log is append-only with a correction chain (§4.7); the planned manning figure does not live on the
immutable row (§4.6); half-open $(a,b]$ bucketing shared with $AC$ (§4.5); PI buckets must contain a
progress observation (§5.5).

---

## 13. Traceability — rule → task → fixture

| Rule | Sprint 12 task | Fixture(s) | Test artifact |
| :-- | :-- | :-- | :-- |
| $PI = EMH/AMH$; the money-form identity | **S12-BE-02** | **M-01** | `CMPlus.Application.Tests/Manpower/ProductivityIndexTests` |
| **Manning ratio ≠ PI** (the naive defect) | **S12-BE-02**, S12-FE-02 | **M-02** ★ | ditto — **build this first** |
| Ratio-of-sums aggregation across scopes | **S12-BE-02** | **M-03** ★ | `ProductivityAggregationTests` |
| Ratio-of-sums aggregation across time | **S12-BE-02** | **M-04** ★ | ditto |
| `NULL` ≠ `0`; "—" when no planned value | **S12-BE-02**, **S12-FE-02** | **M-05** ★, **M-06** | `ProductivityIndexResultTests` + `web/src/features/maneq/*.test.tsx` |
| Degenerate cases, no exception thrown | **S12-BE-02** | **M-06a–i** | `ProductivityIndexEdgeCaseTests` |
| Partially-reported day; no imputed zeros | **S12-BE-02**, S12-FE-02 | **M-07** | ditto + component test |
| Progress-aligned bucketing | **S12-BE-02** | **M-08** ★ | `ProductivityBucketingTests` |
| Half-open $(a,b]$ period boundary | **S12-BE-02** | **M-09** | ditto |
| Equipment utilisation; hours never mixed | **S12-BE-02** | **M-10** | `EquipmentMetricsTests` |
| PI vs $CPI$ reconciliation; no side effects | **S12-BE-02** | **M-11**, **M-11c** | `CMPlus.Integration.Tests/Manpower/NoSideEffectsTests` |
| Circular earning basis advisory | S12-BE-02 | **M-12** | `ProductivityIndexTests` |
| Immutability, correction chain, 405, audit invariant | **S12-BE-02**, S12-DB | **M-13** | `CMPlus.Domain.Tests/ManpowerEquipmentLogTests` + `CMPlus.Integration.Tests/Manpower/` |
| Tenant/project isolation (ADR-0002) | S12-BE-02 | **M-14** | the standard parameterised tenant-filter suite |
| Cumulative vs period labelling | S12-FE-02 | **M-15** | component test |
| Away-from-zero rounding, once, at the boundary | S12-BE-02 | **M-16** | `ProductivityIndexTests` |
| Histogram: man-hours, stacked, plan overlay, "—" with no plan, hatched partial bars, null gaps | **S12-FE-02** | M-05, M-07, M-08 | `web/src/features/maneq/*.test.tsx` |
| Manning delta must not colour over-manning green | **S12-FE-02** | M-02, M-07 | ditto |

**Rule from `docs/10.` §10 binding every consumer of this file:** an agent claiming a test passes must
show the real run output, and **a fixture that has passed may never be edited to make code pass** — if
a computed value disagrees with a fixture here, escalate to `domain-expert` for a ruling first.
