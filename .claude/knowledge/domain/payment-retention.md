# Payment Certificate — Retention & Advance (canonical reference, with test fixtures)

Source: `docs/specs/master-plan/domain-decisions.md` §2, confirmed by the human 2026-07-27
(closes docs/9. §11 item 3: retention/advance rate is **per-project configurable**, not a fixed
constant). Signed off by `system-architect` in `docs/specs/master-plan/design.md` §3/§4 (field
names carried through unchanged — see `docs/10.` §3 for the landing migration). Scope is the
money math only; approval routing and state machines for Payment Certificates live in
[approval-workflow.md](approval-workflow.md).

⚠️ **Everything in this file is money *in*** — certified/received, at contract price, including the
contractor's margin. It is **never** Actual Cost: `PaymentCertificate` and `ProjectFinanceLedger`
must never feed $AC$/ACWP. Money *out* is a separate ledger, ruled on in
[actual-cost.md](actual-cost.md) §5, which quantifies the damage of conflating them (the same
project reads $CPI = 1.0504$ on cash received versus $0.7716$ on real cost — the substitution flips
the sign of $CV$).

Precision: money `decimal(18,2)`, percent `decimal(5,2)`. Round **each line separately**,
**half-away-from-zero**, then subtract — never compute $N_k$ at full precision and round once.
⚠️ **.NET trap:** `Math.Round(x, 2)` defaults to banker's rounding; pass
`MidpointRounding.AwayFromZero` explicitly or CM+ diverges from SQL Server `ROUND()` and from the
printed Thai certificate (see fixture P7).

## 1. Corrected Net Payment formula

docs/9. §5's sketch was wrong in three ways: it deducted on cumulative rather than period value
(double-deducts partially certified milestones), used `progress%` instead of certified
`ApprovePct`, and had no retention cap. Corrected, for certificate $k$ against milestone $m$
(value $M_m$):

$$G^{cum}_k = M_m \times \frac{p^{app}_k}{100} \qquad G_k = G^{cum}_k - G^{cum}_{k-1}$$

$$R_k = \max\!\Big(0,\ \min\big(\tfrac{r}{100} G_k,\ \ R^{max} - R^{cum}_{k-1}\big)\Big)
\qquad R^{max} = \tfrac{c}{100}\,C \ \ (=\infty \text{ if } c \text{ is null})$$

$$D_k = \max\!\Big(0,\ \min\big(\tfrac{a}{100} G_k,\ \ A - D^{cum}_{k-1},\ \ G_k - R_k\big)\Big)$$

$$N_k = G_k - R_k - D_k$$

| Symbol | Meaning | Source |
| --- | --- | --- |
| $M_m$ | Milestone value (THB) | `PaymentCertificate.MilestoneValue` |
| $p^{app}_k$ | Cumulative certified % for the milestone | `PaymentCertificate.ApprovePct` |
| $G_k$ | Gross certified value **this period** | `PaymentCertificate.GrossCertifiedAmount` |
| $r$ | Retention rate (%) | `Project.RetentionRate` |
| $c$ | Retention cap (% of contract), nullable | `Project.RetentionCapPercentage` |
| $C$ | Contract value incl. approved VOs | `Project.ContractValue` |
| $R^{cum}_{k-1}$ | Retention held before this certificate | `ProjectFinanceLedger` sum (§4) |
| $a$ | Advance rate (%) | `Project.AdvanceRate` |
| $A$ | Advance actually disbursed | `Project.AdvanceAmountPaid` (default $\tfrac{a}{100}C$) |
| $D^{cum}_{k-1}$ | Advance recovered before this certificate | `ProjectFinanceLedger` sum (§4) |
| $N_k$ | Net certified amount this period (pre-tax) | `PaymentCertificate.NetPayment` |

Notes:
- Deductions are computed on the **period** gross $G_k$, never the cumulative — see fixture P2.
- The third term of $D_k$ ($G_k - R_k$) guarantees $N_k \ge 0$; the unrecovered advance balance
  carries to the next certificate (only binds when $a + r > 100$).
- No division anywhere in the formula — the only guards are monotonicity
  ($p^{app}_k \ge p^{app}_{k-1}$) and the $\max(0,\cdot)$ clamps.

## 2. Retention cap — data, not a constant

`Project.RetentionCapPercentage decimal(5,2) NULL` (null = uncapped). Both patterns below are in
scope for CM+'s user base, so the cap must be configurable per project:

- **Thai standard construction contract** (พ.ร.บ.การจัดซื้อจัดจ้างฯ 2560): withholds 5% of each
  installment as เงินประกันผลงาน with no separate ceiling — cap is implicitly 5% of contract,
  reached only at completion. Model as `RetentionCapPercentage = NULL`. (หลักประกันสัญญา 5% is a
  *separate* performance bond — never conflate it with retention in the data model.)
- **FIDIC Red Book Sub-Clause 14.3(c):** commonly 10% deducted per IPC, capped at 5% of the
  Accepted Contract Amount — the cap **binds hard at 50% completion**; without the field CM+
  over-deducts every certificate after that point (fixture P3).
- Retention accrues against **contract value**, not `BAC` (budget) — hence the new
  `Project.ContractValue`, defaulting to `BAC`. **[ต้องยืนยัน]** if the human confirms budget ≡
  contract sum always, the cap may reference `BAC` directly instead.
- ⚠️ Prototype inconsistency (`docs/ECM Planning Prototype.dc.html`): shows "Retention 5% (เพดาน
  10% ของสัญญา)" — a 5%-per-payment rate can never accumulate past 5%, so a 10% cap is
  unreachable. Either the rate should be 10% (FIDIC pairing) or the cap should be blank (Thai
  standard); flagged to `po-analyst`/human, not yet resolved.

## 3. Advance recovery methods

`Project.AdvanceRecoveryMethod enum { ProRata, ThresholdBanded, Manual }`, default `ProRata`.

- **`ProRata`** (default, Thai practice): $D_k = \tfrac{a}{100}G_k$, capped by outstanding
  balance. Self-consistent: with $A = \tfrac{a}{100}C$, fully recovered exactly when cumulative
  gross reaches $C$.
- **`ThresholdBanded`** (FIDIC 14.2): recovery starts only after cumulative certification passes
  start threshold $s$ (% of $C$), amortises at rate $\rho$ of the excess, forced full recovery
  once cumulative reaches $e$ (% of $C$):
  $$D_k = \max\!\Big(0,\ \min\big(\tfrac{\rho}{100}\max(0,\ G^{cum}_k - \tfrac{s}{100}C) - D^{cum}_{k-1},\ \ A - D^{cum}_{k-1}\big)\Big)$$
  FIDIC defaults $s=10$, $\rho=25$, $e=90$. Needs `Project.AdvanceRecoveryStartPct` /
  `RatePct` / `EndPct` (nullable, used only by this method).
- **`Manual`:** QS enters $D_k$ per certificate, still capped by outstanding balance.

## 4. Retention release & the ledger

$$\text{Release}_1 = \tfrac{\rho_1}{100} R^{cum} \text{ at substantial completion}
\qquad \text{Release}_2 = R^{cum} - \text{Release}_1 \text{ at end of DLP}$$

Common value $\rho_1 = 50.00$ (`Project.RetentionRelease1Percentage`, default 50.00);
`Project.DefectsLiabilityMonths` drives Release₂ timing. Some contracts release 100% early
against a bank guarantee — model as an `Adjustment` ledger entry, never a formula branch.

Held/recovered balances are a **ledger**, not a recomputation, so they are audit-provable and the
cap check is trivial:

```
ProjectFinanceLedger   -- append-only; no update/delete path (ADR-pending, see design.md §3)
  Id, TenantId, ProjectId, PaymentCertificateId NULL
  Category  enum { Retention, Advance }
  EntryType enum { Accrual, Release, Disbursement, Recovery, Adjustment }
  Amount    decimal(18,2)      -- signed; R^cum = SUM(Amount) WHERE Category = Retention
  EffectiveDate DateTimeOffset, Reference, Note
```

Written when a certificate reaches `Certified` (accrual/recovery) and on release events. Index
`(TenantId, ProjectId, Category)` — the `SUM()` must seek (see `docs/10.` §3).

## 5. Field reference (accepted; landing migration in `docs/10.` §3)

```
Project.RetentionRate                      decimal(5,2)   -- existing
Project.AdvanceRate                        decimal(5,2)   -- existing
Project.ContractValue                      decimal(18,2)  -- defaults to BAC   [ต้องยืนยัน §2]
Project.RetentionCapPercentage             decimal(5,2) NULL   -- null = uncapped
Project.RetentionRelease1Percentage        decimal(5,2) default 50.00
Project.DefectsLiabilityMonths             int NULL
Project.AdvanceAmountPaid                  decimal(18,2) NULL  -- actual disbursement
Project.AdvanceRecoveryMethod              enum default ProRata
Project.AdvanceRecoveryStartPct/RatePct/EndPct   decimal(5,2) NULL  -- ThresholdBanded only

PaymentCertificate.GrossCertifiedAmount        decimal(18,2)  -- also the approval routing amount, §5.1 of approval-workflow.md
PaymentCertificate.AdvanceRecoveryAmount       decimal(18,2)  -- without it the printed certificate cannot foot
PaymentCertificate.PreviousCumulativeApprovePct decimal(5,2)  -- or derive from prior certificate
PaymentCertificate.RevisionNo                  int
```

⚠️ The prototype's Payment Certificate panel shows only *Milestone Value → Retention → Net* with
**no advance-recovery line** — with `AdvanceRate = 10%` its Net Payment is overstated by 10% of
gross. `frontend-developer` must add the advance-recovery line and a retention-cap indicator
(tracked in `docs/specs/master-plan/design.md` §4, `payment/` row).

## 6. Worked fixtures (unit-test fixtures, transcribe verbatim into xUnit theories)

**P1 — normal period, Thai config (no cap).**
$C=485{,}000{,}000.00$; $r=5.00$; $c=$ null; $a=10.00 \Rightarrow A=48{,}500{,}000.00$;
$R^{cum}_{k-1}=11{,}900{,}000.00$; $D^{cum}_{k-1}=23{,}800{,}000.00$; $M=21{,}600{,}000.00$;
$p^{app}=100.00$ (was 0).
→ $G_k=21{,}600{,}000.00$ · $R_k=1{,}080{,}000.00$ · $D_k=2{,}160{,}000.00$ ·
**$N_k = 18{,}360{,}000.00$**

**P2 — partial certification (proves period-vs-cumulative).**
$M=10{,}000{,}000.00$; prior $p^{app}=40.00$; this period $p^{app}=65.00$; $r=5.00$, $a=10.00$,
no caps binding.
→ $G_k=2{,}500{,}000.00$ · $R_k=125{,}000.00$ · $D_k=250{,}000.00$ · **$N_k = 2{,}125{,}000.00$**
*(Deducting on the cumulative 6,500,000 instead would over-deduct retention/advance by 200,000/400,000.)*

**P3 — FIDIC cap bites (fails without `RetentionCapPercentage`).**
$C=100{,}000{,}000.00$; $r=10.00$; $c=5.00 \Rightarrow R^{max}=5{,}000{,}000.00$;
$R^{cum}_{k-1}=4{,}500{,}000.00$; $a=10.00$, $A=10{,}000{,}000.00$, $D^{cum}_{k-1}=4{,}500{,}000.00$;
$G_k=8{,}000{,}000.00$.
→ uncapped retention 800,000.00 but headroom only 500,000.00 → $R_k=500{,}000.00$;
$D_k=800{,}000.00$ → **$N_k = 6{,}700{,}000.00$**. Without cap logic $N_k$ would wrongly be
6,400,000.00 — a 300,000.00 under-payment.

**P3b — next period, cap fully consumed.** $G_k=5{,}000{,}000.00$, $R^{cum}=5{,}000{,}000.00$
→ $R_k=\mathbf{0.00}$; $D_k=500{,}000.00$; **$N_k = 4{,}500{,}000.00$**.

**P4 — advance nearly exhausted.** $A=10{,}000{,}000.00$, $D^{cum}_{k-1}=9{,}960{,}000.00$,
$G_k=1{,}000{,}000.00$, $r=5.00$, $a=10.00$.
→ $R_k=50{,}000.00$; uncapped $D$ 100,000.00 but only 40,000.00 outstanding → $D_k=40{,}000.00$;
**$N_k = 910{,}000.00$**.

**P5 — edge: nothing certified.** $p^{app}_k = p^{app}_{k-1}$ (or certificate returned) →
$G_k=R_k=D_k=$ **$N_k = 0.00$**. No division anywhere.

**P6 — `ThresholdBanded` advance recovery (FIDIC).** $C=100{,}000{,}000.00$, $A=10{,}000{,}000.00$,
$s=10$, $\rho=25$, $e=90$:

| Cert | $G^{cum}_k$ | Excess over $sC$ | Target $D^{cum}$ | $D_k$ |
| --- | --- | --- | --- | --- |
| 1 | 8,000,000.00 | 0 | 0.00 | **0.00** |
| 2 | 15,000,000.00 | 5,000,000.00 | 1,250,000.00 | **1,250,000.00** |
| 3 | 25,000,000.00 | 15,000,000.00 | 3,750,000.00 | **2,500,000.00** |
| … | 50,000,000.00 | 40,000,000.00 | 10,000,000.00 = $A$ | advance fully recovered |

**P7 — rounding determinism (the banker's-rounding trap).** $G_k=1{,}000{,}000.10$, $r=5.00$,
$a=10.00$ → $R_k = 50{,}000.005 \to \mathbf{50{,}000.01}$ (half-away-from-zero); $D_k =
\mathbf{100{,}000.01}$; **$N_k = 850{,}000.08$**. Banker's rounding wrongly yields 850,000.09 —
this fixture exists specifically to catch that regression.

## 7. Open questions for the human — [ต้องยืนยัน]

1. **VAT 7% / WHT 3%:** is VAT charged on gross or net-of-retention? Is WHT on gross or net? Is
   VAT on the retained amount accounted at certification or at release? *Recommendation until
   answered:* keep the core formula above tax-free; model tax as a separate optional block
   (`VatAmount`, `WithholdingTaxAmount`, `NetPayableAmount`) added later without touching the
   certified-value math. Ask the client's QS/accounting before Sprint 9.
2. **`ContractValue` vs `BAC`:** are they ever distinct in CM+, or always equal? Default answer:
   add the field, default it to `BAC`.
3. **Pilot project retention config:** Thai standard (5%, no cap) or FIDIC (10%, capped at 5%)?
   Default answer: Thai standard.
4. IPC approval routing amount (gross $G_k$ vs net $N_k$) is an approval-workflow question, not a
   money-math one — tracked in [approval-workflow.md](approval-workflow.md) §9 item 1.
