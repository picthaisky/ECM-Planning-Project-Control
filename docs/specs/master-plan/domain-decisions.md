# Master Plan — Domain Decisions (consolidated)

**Author:** `domain-expert` · **Date:** 2026-07-27 · **Status:** ready for `system-architect`
**Closes:** docs/9. §11 items **2** (EAC variant), **3** (Retention/Advance config), **4** (VO/Payment approval authority)

Self-contained. Full derivations, invariants and extra fixtures live in
`.claude/knowledge/domain/evm-formulas.md` (§ EAC Variants) and
`.claude/knowledge/domain/approval-workflow.md` — but everything needed to design schema and
handlers is reproduced here.

Precision everywhere: money `decimal(18,2)`, percent `decimal(5,2)`, dates `DateTimeOffset`,
keys `Guid`. Keep full precision through intermediates; round **once**, **half-away-from-zero**.

---

## 1. EAC is a selectable variant

### 1.1 One formula, five variants

Implement a **single** Performance-Factor function, not five formulas:

$$ETC = PF \times (BAC - EV) \qquad EAC = AC + ETC \qquad VAC = BAC - EAC$$

| Enum value | $PF$ | Closed form | Use when |
| --- | --- | --- | --- |
| **`CpiBased`** ← **recommended default** | $1/CPI$ | $EAC = \dfrac{BAC}{CPI}$ | Current cost performance is typical and will continue. |
| `Atypical` | $1$ | $EAC = AC + (BAC - EV)$ | The variance was a documented one-off; remaining work runs at plan. |
| `CpiSpiBased` | $1/(CPI \times SPI)$ | $EAC = AC + \dfrac{BAC-EV}{CPI \times SPI}$ | Schedule slip is itself driving cost (acceleration, extended preliminaries). |
| `BottomUpEtc` | — | $EAC = AC + ETC_{manual}$ | A fresh QS re-estimate of remaining work exists. |
| `CustomPf` | $PF_c$ (user) | $EAC = AC + PF_c(BAC-EV)$ | Contract/lender-mandated factor; also how we replicate a P6 custom PF. |

Where $CPI = EV/AC$, $SPI = EV/PV$, $BAC$ = budget at completion (including approved VOs),
$AC$ = ACWP to data date, $EV$ = BCWP to data date, $PV$ = BCWS to data date. All money in THB.

$TCPI$ follows the selected variant:
$$TCPI_{BAC} = \frac{BAC-EV}{BAC-AC} \qquad TCPI_{EAC} = \frac{BAC-EV}{EAC-AC}$$

### 1.2 Why `CpiBased` is the recommended default

1. It is already the stated default in docs/9. §5 and in the existing `evm-formulas.md` — no
   existing fixture or test breaks.
2. Construction work is largely unit-rate and repetitive (per floor, per pile, per m² of finish),
   so realised unit cost is the best available predictor of remaining unit cost.
3. It equals **MS Project's `EAC` field** exactly ($ACWP + (BAC-BCWP)/CPI$), and equals
   **Primavera P6** configured with `PF = 1/CPI`. Choosing it makes CM+ agree with MSP out of the box.
4. It is the least "opinionated" of the three index-based options: `Atypical` requires a
   documented root cause to be honest, and `CpiSpiBased` double-counts unless schedule pressure is
   demonstrably monetised.

### 1.3 Edge cases (must be deterministic — no exceptions, no NaN)

| Condition | Result |
| --- | --- |
| $BAC - EV = 0$ | **Short-circuit before dividing:** $ETC=0.00$, $EAC=AC$, for every variant. |
| $EV=0 \wedge AC=0 \wedge PV=0$ | all variants `null`, reason `NotStarted`, render "—" |
| $AC=0 \wedge EV>0$ | `null` for `CpiBased`, `CpiSpiBased` **and `Atypical`** (reason `NoActualCost`) + data-quality warning. `Atypical` is computable but excludes the cost of completed work, so it is suppressed deliberately. `BottomUpEtc` still valid. |
| $PV=0 \wedge EV>0$ | `CpiSpiBased` → `null` (`NoPlannedValue`); others valid |
| $EV=0 \wedge AC>0$ | $CPI=0$ → `CpiBased`, `CpiSpiBased` → `null` (`ZeroCpi`); `Atypical` = $AC+BAC$ and is what the dashboard shows |
| $EV > BAC$ | compute as-is (negative ETC) **and** raise `EarnedValueExceedsBudget`; clamp `ProgressPercentage` to $[0,100]$ at input |
| $PF_c \le 0$ | FluentValidation rejects; never compute |

### 1.4 Worked example — Fixture A (reuses the existing fixture verbatim)

$BAC=1{,}000{,}000.00$; $PV=400{,}000.00$; $EV=300{,}000.00$; $AC=350{,}000.00$
→ $SV=-100{,}000.00$; $CV=-50{,}000.00$; $SPI=0.75$; $CPI=0.857142\overline{857}$; $BAC-EV=700{,}000.00$

| Variant | $PF$ | $ETC$ | $EAC$ | $VAC$ |
| --- | --- | --- | --- | --- |
| `CpiBased` | 1.166667 | 816,666.67 | **1,166,666.67** | −166,666.67 |
| `Atypical` | 1 | 700,000.00 | **1,050,000.00** | −50,000.00 |
| `CpiSpiBased` | 1.555556 | 1,088,888.89 | **1,438,888.89** | −438,888.89 |
| `BottomUpEtc` ($ETC_{manual}$ = 760,000.00) | — | 760,000.00 | **1,110,000.00** | −110,000.00 |
| `CustomPf` ($PF_c$ = 1.20) | 1.20 | 840,000.00 | **1,190,000.00** | −190,000.00 |

$TCPI_{BAC} = 700{,}000/650{,}000 = 1.0769$.

### 1.5 Worked example — Fixture D (favourable performance; ordering reverses)

$BAC=500{,}000.00$; $PV=200{,}000.00$; $EV=220{,}000.00$; $AC=200{,}000.00$
→ $SPI=1.10$; $CPI=1.10$; $BAC-EV=280{,}000.00$

| Variant | $PF$ | $ETC$ | $EAC$ | $VAC$ |
| --- | --- | --- | --- | --- |
| `CpiBased` | 0.909091 | 254,545.45 | **454,545.45** | +45,454.55 |
| `Atypical` | 1 | 280,000.00 | **480,000.00** | +20,000.00 |
| `CpiSpiBased` | 0.826446 | 231,404.96 | **431,404.96** | +68,595.04 |

Unfavourable performance orders `Atypical < CpiBased < CpiSpiBased`; favourable performance
**reverses** it. UI colour-coding must not assume a fixed order.

### 1.6 Invariants for QA

- `Atypical` always gives $VAC = CV$.
- $TCPI$ against `CpiBased` EAC always equals $CPI$ exactly.
- $PF=1/CPI$ collapses to $BAC/CPI$ — assert both code paths agree to the cent.

### 1.7 Field & API recommendations *(recommendation, not an accepted ADR)*

```
Project.EacVariantDefault             enum NOT NULL  default CpiBased
Project.EacCustomPerformanceFactor    decimal(9,4) NULL   -- required iff CustomPf; > 0
Project.EacManualEtc                  decimal(18,2) NULL  -- required iff BottomUpEtc; >= 0
```

- Override per request: `GET /api/v1/projects/{id}/evm?dataDate=&eacVariant=`; unknown value →
  400 ProblemDetails, never a silent fallback.
- **Return every computable variant in the EVM response** with a `selectedVariant` marker. The set
  is five multiplications on four scalars — the EVM screen gets a variant switcher for free.
- **Do not persist EAC on `Project`.** It is derived. Persist it only in dated EVM period
  snapshots, storing the variant + PF used, so an old snapshot stays explainable after the default
  changes.
- Changing `EacVariantDefault` is a mutating domain operation → audit log entry.

### 1.8 Reconciliation notes

- P6 uses the identical unified form (`ETC = PF × (BAC − EV)`) with PF ∈ {1, 1/CPI, 1/(CPI×SPI),
  custom} — mapping is 1:1. **However P6's factory default ETC is the schedule's own remaining
  cost (bottom-up)**, so a raw XER compared against CM+ defaults will legitimately differ.
  **[ต้องยืนยัน]** — confirm the source project's real P6 Earned Value setting before signing off
  any golden-file EAC test.
- MS Project `EAC`/`VAC`/`TCPI` map to `CpiBased`, $BAC-EAC$, and $TCPI_{BAC}$ respectively.
- If CM+ has rebaselined for an approved VO and P6 has not, EAC differs by the VO amount — always
  compare like-for-like baselines.

---

## 2. Retention & Advance are per-project configurable

### 2.1 Corrected Net Payment formula

The sketch in docs/9. §5 is **incomplete in three ways**: it deducts on cumulative rather than
period value (double-deduction on partially certified milestones), it uses `progress%` rather than
the *certified* `ApprovePct`, and it has no retention cap. Corrected:

For certificate $k$ against milestone $m$ with milestone value $M_m$:

$$G^{cum}_k = M_m \times \frac{p^{app}_k}{100} \qquad\qquad \boxed{G_k = G^{cum}_k - G^{cum}_{k-1}}$$

$$\boxed{R_k = \max\!\Big(0,\ \min\big(\tfrac{r}{100} G_k,\ \ R^{max} - R^{cum}_{k-1}\big)\Big)}
\qquad R^{max} = \tfrac{c}{100}\,C \ \ (=\infty \text{ if } c \text{ is null})$$

$$\boxed{D_k = \max\!\Big(0,\ \min\big(\tfrac{a}{100} G_k,\ \ A - D^{cum}_{k-1},\ \ G_k - R_k\big)\Big)}$$

$$\boxed{N_k = G_k - R_k - D_k}$$

| Symbol | Meaning | Type / source |
| --- | --- | --- |
| $M_m$ | Milestone value (THB) | `PaymentCertificate.MilestoneValue` |
| $p^{app}_k$ | **Cumulative certified %** for that milestone | `PaymentCertificate.ApprovePct` |
| $G_k$ | Gross certified value **this period** | new field `GrossCertifiedAmount` |
| $r$ | Retention rate (%) | `Project.RetentionRate` |
| $c$ | Retention cap (% of contract), nullable | **new** `Project.RetentionCapPercentage` |
| $C$ | Contract value incl. approved VOs | **new** `Project.ContractValue` |
| $R^{cum}_{k-1}$ | Retention held before this certificate | ledger sum (§2.4) |
| $a$ | Advance rate (%) | `Project.AdvanceRate` |
| $A$ | Advance actually disbursed | **new** `Project.AdvanceAmountPaid` (default $\tfrac{a}{100}C$) |
| $D^{cum}_{k-1}$ | Advance recovered before this certificate | ledger sum (§2.4) |
| $N_k$ | **Net certified amount** this period (pre-tax) | `PaymentCertificate.NetPayment` |

Notes:
- Deductions are computed on the **period** gross $G_k$, never the cumulative.
- The third term of $D_k$ ($G_k - R_k$) guarantees $N_k \ge 0$; the unrecovered balance carries to
  the next certificate. Only binds when $a + r > 100$ or catch-up recovery is enabled.
- The whole formula contains **no division** — there is no division-by-zero case. The only guards
  needed are monotonicity ($p^{app}_k \ge p^{app}_{k-1}$) and the $\max(0,\cdot)$ clamps.
- Round **each line** to 2 dp half-away-from-zero, then subtract. Never compute $N_k$ at full
  precision and round independently — the printed Thai certificate must foot. Divergence between
  the two approaches is bounded by 0.01 per deduction line, and the line-item order is authoritative.
- ⚠️ **.NET trap:** `Math.Round(x, 2)` is banker's rounding. Must pass
  `MidpointRounding.AwayFromZero`, or CM+ diverges from SQL Server `ROUND()` and from the printed
  certificate. Example: $G_k = 1{,}000{,}000.10$, $r=5.00$ → $R_k = 50{,}000.005$ →
  **50,000.01** (correct) vs 50,000.00 (banker's) → $N_k$ off by 0.01.

### 2.2 Is there a retention cap? — Yes, and CM+ needs the field

**Recommendation: add `Project.RetentionCapPercentage decimal(5,2) NULL`** (null = uncapped).

- **Thai standard construction contract** (แบบสัญญาจ้างก่อสร้าง under พ.ร.บ.การจัดซื้อจัดจ้างฯ 2560):
  the employer withholds **5% of each installment** as เงินประกันผลงาน with no separate ceiling —
  i.e. the cap is implicitly 5% of contract, which the 5% per-payment rate reaches only at
  completion. `RetentionCapPercentage = NULL` models this exactly. (หลักประกันสัญญา 5% is a
  *separate* performance bond, not retention — do not conflate them in the data model.)
- **FIDIC Red Book Sub-Clause 14.3(c)** deducts "the percentage of retention" up to "the Limit of
  Retention Money", both stated in the Appendix to Tender. The common pairing is **10% deducted per
  IPC, limited to 5% of the Accepted Contract Amount** — here the cap binds hard, at 50% completion.
  Without the field, CM+ over-deducts every certificate after that point.

Because both patterns are in scope for CM+'s users, the cap must be data, not a constant.

⚠️ **Prototype inconsistency to fix:** `docs/ECM Planning Prototype.dc.html` shows
"Retention **5%** (เพดาน **10%** ของสัญญา)". With a 5% per-payment rate, cumulative retention can
never exceed 5% of contract, so a 10% cap is unreachable and misleading. Either the rate is 10% and
the cap 5% (FIDIC), or the cap should be blank (Thai standard). Flag to `po-analyst`/human.

Also note: retention accrual is a % of **contract value**, not of `BAC` (budget). CM+ currently has
only `Project.BAC`. **Recommend adding `Project.ContractValue decimal(18,2)`**, defaulting to `BAC`.
If the human confirms CM+ always treats budget ≡ contract sum, the cap may reference `BAC` instead —
**[ต้องยืนยัน]**.

### 2.3 Advance recovery methods

**Recommend `Project.AdvanceRecoveryMethod enum { ProRata, ThresholdBanded, Manual }`, default
`ProRata`.**

- **`ProRata` (default, Thai practice):** $D_k = \tfrac{a}{100}G_k$, capped by the outstanding
  balance. Self-consistent: with $A = \tfrac{a}{100}C$, the advance is fully recovered exactly when
  cumulative gross reaches $C$.
- **`ThresholdBanded` (FIDIC 14.2):** recovery starts only after cumulative certification passes a
  start threshold $s$ (% of $C$) and amortises at rate $\rho$ of the excess:
  $$D_k = \max\!\Big(0,\ \min\big(\tfrac{\rho}{100}\max(0,\ G^{cum}_k - \tfrac{s}{100}C) - D^{cum}_{k-1},\ \ A - D^{cum}_{k-1}\big)\Big)$$
  with a forced full recovery once $G^{cum}_k \ge \tfrac{e}{100}C$. FIDIC defaults
  $s=10$, $\rho=25$, $e=90$. Needs `AdvanceRecoveryStartPct`, `AdvanceRecoveryRatePct`,
  `AdvanceRecoveryEndPct` (all nullable, only used by this method).
- **`Manual`:** the QS enters $D_k$ per certificate; still capped by the outstanding balance.

### 2.4 Retention release & the ledger

$$\text{Release}_1 = \tfrac{\rho_1}{100} R^{cum} \text{ at substantial completion / taking-over}
\qquad \text{Release}_2 = R^{cum} - \text{Release}_1 \text{ at end of DLP}$$

Common Thai/FIDIC value $\rho_1 = 50.00$. Some contracts release 100% early against a bank
guarantee — model that as an `Adjustment` ledger entry, not a formula.

**Recommend a ledger rather than recomputation**, so $R^{cum}$ and $D^{cum}$ are `SUM()`s that are
audit-provable and make the cap check trivial:

```
ProjectFinanceLedger
  Id, TenantId, ProjectId, PaymentCertificateId NULL
  Category  enum { Retention, Advance }
  EntryType enum { Accrual, Release, Disbursement, Recovery, Adjustment }
  Amount    decimal(18,2)      -- signed; Retention held = SUM(Amount) where Category = Retention
  EffectiveDate DateTimeOffset, Reference, Note
```

Append-only. Written when a certificate reaches `Certified` (accrual/recovery) and on release events.

### 2.5 Worked examples (unit-test fixtures)

**P1 — normal period, Thai config (no cap).**
$C=485{,}000{,}000.00$; $r=5.00$; $c=$ null; $a=10.00$ → $A=48{,}500{,}000.00$;
$R^{cum}_{k-1}=11{,}900{,}000.00$; $D^{cum}_{k-1}=23{,}800{,}000.00$;
$M=21{,}600{,}000.00$; $p^{app}=100.00$; $p^{app}_{k-1}=0$.
→ $G_k = 21{,}600{,}000.00$ · $R_k = 1{,}080{,}000.00$ · $D_k = 2{,}160{,}000.00$
· $\mathbf{N_k = 18{,}360{,}000.00}$

**P2 — partial certification (proves period-vs-cumulative).**
$M=10{,}000{,}000.00$; prior $p^{app}=40.00$ → $G^{cum}_{k-1}=4{,}000{,}000.00$; this period
$p^{app}=65.00$ → $G^{cum}_k=6{,}500{,}000.00$; $r=5.00$, $a=10.00$, no caps binding.
→ $G_k = 2{,}500{,}000.00$ · $R_k=125{,}000.00$ · $D_k=250{,}000.00$
· $\mathbf{N_k = 2{,}125{,}000.00}$
*(Deducting on the cumulative 6,500,000 instead would over-deduct by 200,000 and 400,000.)*

**P3 — FIDIC cap bites (the fixture that fails without `RetentionCapPercentage`).**
$C=100{,}000{,}000.00$; $r=10.00$; $c=5.00$ → $R^{max}=5{,}000{,}000.00$;
$R^{cum}_{k-1}=4{,}500{,}000.00$; $a=10.00$, $A=10{,}000{,}000.00$, $D^{cum}_{k-1}=4{,}500{,}000.00$;
$G_k=8{,}000{,}000.00$.
→ uncapped retention 800,000.00 but headroom only 500,000.00 → $R_k = 500{,}000.00$;
$D_k = 800{,}000.00$; $\mathbf{N_k = 6{,}700{,}000.00}$.
**Without cap logic $N_k$ would be 6,400,000.00 — a 300,000.00 under-payment.**

**P3b — next period, cap fully consumed.** $G_k = 5{,}000{,}000.00$, $R^{cum}=5{,}000{,}000.00$
→ $R_k = \mathbf{0.00}$; $D_k = 500{,}000.00$; $\mathbf{N_k = 4{,}500{,}000.00}$.

**P4 — advance nearly exhausted.** $A=10{,}000{,}000.00$, $D^{cum}_{k-1}=9{,}960{,}000.00$,
$G_k=1{,}000{,}000.00$, $r=5.00$, $a=10.00$.
→ $R_k=50{,}000.00$; uncapped $D$ 100,000.00 but only 40,000.00 outstanding → $D_k = 40{,}000.00$;
$\mathbf{N_k = 910{,}000.00}$.

**P5 — edge: nothing certified.** $p^{app}_k = p^{app}_{k-1}$ (or certificate returned) →
$G_k = 0.00$, $R_k = 0.00$, $D_k = 0.00$, $\mathbf{N_k = 0.00}$. No division anywhere.

**P6 — `ThresholdBanded` advance recovery (FIDIC).** $C=100{,}000{,}000.00$, $A=10{,}000{,}000.00$,
$s=10$, $\rho=25$, $e=90$:

| Cert | $G^{cum}_k$ | Excess over $sC$ | Target $D^{cum}$ | $D_k$ |
| --- | --- | --- | --- | --- |
| 1 | 8,000,000.00 | 0 | 0.00 | **0.00** |
| 2 | 15,000,000.00 | 5,000,000.00 | 1,250,000.00 | **1,250,000.00** |
| 3 | 25,000,000.00 | 15,000,000.00 | 3,750,000.00 | **2,500,000.00** |
| … | 50,000,000.00 | 40,000,000.00 | 10,000,000.00 = $A$ | advance fully recovered |

**P7 — rounding determinism.** $G_k = 1{,}000{,}000.10$, $r=5.00$, $a=10.00$
→ $R_k = 50{,}000.005 \to \mathbf{50{,}000.01}$ (half-away-from-zero);
$D_k = \mathbf{100{,}000.01}$; $\mathbf{N_k = 850{,}000.08}$.
Banker's rounding yields 850,000.09 — the defect this fixture exists to catch.

### 2.6 Field recommendations *(recommendation, not an accepted ADR)*

```
Project.RetentionRate              decimal(5,2)   -- EXISTS
Project.AdvanceRate                decimal(5,2)   -- EXISTS
Project.ContractValue              decimal(18,2)  -- NEW, defaults to BAC   [ต้องยืนยัน §2.2]
Project.RetentionCapPercentage     decimal(5,2) NULL          -- NEW, null = uncapped
Project.RetentionRelease1Percentage decimal(5,2) default 50.00 -- NEW
Project.DefectsLiabilityMonths     int NULL                    -- NEW
Project.AdvanceAmountPaid          decimal(18,2) NULL          -- NEW, actual disbursement
Project.AdvanceRecoveryMethod      enum default ProRata        -- NEW
Project.AdvanceRecoveryStartPct / RatePct / EndPct  decimal(5,2) NULL  -- NEW, ThresholdBanded only

PaymentCertificate.GrossCertifiedAmount    decimal(18,2)  -- NEW, also the approval routing amount
PaymentCertificate.AdvanceRecoveryAmount   decimal(18,2)  -- NEW; without it the printed
                                                          --   certificate cannot foot
PaymentCertificate.PreviousCumulativeApprovePct decimal(5,2) -- NEW (or derive from prior cert)
PaymentCertificate.RevisionNo              int            -- NEW
```

⚠️ The prototype's Payment Certificate panel shows only *Milestone Value → Retention → Net* with
**no advance-recovery line**. With `AdvanceRate = 10%` configured, its Net Payment is overstated by
10% of the gross. `frontend-developer` must add the advance line (and a retention-cap indicator).

### 2.7 Open question — VAT / withholding tax **[ต้องยืนยัน]**

$N_k$ above is the **net certified amount (pre-tax)**. Thai construction invoicing normally then
applies VAT 7% and withholding tax 3% (จ้างทำของ, juristic persons). Genuinely contentious points —
**do not guess**:

1. Is VAT charged on the gross certified value or on the value net of retention?
2. Is WHT computed on the gross or on the net certified amount?
3. Is VAT on the retained amount accounted at certification or at release?

**Recommendation:** keep the core formula tax-free and model tax as a separate, optional block
(`VatAmount`, `WithholdingTaxAmount`, `NetPayableAmount`) so the answer can be added later without
touching the certified-value math. Ask the client's QS/accounting before Sprint 9.

---

## 3. VO / Payment approval = per-tenant permission matrix

### 3.1 State machines

**Variation Order** — `Draft` → `PendingApproval` → `Approved` | `Rejected`; `Draft` → `Cancelled`.
`Approved`/`Rejected`/`Cancelled` are terminal. `ReturnForRevision` from `PendingApproval` goes
back to `Draft` with `RevisionNo + 1`.

**Payment Certificate** — `NotDue` → `Draft` → `PendingApproval` → `Certified` → `Paid`, plus
`Rejected`/`Cancelled` and the same `ReturnForRevision` loop. `Certified` is not terminal: `Paid`
records actual disbursement.

Schema gaps against docs/9. §4:
- `VariationOrder.Status` is currently `Pending/Approved/Rejected` — add `Draft` and `Cancelled`,
  rename `Pending` → `PendingApproval`.
- `PaymentCertificate.Status` needs the full 7-state enum above.
- The prototype's "ตีกลับ" badge currently maps to `rejected`; it must map to
  **return-for-revision**, otherwise a returned document can never be resubmitted.

Key workflow rules:
- Only the **final** step's approver may `Reject` (terminal). Intermediate approvers may only
  `ReturnForRevision`. Comment mandatory on both.
- `ReturnForRevision` **voids every approval collected on that revision** — no partial carry-over,
  because the amount may have changed.
- From `PendingApproval` onward money fields are frozen; a `Certified` certificate is immutable
  forever (per `conventions.md`); corrections are new documents.
- Optimistic concurrency (`RowVersion`) on every transition — simultaneous approvers → 409, never a
  double-advance.
- Every transition writes `AuditLog` **and** an append-only `ApprovalAction` row.
- `Paid` amounts are **receipts** and must never be written into $AC$ (ACWP).

### 3.2 Amount-tiered approval — **recommended: role + amount threshold**

Tiered Delegation of Authority is standard practice in all three traditions CM+'s users span:
FIDIC (Employer's consent above a stated value), Thai public works (พ.ร.บ.การจัดซื้อจัดจ้างฯ 2560
tiers approval authority by value), and corporate contractor DoA tables (PM → Project Director →
MD/Board by baht value). A role-only matrix cannot express the single most common real-world rule —
*"PM approves VOs under ฿500,000; above that the Project Director must also sign"* — and would
force per-tenant code branches later.

Add a second dimension: **cumulative-VO escalation** — many contracts require owner/board sign-off
once cumulative approved VOs pass a % of contract value (commonly 10%), even for a small individual
VO. Model it as an escalation rule, not by distorting the amount bands.

### 3.3 Matrix shape *(recommendation, not an accepted ADR)*

```
ApprovalPolicy
  Id, TenantId, ProjectId NULL       -- NULL = tenant default; non-null = project override
  DocumentType enum { VariationOrder, PaymentCertificate }
  Version int, IsActive bit, EffectiveFrom, EffectiveTo NULL
  AllowSelfApproval bit default 0
  CumulativeVoEscalationPct decimal(5,2) NULL      -- e.g. 10.00
  CumulativeVoEscalationRole enum NULL

ApprovalPolicyRule                   -- the matrix rows
  Id, ApprovalPolicyId
  StepNo int                         -- 1..n sequential
  MinAmount decimal(18,2)            -- inclusive
  MaxAmount decimal(18,2) NULL       -- exclusive; NULL = unbounded
  RequiredRole enum, RequiredUserId Guid NULL, QuorumCount int default 1

ApprovalAction                       -- append-only ledger, one row per human act
  Id, TenantId, DocumentType, DocumentId, RevisionNo, StepNo
  ActorUserId, ActorRoleAtTime
  Action enum { Submit, Approve, ReturnForRevision, Reject, Withdraw, Cancel, RecordPayment }
  Comment, ActedAt
  ApprovalPolicyId, ApprovalPolicyVersion   -- pin the version that routed this document
```

**Version-pin, never mutate.** Editing a policy creates `Version+1`; documents keep the version that
routed them, so a two-year-old approval stays explainable in a dispute.

### 3.4 Routing algorithm (deterministic, fail-closed)

1. Routing amount: VO → $A^{route} = |Amount|$ (**absolute value is mandatory**; a −฿5M omission
   carries the same authority as a +฿5M addition, and a signed value matches no `MinAmount ≥ 0`
   band). Payment Certificate → $A^{route} = G_k$, the gross certified value.
   *(Alternative: route IPCs on $N_k$. Flagged as an open question below.)*
2. Select the active policy: project override, else tenant default, matched on submission date.
3. Select rules where $MinAmount \le A^{route} < MaxAmount$ (or `MaxAmount IS NULL`); order by `StepNo`.
4. VO only: if $(\sum \text{approved VO} + Amount)/C \times 100 > CumulativeVoEscalationPct$, append
   `CumulativeVoEscalationRole` as a final step if not already present.
5. **Fail closed:** an empty chain blocks submission with `ApprovalPolicyGap` (422) — never
   auto-approve. With no policy at all, fall back to one mandatory `ProjectDirector` step.
6. Validate bands non-overlapping and gap-free per `StepNo` on policy save.
7. If the amount changes during a revision, **re-resolve the chain on resubmission**.
8. Separation of duties: creator/submitter cannot approve unless `AllowSelfApproval = 1`; one human
   cannot satisfy two steps of the same chain.
9. All policy/approver lookups are `TenantId`-scoped (ADR-0002).

### 3.5 Routing fixtures

Policy `TH-Default-VO`: step 1 `PM` [0, 500k) · steps 1–2 `PM`,`ProjectDirector` [500k, 5M) ·
steps 1–3 `PM`,`ProjectDirector`,`Executive` [5M, ∞) · `CumulativeVoEscalationPct = 10.00`,
`ContractValue = 485,000,000.00`.

| # | Input | $A^{route}$ | Expected chain |
| --- | --- | --- | --- |
| R1 | VO Add +2,400,000.00 | 2,400,000.00 | `[PM, ProjectDirector]` |
| R2 | VO Deduct −800,000.00 | **800,000.00** (abs) | `[PM, ProjectDirector]` — signed routing would give an empty chain |
| R3 | VO Add +300,000.00 | 300,000.00 | `[PM]` |
| R4 | VO Add +3,200,000.00, cumulative approved 46,000,000.00 → 10.14% > 10% | 3,200,000.00 | `[PM, ProjectDirector, Executive]` (escalation appends step 3) |
| R5 | VO Add +50,000.00, policy's lowest `MinAmount` = 100,000.00 | 50,000.00 | **blocked**, `ApprovalPolicyGap` — must not auto-approve |
| R6 | VO Add +500,000.00 exactly | 500,000.00 | `[PM, ProjectDirector]` (Min inclusive, Max exclusive) |

Policy `TH-Default-IPC`: step 1 `QS` [0,∞) · step 2 `PM` [0,∞) · step 3 `ProjectDirector` [10M,∞).

| # | Input | Expected |
| --- | --- | --- |
| R7 | Certificate gross 21,600,000.00 (= fixture P1) | `[QS, PM, ProjectDirector]` |
| R8 | Certificate gross 5,000,000.00 | `[QS, PM]` |
| R9 | R7 approved by QS, returned by PM, resubmitted at gross 9,000,000.00 | `RevisionNo = 2`; QS approval **voided**; new chain `[QS, PM]` from step 1 |
| R10 | R8 where the QS is also the submitter, `AllowSelfApproval = 0` | approve rejected, `SelfApprovalNotPermitted` |

### 3.6 Effects on approval

**VO → `Approved`:** $BAC_{new} = BAC_{old} + Amount$ (signed) · `ContractValue` moves by the same
signed amount, which **shifts the retention cap ceiling $R^{max}$ and the advance base** (§2) ·
S-Curve rebaselines from `ApprovedAt`, historical points never rewritten · new activities become
schedulable, CPM re-runs. A Deduct VO can drop `ContractValue` below retention already held — the
$\max(0,\cdot)$ in $R_k$ ensures retention is **never clawed back automatically**.

**Certificate → `Certified`:** posts the retention accrual and advance recovery to
`ProjectFinanceLedger`; certificate becomes immutable and printable. → `Paid`: records the receipt.

---

## 4. Open questions for the human — [ต้องยืนยัน]

| # | Question | Recommended answer if no response |
| --- | --- | --- |
| 1 | Is `Project.ContractValue` distinct from `BAC`, or are they always equal in CM+? | Add the field, default it to `BAC` |
| 2 | Retention config for the pilot project: Thai standard (5%, no cap) or FIDIC (10% capped at 5%)? The prototype's "5% / cap 10%" is unreachable and must be corrected. | Thai standard: `RetentionRate = 5.00`, `RetentionCapPercentage = NULL` |
| 3 | VAT 7% / WHT 3% treatment (§2.7) — three sub-questions | Keep the core formula pre-tax; add an optional tax block later |
| 4 | Route IPC approvals on gross certified $G_k$ or net payment $N_k$? | Gross $G_k$ |
| 5 | Cumulative-VO escalation threshold — is 10% right, and does it reset after a formal contract amendment? | 10.00%, no reset until `ContractValue` is amended |
| 6 | May an intermediate approver reject outright, or only return for revision? | Return for revision only |
| 7 | Is `ProjectDirector` a real role to add to `User.Role`? docs/9. §4 lists PM/Planning/Site/QS/Executive/Admin only, but a 3-tier VO matrix needs it. | Add it |
| 8 | The actual P6 Earned Value setting of the source project, before any golden-file EAC test is signed off | Set CM+ to `CpiBased` and P6 to `PF = 1/CPI` for the comparison |
| 9 | Do external parties (Employer's rep / consultant engineer) approve VOs or IPCs? | No; if yes, an "external approval recorded on behalf of" action type is required |

---

## 5. Follow-ups for other agents

- `system-architect`: turn §1.7, §2.6 and §3.3 into `docs/specs/master-plan/design.md` + ADRs
  (suggest: *ADR-0007 EAC selectable variants via unified Performance Factor*,
  *ADR-0008 Configurable approval matrix with amount tiering*).
- `database-engineer`: new/changed columns in §2.6 and the three new tables
  (`ApprovalPolicy`, `ApprovalPolicyRule`, `ApprovalAction`, `ProjectFinanceLedger`) — all
  `TenantId`-scoped per ADR-0002.
- `qa-engineer`: fixtures A/D/E–I (§1.4–1.5 + evm-formulas.md), P1–P7 (§2.5), R1–R10 (§3.5) are
  written to be transcribed directly into xUnit theories.
- `frontend-developer`: add the advance-recovery line and retention-cap indicator to the Payment
  Certificate panel; EVM screen gets an EAC variant switcher fed by the all-variants response.
- `knowledge-curator`: after `system-architect` signs off, promote §2 into a new
  `.claude/knowledge/domain/payment-retention.md` and register it in `INDEX.md`
  (§1 and §3 already live in `evm-formulas.md` and `approval-workflow.md`).
