# EVM Formulas — Canonical Reference (with test fixtures)

Source: docs/4. §3 + PMI standard practice. Precision: money `decimal(18,2)`,
percent `decimal(5,2)`; keep full precision in intermediates, round at display.

## Core metrics at data date $t$

| Metric | Formula | Meaning |
| --- | --- | --- |
| $PV$ (BCWS) | $\sum_{i \in scheduled(t)} BudgetCost_i \cdot plannedPct_i(t)$ | มูลค่าตามแผน |
| $EV$ (BCWP) | $\sum_i BudgetCost_i \cdot \frac{ProgressPct_i}{100}$ | มูลค่างานที่ทำได้จริง |
| $AC$ (ACWP) | $\sum$ actual cost recorded $\le t$ | ค่าใช้จ่ายจริง |
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

## Cash-flow / S-Curve

Three cumulative series on the time axis: PV (plan), EV (earned), AC (actual), plus EAC
forecast extension (dashed, forecast-blue). A VO approval changes BAC → S-Curve rebaselines
from the approval date; historical points are never rewritten (audit integrity).
