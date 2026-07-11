---
name: cm-domain
description: Construction project-control domain reference for CM+ — EVM formulas (PV/EV/AC, SV/CV, SPI/CPI, EAC/ETC/VAC), CPM scheduling (forward/backward pass, float, critical path), payment/retention math, and weather-EOT rules. Load before implementing, reviewing, or testing any domain calculation.
---

# CM+ Domain: EVM, CPM, Payment & Weather Rules

Canonical formula reference. Deep detail and worked examples live in
`.claude/knowledge/domain/evm-formulas.md` and `.claude/knowledge/domain/cpm-method.md` —
read those files for edge cases and test fixtures.

## Precision rules (apply everywhere)

- Money: `decimal(18,2)`. Percent: `decimal(5,2)`. Round half-up at the final display step only;
  keep full precision through intermediate calculation.
- Division by zero: SPI/CPI are undefined when the denominator is 0 → return null/`—`, never 0 or ∞.

## EVM (Earned Value Management)

With `BAC` = Budget at Completion, at data date *t*:

- **PV (BCWS)** = Σ planned budget of work scheduled up to *t* (weight-based rollup through WBS).
- **EV (BCWP)** = Σ (BudgetCost × ProgressPercentage/100) over activities.
- **AC (ACWP)** = Σ actual cost recorded up to *t*.
- **SV** = EV − PV (≥ 0 on/ahead of schedule) · **CV** = EV − AC (≥ 0 under budget)
- **SPI** = EV / PV · **CPI** = EV / AC
- **EAC** (default, ongoing performance): `EAC = BAC / CPI`.
  Variants: `EAC = AC + (BAC − EV)` (atypical variance); `EAC = AC + (BAC − EV)/(CPI × SPI)`
  (schedule-and-cost driven). The variant used must be stated in the spec.
- **ETC** = EAC − AC · **VAC** = BAC − EAC
- Progress rollup: parent % = Σ(child % × child weight) / Σ(child weight); project % uses
  WBS `WeightPercentage` (budget-proportional weights).

## CPM (Critical Path Method)

Activity *i* with duration `D_i`, relations FS/SS/FF/SF with lag:

- **Forward pass:** `ES_start = 0`; `EF_i = ES_i + D_i`;
  `ES_i = max over predecessors p of (constraint from relation type + lag)` (FS: `EF_p + lag`).
- **Backward pass:** `LF_last = project EF`; `LS_i = LF_i − D_i`;
  `LF_i = min over successors s` (FS: `LS_s − lag`).
- **Total Float** `TF_i = LS_i − ES_i = LF_i − EF_i` · **Free Float** `FF_i = min(ES_succ) − EF_i`
- `TF = 0` → critical activity → red bar on Gantt (`IsCritical = true`).
- Must reject cyclic relation graphs before computing (topological sort first).
- Reconciliation: results must match Primavera P6 for the same network (QA golden tests).

## Payment / เบิกงวด

For milestone value `M`, retention rate `r`, advance recovery `a`:
`Net Payment = M × progress% − (M × progress% × r) − advance recovery installment`.
Retention is released per contract terms (typically 50% at substantial completion, 50% after
defect liability period). VO-approved amounts adjust `BAC` and future milestone bases —
S-Curve must rebaseline when a VO is approved.

## Weather Log / EOT (ขอขยายเวลา)

Daily log: date, condition, rainfall (mm), work-stoppage flag, affected activities.
A stoppage day on a **critical-path activity** is EOT-eligible evidence; on non-critical
activities it only consumes float. Logs are legal evidence — immutable once submitted
(corrections are new audited entries, never edits).
