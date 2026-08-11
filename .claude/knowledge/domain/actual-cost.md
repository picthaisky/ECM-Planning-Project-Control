# Actual Cost (AC / ACWP) — canonical ruling, with test fixtures

Closes the gap surfaced by Sprint 7: `AC` is one of the three fundamental EVM inputs but **no
entity in the data model records it** (`docs/9.` §4, `docs/10.` §3, `backlog-detailed.md` — all
checked, none define a cost source). Until this ruling lands, `IActualCostReader` returns a
literal `0` and every cost-driven metric routes to `NoActualCost` (see
`backend/src/CMPlus.Infrastructure/Persistence/ActualCostReader.cs`).

This file is the single source of truth for **what AC is, where it comes from, and how it is
stored**. Formula consumers (`CV`, `CPI`, all `EAC` variants, `TCPI`) stay in
[evm-formulas.md](evm-formulas.md); money-in (certified payments, retention, advance) stays in
[payment-retention.md](payment-retention.md). One fact, one place.

Precision: money `decimal(18,2)`, percent `decimal(5,2)`, dates `DateTimeOffset`, ids `Guid`,
everything `TenantId`-scoped (ADR-0002). Rounding half-away-from-zero (`MidpointRounding.AwayFromZero`).

---

## 1. Definitions

| Term (EN) | ไทย | Meaning |
| :-- | :-- | :-- |
| **Committed cost** | ต้นทุนผูกพัน | A PO/subcontract is signed; nothing received yet. A *future* obligation. |
| **Incurred cost** | ต้นทุนที่เกิดขึ้นจริง | Goods/services **received and consumed** on work performed, whether or not invoiced. |
| **Accrued cost** | ค่าใช้จ่ายค้างจ่าย | The *estimated* portion of incurred cost not yet invoiced at cut-off. A subset of incurred. |
| **Paid cost** | ต้นทุนที่จ่ายเงินแล้ว | Cash has left the bank. Lags incurred by one credit cycle (30–60 days in Thai practice). |
| **Control account** | บัญชีควบคุมต้นทุน | AACE/EIA-748: the point in the WBS where budget, EV and AC are all measured against each other. In CM+ this is a `WBSNode`. |
| **Cost code** | รหัสต้นทุน | The accounting system's job-cost key. The join key between the ERP export and a CM+ control account. |
| **Valid time** (`IncurredDate`) | วันที่เกิดต้นทุน | The date the cost *belongs to*. Drives $AC(t)$. |
| **Transaction time** (`PostedAt`) | วันที่บันทึกเข้าระบบ | When the row was created in CM+. Drives audit / "as-known-at". |
| **Restatement** | การปรับปรุงย้อนหลัง | A live recomputation of a past data date now differing from the frozen `EvmPeriodSnapshot`. |

---

## 2. Ruling 1 — AC is **incurred cost on an accrual basis**, matched to work performed

$$AC \equiv \text{cost incurred in accomplishing the work for which } EV \text{ was measured, as of } t$$

Not committed, not paid. This is the PMBOK / EIA-748 / AACE RP 10S-90 definition, and the reason
is the **matching principle**: $EV$ counts work *done*, so $AC$ must count the cost *of that work*.
Any other basis makes $CV$ and $CPI$ measure something other than construction cost performance:

| If AC were… | What $CPI$ would actually measure | Failure mode |
| :-- | :-- | :-- |
| **Committed** | Procurement timing | $CPI$ collapses the day a large subcontract is signed, before a single day's work. Recovers later. Pure noise. |
| **Paid** | Accounts-payable speed | $AC$ lags $EV$ by one credit cycle → $CPI$ systematically **flattered**. A contractor sliding into loss looks healthy for two months. |
| **Certified/received** | Billing progress and *margin* | See §5 — the single most expensive mistake available here. |
| **Incurred (accrual)** ✅ | Construction cost performance | Correct. Requires a month-end accrual discipline (below). |

### 2.1 The accrual discipline is not optional

At any month-end, a slice of incurred cost has no invoice yet (subcontractor valuation agreed,
materials delivered, labour worked). If it is omitted, $EV$ is recognised without its cost and
$CPI$ spikes. Rule:

- The QS/cost engineer posts an **`Accrual`** entry at the cut-off date for work received but not
  invoiced (Thai practice: ตั้งค้างจ่าย ณ วันปิดงวด).
- When the invoice arrives it is posted as `Actual` **and** the accrual is reversed with an
  `AccrualReversal` entry carrying `ReversesEntryId`. Never edited, never deleted.
- Both the reversal and the invoice carry the **original `IncurredDate`**, so they land in the
  period the work belongs to — not the period the paperwork arrived.

Fixture **AC-4** quantifies this: with the accrual, March restates by ฿40,000; without it, by
฿640,000, and March's $CPI$ reads 0.96 (looks fine) instead of 0.78 (clearly in trouble).

### 2.2 Normalisation rules for the amount posted (Thai tax/contract specifics)

`Amount` is the **value of the work/goods received**, i.e.:

- **Net of recoverable input VAT** (ภาษีซื้อ 7%). Input VAT is reclaimed, not a project cost.
  *Exception:* a non-VAT-registered entity, or genuinely non-recoverable VAT — then VAT is a cost.
  See open question Q2.
- **Gross of withholding tax** (หัก ณ ที่จ่าย 3% services / 1% transport). WHT is a prepayment of
  the *payee's* income tax collected on their behalf; it does not reduce the payer's cost.
- **Gross of retention the contractor withholds from its own subcontractors**
  (เงินประกันผลงานที่หักจากผู้รับเหมาช่วง). The cost is incurred at full value; withholding is a
  cash-timing event.
- **Gross of any early-payment discount taken** only if the discount is a financing gain; if it is
  a genuine price reduction, post the net price. Default: post the invoice price.
- **Excluding head-office overhead allocation** (ค่าโสหุ้ยสำนักงานใหญ่, typically 3–7% of turnover)
  unless `BAC` also carries it — see the scope-match rule in §7.4 and open question Q4.

Fixture **AC-8** shows the three ways this goes wrong (−1.00%, −5.00%, +7.00% on the same invoice).

---

## 3. Ruling 2 — granularity: **control account (`WBSNode`) + cost category + date**, activity optional

$EV$ is per-Activity (ADR-0009: $EV(t)=\sum_i BudgetCost_i \cdot Pct_i(t)/100$). AC cannot always
match that granularity — no real contractor codes an invoice to one of 10,000 activities. AACE and
EIA-748 both resolve this at the **control account**: the level where budget, EV and AC all meet.

**Ruling:**

- `WbsNodeId` — the control account. **Nullable**; `NULL` means project-level / not yet attributed
  (typical for preliminaries and for freshly imported, unmapped rows).
- `ActivityId` — **nullable**, populated only when genuinely known (e.g. a plant-hire ticket for a
  specific pour). Enables activity-level $CPI$ where the data supports it.
- `CostCategory` — the Thai 5-หมวด job-cost structure: `Material` (ค่าวัสดุ), `Labour` (ค่าแรง),
  `Subcontract` (ค่าผู้รับเหมาช่วง), `PlantEquipment` (ค่าเครื่องจักร/เครื่องมือ), `SiteOverhead`
  (ค่าดำเนินการหน่วยงาน/โสหุ้ยสนาม), `Other`.
- `IncurredDate` — mandatory; the time dimension.

**Validity of $CPI$ by level (a hard UI rule, not a suggestion):**

| Level | Valid when | Otherwise |
| :-- | :-- | :-- |
| Project | Always | — |
| WBS node $n$ | All AC in $n$'s subtree is posted at $n$ or below | Show $CPI$ with an "unallocated cost present" badge, or "—" |
| Activity $i$ | The activity has ≥ 1 AC row with `ActivityId = i` | **"—"** |

**Never** pro-rate a parent's AC down to children by budget weight and present the result as
measured. Doing so makes activity-level $CPI$ an algebraic identity of the budget split
(it converges on the parent's $CPI$ for every child) and is worse than showing nothing.
Fixture **AC-3** shows the 0.0297 error a naive "sum of node ACs" project rollup produces.

---

## 4. Ruling 3 — where the data comes from

A real Thai contractor's cost cycle, and what each stage can give CM+:

| When | Who | What exists | Usable as AC? |
| :-- | :-- | :-- | :-- |
| Daily | โฟร์แมน / วิศวกรสนาม | Labour head-count, plant hours, delivery orders (ใบส่งของ) | Only with **rates** → an *estimate*. This is `ManpowerEquipmentLog` (S12), which today stores counts, **no money**. |
| Weekly / monthly | QS / วิศวกรต้นทุน | Subcontractor valuations (ใบตรวจรับงาน), accruals | Yes — the accrual source. Judgement, not an accounting record. |
| Monthly + 5–15 days | บัญชี / ERP | Job-cost ledger closed by cost code (Express, FORMULA, WINSpeed, SAP B1, in-house ERP) | **Yes — this is the authoritative AC.** |

**Ruling — CM+ needs both an import path and a manual UI, in this priority:**

1. **Excel/CSV import of the monthly job-cost ledger (primary).** The data already exists in the
   contractor's accounting system; making them re-key it guarantees the module is abandoned within
   two months. Mapped by `CostCode` → `WbsNodeId` + `CostCategory`. Reuses the EPPlus infrastructure
   and `FileImportJob` from Sprint 3. A later ERP/API integration is the same seam.
2. **Manual entry UI (required, not a fallback).** Because (a) small contractors have no exportable
   ledger, (b) **accruals are a project-control judgement, not an accounting record** and will never
   come out of the ERP, (c) every import leaves unmapped rows needing a human, (d) corrections must
   be postable.
3. **Derived from `ManpowerEquipmentLog` + resource rates (optional, S12 at the earliest).** Would
   need a rate table that does not exist. If ever built: entries must carry
   `Source = DerivedFromResourceLog`, be visually distinguished as *estimated*, and be **superseded
   (reversed) when the accounting figure lands** — never summed alongside it. Double-counting labour
   is the most likely defect in this whole module.

---

## 5. Ruling 4 — AC and certified payments are **two separate ledgers**. Confirmed.

`docs/specs/master-plan/domain-decisions.md` §3.1 ("`Paid` amounts are receipts and must never be
written into $AC$") and [approval-workflow.md](approval-workflow.md) §3 are **correct**. Agreed and
reinforced — the margin argument below is the decisive one and was not previously stated.

| | Certified payment (`PaymentCertificate` + `ProjectFinanceLedger`) | Actual cost (`ActualCostEntry`) |
| :-- | :-- | :-- |
| Direction | Money **in** (revenue / receivable) | Money **out** (expenditure) |
| Priced at | **Contract rates** — includes the contractor's margin, prelims recovery, risk allowance | **Resource cost** — no margin |
| Trigger | Certification by the employer/engineer | Receipt of goods/services |
| Clock | work → certify (weeks) → pay (30–60 d) → retention release (to end of DLP) | at incurrence |
| Deductions | Retention, advance recovery, VAT, WHT | None (see §2.2) |
| Mutability | Immutable from `PendingApproval`; corrections are new certificates | Append-only; corrections are reversal + repost |

**Why substituting one for the other is catastrophic, not merely imprecise:** certified value is
$EV$ *marked up to contract price*. Setting $AC :=$ certified value therefore makes
$CV = EV - EV(1+m) \approx -m \cdot EV$ — a number that tracks the markup and the certification lag,
never the cost. $CPI$ hovers near a constant regardless of how badly the job is performing.
Fixture **AC-7**: the same project reads $CPI = 1.0504$ (green, $CV = +฿120{,}000$) on cash
received, $0.8929$ on gross certified, and the truth is $0.7716$ ($CV = -฿740{,}000$).
**The cash-received substitution flips the sign of $CV$.**

**The one legitimate join** is the Cash Flow screen: receipts (from the certificate/finance ledger)
and AC (from the cost ledger) plotted as two series, differenced into a funding position — exactly
the prototype's `รับเงินสะสม 238.4 MB` vs `จ่ายจริงสะสม (AC) 253.1 MB` → `−14.7 MB`. No other
arithmetic between the two ledgers is valid.

> ⚠️ **UI copy defect for `frontend-developer`/`po-analyst` (S8-FE-02).** The prototype's tile is
> labelled `จ่ายจริงสะสม (AC)` — "cumulative actually **paid**". Under this ruling AC is cost
> *incurred*, not cash paid, so the tile should read **`ต้นทุนเกิดขึ้นจริงสะสม (AC)`** and
> `Net Cash Position` is strictly `รับเงินสะสม − ต้นทุนสะสม`, i.e. a *funding/margin* position, not
> a bank balance. Either rename the tile, or add the optional `PaidDate` field in §9 which makes a
> true cash-out series derivable later with no new table.

---

## 6. Ruling 5 — AC is **bitemporal and append-only**, and interacts with `EvmPeriodSnapshot` exactly like progress does

Yes, AC needs the same dated-history treatment as `ActivityProgressLog` under ADR-0009 — and needs
it *more*, because cost data is late by nature (an invoice for March work is posted in April).

1. **Two dates, both mandatory.** $AC(t)$ is computed on **`IncurredDate` (valid time)**, never on
   `PostedAt` (transaction time). `PostedAt` exists for audit and for "what did we know on date X".
2. **A backdated cost entry *does* change a past period's live $CPI$ — and that is correct.**
   Identical to ADR-0009's ruling for backdated progress. The cost belonged to that period; the
   period was reported before the cost was known.
3. **Closed periods are frozen in `EvmPeriodSnapshot`** (already built, S7, and it already stores
   `Ac`). Printed reports, PDFs and anything cited contractually reference a snapshot id. A live
   recomputation of the same data date may legitimately differ.
4. **Restatement must be surfaced, never silent.** Recommended: when a snapshot exists for the
   requested data date, the EVM response carries
   $\Delta AC = AC_{\text{live}}(t) - AC_{\text{snapshot}}$ and a `periodRestated` flag when
   $\Delta AC \neq 0$. Two numbers disagreeing across two screens with no explanation destroys
   user trust in the whole dashboard.
5. **Append-only, no `UPDATE`/`DELETE` path** — same pattern as `ProjectFinanceLedger`,
   `ApprovalAction` and `DailyWeatherLog`. `Amount` is **signed** so $AC = \texttt{SUM(Amount)}$ is a
   pure aggregate. Corrections are a reversal row plus a re-post, linked by `ReversesEntryId`.
6. **Posting into a closed period is allowed** (soft, not hard). Blocking it would push real
   invoices into the wrong period, which is worse than restating. Requires a `Note`, raises the
   restatement flag, writes an `AuditLog` row. `EvmPeriodSnapshot` itself is never touched.
7. **No denormalised cache** (`Project.ActualCostToDate` or similar). Unlike
   `Activity.ProgressPercentage`, there is no read-path justification — row volume is ~300/month
   (≈6,000 for a 20-month project, versus `ActivityProgressLog`'s ~350k) so the indexed `SUM` is
   sub-millisecond, and a cache would break the bitemporal property.

---

## 7. Formulas

Let $E_P$ be the set of `ActualCostEntry` rows for project $P$ in the current tenant.

### 7.1 Actual Cost at data date

$$AC(t) \;=\; \sum_{e \in E_P \,:\, \text{IncurredDate}_e \,\le\, t} \text{Amount}_e$$

All `EntryType`s are included (`Actual`, `Accrual`, `AccrualReversal`, `Adjustment`) — the sign on
`Amount` already carries the semantics. Unit THB, `decimal(18,2)`. The sum is exact (every addend is
already 2dp); no rounding step is required or permitted mid-sum.

**Date convention (must match ADR-0009 exactly).** `IncurredDate` is stored normalised to
`00:00:00` in the project's timezone (Asia/Bangkok, +07:00) and compared **inclusive `<=`**, the
same shape as `ActivityProgressLog.PeriodEndDate ≤ t`. If EV and AC ever use different boundary
conventions, every period-end $CPI$ is wrong by one day of cost.

### 7.2 Control-account and activity AC

$$AC_n(t) = \sum_{\substack{e \,:\, \text{WbsNodeId}_e \in subtree(n) \\ \text{IncurredDate}_e \le t}} \text{Amount}_e
\qquad
AC_i(t) = \sum_{\substack{e \,:\, \text{ActivityId}_e = i \\ \text{IncurredDate}_e \le t}} \text{Amount}_e$$

$CPI_n = EV_n / AC_n$ and $CPI_i = EV_i / AC_i$, each `null` when its denominator is 0 — same rule
as [evm-formulas.md](evm-formulas.md). Note $\sum_n AC_n(t) \le AC(t)$; the difference is
unallocated cost and must **not** be redistributed.

### 7.3 Attribution coverage (data-quality metric)

$$Cov(t) \;=\; 100 \times \frac{\sum_{e\,:\,\text{WbsNodeId}_e \neq \text{NULL}} \text{Amount}_e}{AC(t)}
\qquad \text{null if } AC(t) = 0$$

`decimal(5,2)`. Surfaced on the EVM/Cash Flow screens. Recommended threshold: warn below 90.00.

### 7.4 Scope-match invariant (the silent-killer rule)

$$\text{scope}(AC) \equiv \text{scope}(BAC)$$

Every cost category posted to AC must have a budgeted counterpart inside
$BAC = \sum_i BudgetCost_i$. If site overhead is in AC but preliminaries are not in the WBS, $CPI$
is depressed permanently and by construction — a perfectly-run project still reads over budget.
Fixture **AC-9** puts the number on it: $CPI = 0.8537$ at completion with flawless cost control.
Fix by adding a budgeted Preliminaries WBS branch earned as Level of Effort (progress = elapsed
duration %), *not* by hiding the cost.

### 7.5 Period slice (Cash Flow bars)

$$AC_{(a,\,b]} \;=\; \sum_{a \,<\, \text{IncurredDate}_e \,\le\, b} \text{Amount}_e$$

**Half-open, lower-exclusive.** Adjacent periods must not double-count the boundary day; the
cumulative series must satisfy $\sum_k AC_{(t_{k-1},\,t_k]} = AC(t_K)$ exactly. Fixture **AC-10**.

### 7.6 Edge cases (deterministic; nothing throws)

| Condition | Behaviour |
| :-- | :-- |
| No entries with `IncurredDate` ≤ $t$ | $AC = 0.00$ (**not** null). $CPI$ null, reason `NoActualCost`. Matches [evm-formulas.md](evm-formulas.md) fixture E. |
| Entries exist but net to exactly 0.00 (e.g. accrual fully reversed, invoice not yet posted) | $AC = 0.00$; $CPI$ still null, reason **unchanged** `NoActualCost` (no breaking change to the S7 contract), **but** the warning copy must differ. This is why the reader should return an **entry count** alongside the amount: "ต้นทุนสุทธิเป็นศูนย์จาก 2 รายการ" ≠ "ยังไม่มีการบันทึกต้นทุน". |
| $AC(t) < 0$ (over-reversal, credit note exceeding costs) | Compute and return as-is; **do not clamp**. $CPI$ would be negative and meaningless → return null with a recommended new reason `NegativeActualCost` + data-quality warning. |
| `IncurredDate` > project data date at posting time | Allowed (a future-dated correction is legitimate) but warn — it is far more often a typo. It is excluded from $AC(t)$ by definition until $t$ catches up. |
| `Amount = 0.00` | **Reject at validation.** A zero cost entry, including a zero reversal, is noise. |
| `Amount` supplied with > 2 dp | Manual entry: reject. Import: round half-away-from-zero per row and record the adjustment on the `FileImportJob` log. |
| Cost posted to a WBS node in another project/tenant | Reject (ADR-0002). Release-blocking if it ever passes. |

---

## 8. Worked examples (transcribe verbatim into xUnit theories)

**Common setup — project `PJ-DEMO`**, timezone +07:00, data date $t_1$ = `2026-03-31`.
WBS: `W-01` Structure ($BudgetCost$ 4,000,000.00), `W-02` Architectural (3,000,000.00).
$BAC = 7{,}000{,}000.00$. Progress at $t_1$: W-01 40.00%, W-02 30.00% →
$EV = 1{,}600{,}000.00 + 900{,}000.00 = 2{,}500{,}000.00$. $PV = 3{,}125{,}000.00$ → $SPI = 0.80$.

Ledger (in `PostedAt` order):

| # | IncurredDate | PostedAt | Node | Category | EntryType | Amount |
| :-- | :-- | :-- | :-- | :-- | :-- | --: |
| 1 | 2026-01-31 | 2026-02-05 | W-01 | Subcontract | Actual | 1,200,000.00 |
| 2 | 2026-02-28 | 2026-03-04 | W-01 | Material | Actual | 850,000.00 |
| 3 | 2026-02-28 | 2026-03-04 | W-02 | Labour | Actual | 430,000.00 |
| 4 | 2026-03-31 | 2026-04-02 | *(null)* | SiteOverhead | Actual | 120,000.00 |
| 5 | 2026-03-31 | 2026-04-08 | W-02 | Subcontract | **Accrual** | 600,000.00 |
| 6 | 2026-03-31 | 2026-04-20 | W-02 | Subcontract | **AccrualReversal** (reverses #5) | −600,000.00 |
| 7 | 2026-03-31 | 2026-04-20 | W-02 | Subcontract | Actual (INV-2026-0417) | 640,000.00 |

---

**AC-1 — $AC(t)$ is driven by `IncurredDate`, not `PostedAt`.**
Same data date $t_1$, queried at three different moments:

| Queried on | Rows visible | $AC(t_1)$ | $CPI$ |
| :-- | :-- | --: | --: |
| 2026-04-05 | 1–4 | **2,600,000.00** | 0.9615 |
| 2026-04-10 | 1–5 | **3,200,000.00** | 0.7813 |
| 2026-04-25 | 1–7 | **3,240,000.00** | 0.7716 |

Entries 4–7 were *posted* in April but *incurred* in March — all count. Assert that a query with
`asOf = 2026-04-30` returns the same 3,240,000.00 (no April-incurred cost exists), proving the
reversal does not leak forward.

**AC-2 — accrual reversal nets correctly, no double count.**
$AC(t_1)$ after #5 = 3,200,000.00. After #6 and #7 = $3{,}200{,}000 - 600{,}000 + 640{,}000 =$
**3,240,000.00**. The subcontract cost appears **once**, at its true value 640,000.00, in March.
A naive implementation that treats the invoice as new cost without the reversal reports
3,840,000.00 — a ฿600,000 (18.5%) overstatement.

**AC-3 — three levels of $CPI$, and the number a naive rollup gets wrong.**
At $t_1$, final ledger state:

| Scope | $EV$ | $AC$ | $CPI$ |
| :-- | --: | --: | --: |
| W-01 | 1,600,000.00 | 2,050,000.00 | 0.7805 |
| W-02 | 900,000.00 | 1,070,000.00 | 0.8411 |
| Unallocated | — | 120,000.00 | n/a |
| **Project** | **2,500,000.00** | **3,240,000.00** | **0.7716** |

Coverage $Cov(t_1) = 100 \times 3{,}120{,}000 / 3{,}240{,}000 =$ **96.30%**.
A rollup that sums node ACs and forgets unallocated cost gets
$2{,}500{,}000 / 3{,}120{,}000 = 0.8013$ — wrong by 0.0297, and always optimistic.

**AC-4 — restatement against a frozen snapshot (the ADR-0009 interaction).**
March closed on 2026-04-10 → `EvmPeriodSnapshot` { DataDate 2026-03-31, BAC 7,000,000.00,
PV 3,125,000.00, EV 2,500,000.00, **AC 3,200,000.00**, CpiBased, PF 1.280000,
EAC 8,960,000.00, ETC 5,760,000.00, VAC −1,960,000.00 }.
After #6/#7 land on 2026-04-20, the **live** recomputation of the same data date gives
AC 3,240,000.00, EAC 9,072,000.00, ETC 5,832,000.00, VAC −2,072,000.00.

- $\Delta AC = +40{,}000.00$; $\Delta EAC = +112{,}000.00$.
- Invariant worth asserting: $\Delta EAC = \Delta AC \times BAC / EV = 40{,}000 \times 2.8 = 112{,}000$ ✓.
- The snapshot is **not** edited. The dashboard flags `periodRestated` with the delta.
- **Counterfactual — no accrual posted:** the snapshot would have carried AC 2,600,000.00,
  $CPI$ **0.9615**, EAC 7,280,000.00. Restatement would then be $\Delta AC = +640{,}000.00$,
  $\Delta EAC = +1{,}792{,}000.00$, and March would have been reported as *near-healthy* when it was
  in fact at $CPI$ 0.7716. This fixture is the numeric justification for §2.1.

**AC-5 — edge: no entries.** $AC(t) = 0.00$, entry count 0 → $CPI$ null, `NoActualCost`.
Consistent with evm-formulas.md fixture E.

**AC-6 — edge: entries net to zero.** Accrual +250,000.00 and its reversal −250,000.00, both
`IncurredDate` ≤ $t$, invoice not yet received. $AC(t) =$ **0.00**, entry count **2** →
$CPI$ null, reason `NoActualCost`, but the warning must say "net zero from 2 entries", not
"no cost recorded". Asserts the reader returns *(amount, count)*, not just an amount.

**AC-7 — the payment-vs-cost trap (the expensive mistake).**
Same $t_1$, $EV = 2{,}500{,}000.00$. Certified to date: $G^{cum} = 2{,}800{,}000.00$,
retention held 140,000.00 (5%), advance recovered 280,000.00 (10%), **cash received
2,380,000.00**. True $AC = 3{,}240{,}000.00$.

| AC source used | $CPI$ | $CV$ | Verdict shown |
| :-- | --: | --: | :-- |
| Cash received (2,380,000.00) ❌ | **1.0504** | **+120,000.00** | green — "under budget" |
| Gross certified (2,800,000.00) ❌ | 0.8929 | −300,000.00 | amber |
| **Incurred cost (3,240,000.00)** ✅ | **0.7716** | **−740,000.00** | red — losing money |

The cash-received substitution **flips the sign of $CV$**. Legitimate joint use of the two ledgers:
Net position $= 2{,}380{,}000.00 - 3{,}240{,}000.00 = -860{,}000.00$ (the contractor is funding
฿860k of working capital).

**AC-8 — VAT / WHT / subcontractor-retention normalisation.**
Subcontract invoice: work value 1,000,000.00; VAT 7% = 70,000.00; invoice total 1,070,000.00.
Main contractor withholds sub-retention 5% = 50,000.00 and WHT 3% = 30,000.00 →
cash paid 990,000.00.

| Posted amount | Error | % |
| :-- | --: | --: |
| **1,000,000.00** ✅ (work value) | 0.00 | 0.00% |
| 990,000.00 (cash paid) | −10,000.00 | −1.00% |
| 950,000.00 (net of sub-retention) | −50,000.00 | −5.00% |
| 1,070,000.00 (invoice incl. VAT) | +70,000.00 | +7.00% |

**AC-9 — scope mismatch: unbudgeted preliminaries.**
Site overhead ฿120,000/month × 10 months = ฿1,200,000, no matching budget.
At completion with *flawless* cost control on every physical activity:
$EV = 7{,}000{,}000.00$, $AC = 8{,}200{,}000.00$ → $CPI =$ **0.8537**, $CV = -1{,}200{,}000.00$.
With a budgeted Preliminaries WBS branch (BAC → 8,200,000.00, earned Level-of-Effort):
$EV = AC = 8{,}200{,}000.00$ → $CPI =$ **1.0000**, $CV = 0.00$.
Mid-project check at month 3: $EV_{prelim} = 1{,}200{,}000 \times 3/10 = 360{,}000.00 = AC_{prelim}$ ✓.

**AC-10 — period slices for the Cash Flow bar chart (half-open intervals).**
Monthly boundaries at month-end +07:00:

| Period | Entries by `IncurredDate` | Period AC | Cumulative |
| :-- | :-- | --: | --: |
| Jan (≤ 01-31) | #1 | 1,200,000.00 | 1,200,000.00 |
| Feb (01-31, 02-28] | #2, #3 | 1,280,000.00 | 2,480,000.00 |
| Mar (02-28, 03-31] | #4, #5, #6, #7 | 760,000.00 | 3,240,000.00 |
| Apr (03-31, 04-30] | — | **0.00** | 3,240,000.00 |

April is 0.00 despite four rows being *posted* in April. Assert
$\sum_k AC_{period,k} = AC(t_1) = 3{,}240{,}000.00$ exactly.

---

## 9. Recommended entity shape — *a recommendation for `system-architect`, not an accepted ADR*

Name: **`ActualCostEntry`**. Deliberately *not* `ProjectCostLedger` — too close to
`ProjectFinanceLedger`, and the whole point of §5 is that a future developer must never confuse the
two. `ActualCostEntry` maps unambiguously onto the EVM term.

```
ActualCostEntry                       -- append-only; no UPDATE/DELETE path (mirrors ProjectFinanceLedger)
  Id                 Guid            PK
  TenantId           Guid            NOT NULL   -- ADR-0002; leading column of every index
  ProjectId          Guid            NOT NULL
  WbsNodeId          Guid            NULL       -- control account; NULL = project-level / unattributed
  ActivityId         Guid            NULL       -- only when genuinely known; enables activity CPI
  CostCategory       enum            NOT NULL   -- Material | Labour | Subcontract | PlantEquipment | SiteOverhead | Other
  EntryType          enum            NOT NULL   -- Actual | Accrual | AccrualReversal | Adjustment
  Source             enum            NOT NULL   -- ManualEntry | ExcelImport | ErpIntegration | DerivedFromResourceLog
  Amount             decimal(18,2)   NOT NULL   -- signed THB; net of recoverable VAT, gross of WHT & sub-retention (§2.2); != 0
  IncurredDate       DateTimeOffset  NOT NULL   -- valid time; drives AC(t); normalised to 00:00 project tz
  PostedAt           DateTimeOffset  NOT NULL   -- transaction time
  PostedByUserId     Guid            NOT NULL
  ReversesEntryId    Guid            NULL       -- self-FK; accrual reversal / correction pairing
  DocumentReference  nvarchar(64)    NULL       -- invoice / PO / DO / payroll batch no.
  CostCode           nvarchar(32)    NULL       -- source system's job-cost code (import mapping key)
  VendorName         nvarchar(200)   NULL
  Note               nvarchar(500)   NULL       -- mandatory when posting into a closed period (§6.6)
  FileImportJobId    Guid            NULL       -- traceability for imported rows (FileImportJob exists, S3)
```

Optional / deferrable without a schema break later, but cheap now:
`PaidDate DateTimeOffset NULL` (enables a genuine cash-out series and fixes the Net Cash Position
label issue in §5); `Quantity decimal(18,4)` + `UnitOfMeasure nvarchar(16)` (unit-rate analysis,
feeds `BottomUpEtc`).

Supporting table for the import path (Sprint 9, optional if the template carries an explicit WBS
code column): `CostCodeMapping(Id, TenantId, ProjectId, CostCode, WbsNodeId, CostCategory)`.

**Indexes** (leading `TenantId`, per ADR-0002 and `docs/db-conventions.md`):

- `(TenantId, ProjectId, IncurredDate) INCLUDE (Amount)` — the $AC(t)$ seek. Primary.
- `(TenantId, WbsNodeId, IncurredDate) INCLUDE (Amount)` — node $CPI$.
- `(TenantId, ActivityId, IncurredDate) WHERE ActivityId IS NOT NULL` — activity $CPI$; filtered
  because most rows are NULL here.
- `(TenantId, ProjectId, CostCategory, IncurredDate) INCLUDE (Amount)` — category breakdown chart.

**Volume:** ~300 rows/month → ≈6,000 rows for a 20-month project. Two orders of magnitude below
`ActivityProgressLog` (risk R-11 does not apply). Per-entry granularity is affordable; no
pre-aggregated period table is needed.

**Reader interface change (small, and worth making now).** `IActualCostReader` should return the
entry count alongside the amount so §7.6's two zero cases can be worded differently:

```csharp
public sealed record ActualCostResult(decimal Amount, int EntryCount);
Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken ct = default);
```

**No new `Project` columns.** AC is fully derived from the ledger.

---

## 10. Business rules

1. **Append-only.** No update, no delete, at any layer. Corrections = `AccrualReversal`/`Adjustment`
   + re-post, linked by `ReversesEntryId`. Same family as `ProjectFinanceLedger`, `ApprovalAction`,
   `DailyWeatherLog`.
2. **Audit on every post** (project convention: every mutating domain operation writes `AuditLog`).
3. **No approval workflow.** Cost entries are accounting facts, not decisions — unlike VOs and
   payment certificates. The control point is the **period close** (`CloseEvmPeriod`), which already
   exists and already requires authority.
4. **RBAC — cost data is commercially sensitive; it needs its own permission.**
   Recommended: `QS`, `PM`, `ProjectDirector`, `Admin` may post; `Executive` read-only;
   **`Site` must not read or post cost.** Thai contractor norm — site staff do not see margin.
   ⚠️ `security-auditor`: reusing the generic project-read permission for these endpoints (and for
   the Cash Flow screen) leaks margin data to site users. Flag this before the endpoints ship.
5. **Backdated posting into a closed period** — allowed, requires `Note`, sets `periodRestated`,
   audit-logged, snapshot untouched (§6.6).
6. **Scope match** — AC categories must have budgeted counterparts (§7.4). Warn when
   $Cov(t) < 90.00$; surface a category-vs-budget reconciliation panel.
7. **VO interaction** — an approved VO raises `BAC`/`ContractValue`; cost of executing VO work posts
   to this ledger like any other cost, against the VO's new WBS nodes. No special path.
   S-Curve rebaselines from `ApprovedAt`; historical AC points are never rewritten.
8. **Referential integrity** — a `WBSNode` or `Activity` with AC posted against it cannot be
   deleted (FK restrict). Rebuild via VO/rebaseline, not deletion.
9. **Currency** — THB only in v1. Imported materials (steel, façade, lifts) are often USD/EUR;
   convert at the posting-date rate and record the rate in `Note` until dedicated fields exist.
   See open question Q5.

---

## 11. Reconciliation notes

**Primavera P6.** P6 holds actual cost on **resource assignments** (`TASKRSRC.act_reg_cost`,
`act_ot_cost`) and on **expenses** (`PROJCOST.act_cost`); activity/WBS actual cost is a rollup P6
computes, not a stored XER column. CM+'s XER parser already reads `TASKRSRC` (for `target_cost` →
`BudgetCost`), so reading the actual-cost columns is nearly free —
**but it must be opt-in, never a default.** Most Thai P6 users never maintain resource actuals, so
`act_reg_cost` is typically 0 or a mirror of the plan. Importing it silently would replace today's
honest zero with a *fabricated* number, which is strictly worse. Gate it behind an explicit user
confirmation that P6 actuals are genuinely maintained, and tag imported rows
`Source = ErpIntegration` (or a dedicated P6 source) so they can be reversed wholesale.
P6's "Store Period Performance" is P6's own frozen-period mechanism, directly analogous to
`EvmPeriodSnapshot`.

**MS Project.** MSP's `Actual Cost` is normally *derived*: `Actual Work × Standard Rate` plus fixed
cost spread by the task's accrual method (Start / Prorated / End). Unless
*"Actual costs are always calculated by Project"* is unchecked, MSP recalculates it continuously.
It is a schedule-derived estimate, not an accounting record — same opt-in rule as P6.

**Time-phasing will differ by design.** P6/MSP spread actual cost across an activity's duration;
CM+ posts it at `IncurredDate`. Period-by-period AC series will therefore differ even when
cumulative totals agree. **Reconcile only on cumulative totals at a data date**, never bar-by-bar.

**The accounting system is the system of record for cost; CM+ is a reporting consumer.** Where
CM+'s cumulative AC disagrees with the job's trial balance, accounting wins and CM+ is reconciled
by posting an `Adjustment` entry with a `Note` — never by editing history.

**Against `docs/4.` §3.** That doc defines ACWP as "ค่าใช้จ่ายที่เกิดขึ้นจริง ณ วันที่ประเมิน
**จากตารางงานที่เบิกจ่ายจริง**". The first clause is right (incurred); the trailing clause reads as
"from the disbursement/claim table" and is the likely origin of the payment/cost conflation. This
ruling **supersedes** that clause: AC comes from the cost ledger, never from `PaymentCertificate`.

---

## 12. Sprint placement

**Ruling: the minimum AC slice lands *inside Sprint 8, as a prerequisite to* S8-BE-01/S8-FE-02 —
not after Sprint 8, and not in Sprint 9 or 12.**

Minimum viable slice (call it S8-DB-00 / S8-BE-00 / S8-FE-00):

1. `ActualCostEntry` entity + migration + the four indexes (`database-engineer`).
2. Real `IActualCostReader` — one indexed `SUM` — replacing `ActualCostReader`'s literal `0`, and
   deleting that class's placeholder remarks in the same commit.
3. `POST` / `GET /api/v1/projects/{id}/actual-costs` — tenant-scoped, append-only, FluentValidation
   (§7.6), audit-logged, RBAC per §10.4.
4. A minimal QS entry UI: table + "add entry" drawer + reversal action. Can live on the Cash Flow
   screen in v1 rather than earning its own nav item (the prototype has no cost-entry screen —
   confirm placement with `po-analyst`).

Explicitly deferred: Excel import of the job-cost ledger + `CostCodeMapping` → **Sprint 9** (rides
with the QS persona and reuses the Sprint 3 EPPlus/`FileImportJob` infrastructure);
`ManpowerEquipmentLog`-derived estimates → **Sprint 12 at the earliest**, and only as labelled
estimates (§4.3); ERP/API integration → post-launch.

**Is Sprint 8 viable without this? No — not as specified.**

- S8-FE-02's own DoD requires separating `รับเงินสะสม` from `จ่ายจริงสะสม (AC)`. With AC pinned at
  0, the AC series is a flat line on the axis and the Net Cash Position tile equals receipts.
- S8-BE-02 / S8-FE-01's KPI tiles include $CV$, $CPI$, $EAC$, $ETC$, $VAC$ — 5 of the 12 EVM tiles
  plus the dashboard's headline health status — all rendering "—" permanently.
- Sprint 8's acceptance criterion in `docs/9.` §7 is "ตรงกับ layout ใน prototype"; the prototype
  shows 253.1 MB and −14.7 MB. It cannot be met.
- Building the screens against a permanent null and then rebuilding them means the Executive
  Dashboard is written twice, and a UAT-visible regression ships in between.

**Cost of doing it:** one table, one `SUM`, two endpoints, one modest screen — smaller than S8-BE-03
(Executive Summary PDF, which additionally carries an unresolved QuestPDF licence question).
**If Sprint 8 capacity is the binding constraint, the clean trade is to move S8-BE-03/S8-FE-03
(PDF export) to Sprint 9 and swap this slice in** — the PDF has no downstream dependency, whereas
every Sprint 8 number does.

**Process note:** `docs/specs/master-plan/design.md` §5 freezes the schema after Sprint 2 —
"changes need an ADR". This therefore requires a new ADR from `system-architect` (next free number:
**ADR-0013** at time of writing) before the migration is written. That ADR, not this file, is the
binding decision.

---

## 13. Open questions — [ต้องยืนยัน]

| # | Question | Why it matters | Default until answered |
| :-- | :-- | :-- | :-- |
| ~~**Q1**~~ | ~~Is the CM+ tenant the **contractor** or the **employer/PMC**?~~ | — | **RESOLVED 2026-08-09 (human): contractor-side**, confirming the default below. §5 stands as written; certified payments are *not* AC. The ledger shape works for both perspectives — only the *source* of entries changes — so if an employer/PMC tenant ever needs supporting, a later `Project.Perspective` flag adds it without a migration. Original evidence for the default: the prototype's `รับเงินสะสม` (receipts) vs `จ่ายจริงสะสม`, retention *withheld from* the tenant, advance *received by* the tenant. |
| **Q2** | Is every tenant VAT-registered with recoverable input VAT? | Changes `Amount` by 7% (§2.2). | Yes — post net of VAT. |
| **Q3** | Does the pilot contractor have an exportable job-cost ledger, and from which system/format? | Decides whether Sprint 9's import is Excel-only or needs an API adapter; decides the `CostCode` mapping design. | Excel/CSV export by cost code. Ask the client's accounting team **before Sprint 8 close**. |
| **Q4** | Should head-office overhead allocation (ค่าโสหุ้ยสำนักงานใหญ่, 3–7% of turnover) sit inside AC? | Only valid if `BAC` carries it too (§7.4); otherwise it depresses $CPI$ permanently. | **Exclude.** Label CM+'s EVM as site-cost basis. |
| **Q5** | Multi-currency purchases (imported steel/façade/lifts)? | v1 has no FX fields. | THB only; record the rate used in `Note`; add `SourceCurrency`/`ExchangeRate` post-v1. |
| **Q6** | Should `NegativeActualCost` be added to the `EacNullReason` enum (§7.6)? | Additive to the S7 API contract; frontend must handle a new reason string. | Yes, recommended — but it is `system-architect`'s call since it touches the frozen `EvmResponse` shape. |
