# EVM Formulas — Canonical Reference (with test fixtures)

Source: docs/4. §3 + PMI standard practice. Precision: money `decimal(18,2)`,
percent `decimal(5,2)`; keep full precision in intermediates, round at display.

## Core metrics at data date $t$

| Metric | Formula | Meaning |
| --- | --- | --- |
| $PV$ (BCWS) | $\sum_{i \in scheduled(t)} BudgetCost_i \cdot plannedPct_i(t)$ | มูลค่าตามแผน |
| $EV$ (BCWP) | $\sum_i BudgetCost_i \cdot \frac{ProgressPct_i}{100}$ | มูลค่างานที่ทำได้จริง |
| $AC$ (ACWP) | $\sum_{e\,:\,IncurredDate_e \le t} Amount_e$ | ต้นทุน**ที่เกิดขึ้นจริง** (incurred, accrual basis) — **not** paid, **not** committed, **never** certified payments. Full ruling + source of the data: [actual-cost.md](actual-cost.md) |
| $SV$ | $EV - PV$ | $\ge 0$ = on/ahead of schedule |
| $CV$ | $EV - AC$ | $\ge 0$ = under budget |
| $SPI$ | $EV / PV$ | undefined if $PV = 0$ → return null |
| $CPI$ | $EV / AC$ | undefined if $AC = 0$ → return null |
| $EAC$ (default) | $BAC / CPI$ | current cost performance continues |
| $EAC$ (atypical) | $AC + (BAC - EV)$ | past variance won't recur |
| $EAC$ (combined) | $AC + \frac{BAC - EV}{CPI \times SPI}$ | schedule pressure affects cost |
| $ETC$ | $EAC - AC$ | remaining cost to finish |
| $VAC$ | $BAC - EAC$ | $< 0$ = forecast overrun |
| $TCPI$ | $\frac{BAC - EV}{BAC - AC}$ | efficiency needed to finish on budget |

The EAC variant used must be stated per feature spec; dashboard default = $BAC / CPI$.
**EAC is user-selectable** — see [§ EAC Variants (selectable)](#eac-variants-selectable)
for the unified PF form, all variants, edge-case rules and fixtures.

$AC$'s definition, granularity, data source, bitemporal storage rules and its (non-)relationship to
certified payments are ruled on in **[actual-cost.md](actual-cost.md)** — read it before touching any
$CV$/$CPI$/$EAC$ code path. Two rules from there that bind this file:
$AC(t)$ is summed on the entry's **`IncurredDate`**, using the *same* inclusive `<= t` boundary
convention as `ActivityProgressLog.PeriodEndDate` (ADR-0009), so $EV$ and $AC$ never straddle a
period boundary differently; and a certified/received payment is **never** $AC$.

## Progress rollup (WBS tree)

- Parent progress: $Pct_{parent} = \frac{\sum_c Pct_c \cdot W_c}{\sum_c W_c}$ using child weights $W_c$.
- Project progress uses `WBSNode.WeightPercentage` (budget-proportional).
- Weights at a level should sum to 100; validate on save, warn (not block) during editing.

## Worked examples (use verbatim as unit-test fixtures)

**Fixture A (typical, behind schedule & over budget):**
BAC = 1,000,000.00; at data date: PV = 400,000.00; EV = 300,000.00; AC = 350,000.00
→ SV = −100,000.00; CV = −50,000.00; SPI = 0.75; CPI = 0.857142857…
→ EAC(default) = 1,000,000 / (300,000/350,000) = **1,166,666.67**
→ ETC = 816,666.67; VAC = −166,666.67; TCPI = 700,000/650,000 = 1.0769…

**Fixture B (edge: not started):**
PV = 0; EV = 0; AC = 0 → SV = 0; CV = 0; SPI = null; CPI = null; EAC = null (report as "—").

**Fixture C (edge: zero-budget activity):**
BudgetCost = 0, Progress = 50% → EV contribution = 0.00 (no division, no NaN).

## EAC Variants (selectable)

Confirmed by the human 2026-07-27 (closes docs/9. §11 item 2): EAC is **user-selectable**,
not one hard-coded formula. This section is the canonical definition of every supported variant.

### Unified Performance-Factor form

Every PF-based variant is the *same* formula with a different **Performance Factor** $PF$ —
implement one function, not five:

$$ETC = PF \times (BAC - EV) \qquad EAC = AC + ETC \qquad VAC = BAC - EAC$$

| Variant (enum) | $PF$ | Resulting $EAC$ | Assumption — when to use |
| --- | --- | --- | --- |
| `CpiBased` **(default)** | $\dfrac{1}{CPI}$ | $\dfrac{BAC}{CPI}$ | Current cost performance is *typical* and will continue. Default for unit-rate/repetitive construction work (floors, piles, m² of finishes) where the unit cost already realised is the best predictor. |
| `Atypical` | $1$ | $AC + (BAC - EV)$ | The overrun/underrun was a **one-off** (a single unforeseen ground condition, a one-time price spike, an FX event) and remaining work will run at the planned rate. Requires a documented root cause — otherwise it flatters the forecast. |
| `CpiSpiBased` | $\dfrac{1}{CPI \times SPI}$ | $AC + \dfrac{BAC - EV}{CPI \times SPI}$ | Schedule slippage is itself driving cost (acceleration, overtime, extended preliminaries/site overheads, liquidated damages exposure). The most conservative variant when $CPI<1$ and $SPI<1$. |
| `BottomUpEtc` | — | $AC + ETC_{manual}$ | A fresh re-estimate of the remaining work exists (revised BoQ / QS re-measure). Overrides all indices. Required for the final forecast before a contract close-out. |
| `CustomPf` | user value $PF_c$ | $AC + PF_c(BAC - EV)$ | Contract- or QS-mandated factor (e.g. a lender's agreed 1.15). Also the variant used to replicate a P6 project configured with a custom Performance Factor. |

Identity worth knowing (and worth a unit test): $PF = 1/CPI$ really does collapse to $BAC/CPI$ —

$$AC + \frac{BAC-EV}{CPI} = AC + \frac{AC\,(BAC-EV)}{EV} = \frac{AC \cdot BAC}{EV} = \frac{BAC}{CPI}$$

Two more invariants QA should assert:

- `Atypical` always satisfies $VAC = CV$ (algebraically $BAC - AC - BAC + EV = EV - AC$).
- $TCPI$ measured against `CpiBased` EAC always equals $CPI$ exactly:
  $\frac{BAC-EV}{EAC_{CpiBased}-AC} = CPI$.

### $TCPI$ follows the selected variant

$$TCPI_{BAC} = \frac{BAC - EV}{BAC - AC} \qquad TCPI_{EAC} = \frac{BAC - EV}{EAC - AC} = \frac{BAC-EV}{ETC}$$

Show $TCPI_{BAC}$ while the budget is still deemed achievable ($VAC \ge 0$); switch the KPI tile to
$TCPI_{EAC}$ once management has accepted a new EAC. Both undefined (null, render "—") when their
denominator is 0.

### Variant ordering (sanity check for reviewers)

- Unfavourable performance ($CPI<1, SPI<1$): $EAC_{Atypical} < EAC_{CpiBased} < EAC_{CpiSpiBased}$.
- Favourable performance ($CPI>1, SPI>1$): the order **reverses**.
A UI that lists the variants must never assume a fixed ordering when colour-coding.

### Edge-case rules (deterministic, no exceptions thrown)

| Condition | Behaviour |
| --- | --- |
| $BAC - EV = 0$ (work complete, or zero-budget scope) | **Short-circuit before dividing:** $ETC = 0.00$, $EAC = AC$ for *every* variant. Never evaluate $PF$. |
| $EV = 0 \wedge AC = 0 \wedge PV = 0$ (not started) | All variants → `null`, reason `NotStarted`. Render "—". |
| $AC = 0 \wedge EV > 0$ (actuals not yet posted) | $CPI$ undefined → `CpiBased`, `CpiSpiBased`, and (by rule) `Atypical` all → `null`, reason `NoActualCost`, plus a data-quality warning. `Atypical` is arithmetically computable ($=BAC-EV$) but excludes the cost of work already done, so it is **suppressed on purpose**. `BottomUpEtc` remains valid if $ETC_{manual}$ is supplied. ⚠️ Two distinct causes share this reason — *no cost entries at all* vs *entries that net to 0.00* (a reversed accrual). The reason code stays the same; the **warning copy must differ**, which is why the AC reader returns an entry count (actual-cost.md §7.6, fixtures AC-5/AC-6). |
| $AC < 0$ (over-reversal / credit note exceeds cost) | Compute $AC$ as-is, **do not clamp**; $CPI$ is meaningless → return `null` with recommended reason `NegativeActualCost` + data-quality warning (actual-cost.md §7.6, open question Q6). |
| $PV = 0 \wedge EV > 0$ (no baseline / unbaselined scope) | $SPI$ undefined → `CpiSpiBased` → `null`, reason `NoPlannedValue`. `CpiBased`, `Atypical`, `BottomUpEtc`, `CustomPf` remain valid. |
| $EV = 0 \wedge AC > 0$ (cost burned, nothing earned) | $CPI = 0$ → `CpiBased`, `CpiSpiBased` → `null` (division by zero), reason `ZeroCpi`. `Atypical` = $AC + BAC$ is valid and is what the dashboard should show. |
| $EV > BAC$ (progress or weights corrupt) | $ETC$ goes negative. Compute and return as-is, **and** raise validation warning `EarnedValueExceedsBudget`. Clamp `ProgressPercentage` to $[0,100]$ at input instead of masking it here. |
| $PF_c \le 0$ for `CustomPf` | Reject at validation (FluentValidation), do not compute. |

Rounding: keep full `decimal` precision through $PF$ and $ETC$; round **once** at the response
boundary to `decimal(18,2)` money / `decimal(5,2)` percent, **half-away-from-zero**.
`Math.Round(x, 2)` in .NET is banker's rounding by default — must pass
`MidpointRounding.AwayFromZero` or results diverge from SQL Server's `ROUND()` and from the
printed Thai certificates.

### Worked examples — variant expansion of Fixture A (use verbatim as fixtures)

Same inputs as **Fixture A** above: $BAC = 1{,}000{,}000.00$; $PV = 400{,}000.00$;
$EV = 300{,}000.00$; $AC = 350{,}000.00$; $CPI = 6/7 = 0.857142\overline{857}$; $SPI = 0.75$;
$BAC - EV = 700{,}000.00$.

| Fixture | Variant | $PF$ | $ETC$ | $EAC$ | $VAC$ |
| --- | --- | --- | --- | --- | --- |
| A1 | `CpiBased` | $7/6 = 1.166667$ | 816,666.67 | **1,166,666.67** | −166,666.67 |
| A2 | `Atypical` | 1 | 700,000.00 | **1,050,000.00** | −50,000.00 (= CV ✓) |
| A3 | `CpiSpiBased` | $14/9 = 1.555556$ | 1,088,888.89 | **1,438,888.89** | −438,888.89 |
| A4 | `BottomUpEtc` ($ETC_{manual}=760{,}000.00$) | — | 760,000.00 | **1,110,000.00** | −110,000.00 |
| A5 | `CustomPf` ($PF_c = 1.20$) | 1.20 | 840,000.00 | **1,190,000.00** | −190,000.00 |

$TCPI_{BAC} = 700{,}000 / 650{,}000 = 1.0769$; $TCPI_{EAC}$ against A1 $= 700{,}000/816{,}666.67 = 0.8571 = CPI$ ✓.
A1 is unchanged from the original Fixture A — the existing test must still pass.

### Worked examples — Fixture D (new: ahead of schedule, under budget)

$BAC = 500{,}000.00$; $PV = 200{,}000.00$; $EV = 220{,}000.00$; $AC = 200{,}000.00$
→ $SV = +20{,}000.00$; $CV = +20{,}000.00$; $SPI = 1.10$; $CPI = 1.10$; $BAC - EV = 280{,}000.00$.

| Fixture | Variant | $PF$ | $ETC$ | $EAC$ | $VAC$ |
| --- | --- | --- | --- | --- | --- |
| D1 | `CpiBased` | $1/1.10 = 0.909091$ | 254,545.45 | **454,545.45** | +45,454.55 |
| D2 | `Atypical` | 1 | 280,000.00 | **480,000.00** | +20,000.00 (= CV ✓) |
| D3 | `CpiSpiBased` | $1/1.21 = 0.826446$ | 231,404.96 | **431,404.96** | +68,595.04 |

$TCPI_{BAC} = 280{,}000/300{,}000 = 0.9333$ (may relax and still finish on budget).
Ordering here is $D3 < D1 < D2$ — the mirror of Fixture A. Assert this reversal in tests.

### Edge fixtures for the variant engine

| Fixture | Inputs | Expected |
| --- | --- | --- |
| E — missing actuals | BAC 1,000,000; PV 400,000; EV 300,000; **AC 0** | CPI null; `CpiBased`/`CpiSpiBased`/`Atypical` = null, reason `NoActualCost`; `BottomUpEtc` valid |
| F — no baseline | BAC 1,000,000; **PV 0**; EV 300,000; AC 350,000 | SPI null; `CpiSpiBased` = null (`NoPlannedValue`); `CpiBased` = 1,166,666.67; `Atypical` = 1,050,000.00 |
| G — zero earned | BAC 1,000,000; PV 100,000; **EV 0**; AC 50,000 | CPI = 0; SPI = 0; `CpiBased`/`CpiSpiBased` = null (`ZeroCpi`); `Atypical` = **1,050,000.00** |
| H — nothing remaining | BAC 1,000,000; **EV 1,000,000**; AC 1,100,000 | short-circuit: ETC = 0.00, EAC = **1,100,000.00**, VAC = −100,000.00, for all variants; no division executed |
| I — zero-budget project | BAC 0; PV 0; EV 0; AC 0 | all null, reason `NotStarted` (do not return 0.00) |

### Storage & API shape — *recommendation for `system-architect`, not an accepted ADR*

1. `Project.EacVariantDefault` — non-null enum
   (`CpiBased | Atypical | CpiSpiBased | BottomUpEtc | CustomPf`), seeded to **`CpiBased`**.
2. `Project.EacCustomPerformanceFactor decimal(9,4) NULL` — required only when the default (or an
   override) is `CustomPf`; validate $> 0$.
3. `Project.EacManualEtc decimal(18,2) NULL` — required only for `BottomUpEtc`; validate $\ge 0$.
4. Per-request override on `GET /api/v1/projects/{id}/evm?dataDate=&eacVariant=`; an unknown value
   → 400 ProblemDetails, never a silent fallback.
5. **Return all computable variants in every EVM response** plus `selectedVariant`. The whole set
   is five multiplications on four scalars — computing them all costs nothing and lets the EVM
   screen offer a variant switcher with no round trip.
6. Do **not** persist EAC on `Project`. It is derived. Persist it only inside dated EVM period
   snapshots (for the trend chart), and store the variant + PF alongside each snapshot so an old
   snapshot stays explainable after the default changes.
7. Changing `EacVariantDefault` is a mutating domain operation → audit log entry.

### Reconciliation with P6 / MS Project

- **Primavera P6** models exactly the same unified form: `ETC = PF × (BAC − EV)`, with the
  project-level Earned Value setting offering `PF = 1`, `PF = 1/CPI`, `PF = 1/(CPI × SPI)`, and a
  user-entered PF. CM+'s variants therefore map 1:1 onto P6 settings — this is why the unified
  form above is the required implementation shape.
- P6's *default* ETC setting is the schedule's own **remaining cost** (bottom-up), not a PF
  formula. So a raw XER import compared against CM+ defaults will legitimately differ. To
  reconcile, either set the CM+ variant to `BottomUpEtc` with P6's remaining cost, or set P6 to
  `PF = 1/CPI`. **[ต้องยืนยัน]** — confirm the source project's actual P6 Earned Value setting on
  the first real XER import before signing off any golden-file EAC test.
- **MS Project** field `EAC` is defined as $ACWP + (BAC - BCWP)/CPI$, i.e. identical to
  `CpiBased`; MSP `VAC = BAC − EAC` and MSP `TCPI = (BAC-BCWP)/(BAC-ACWP)` = our $TCPI_{BAC}$.
  Choosing `CpiBased` as the CM+ default therefore makes CM+ agree with MS Project out of the box.
- P6 uses `BAC` = budget at completion of the *baseline*; if CM+ has rebaselined for an approved VO
  and P6 has not, EAC will differ by the VO amount. Always compare like-for-like baselines.

## Cash-flow / S-Curve

Three cumulative series on the time axis: PV (plan), EV (earned), AC (actual), plus EAC
forecast extension (dashed, forecast-blue). A VO approval changes BAC → S-Curve rebaselines
from the approval date; historical points are never rewritten (audit integrity).

The AC series is time-phased on `IncurredDate` using **half-open, lower-exclusive** period buckets
$AC_{(a,b]}$ so adjacent periods never double-count a boundary day (actual-cost.md §7.5, fixture
AC-10). Cash Flow additionally plots **cumulative receipts** from the certificate/finance ledger —
a *different* series from AC; their difference is the funding position and is the only legitimate
arithmetic between the two ledgers (actual-cost.md §5).
