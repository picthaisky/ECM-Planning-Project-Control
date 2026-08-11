import { roundHalfAwayFromZero } from '../../utils/format'
import { calculateRetentionCapAmount } from '../info/retentionCap'

/**
 * Retention-cap *indicator* for the S9-FE-01 certificate panel (DoD: "Retention (พร้อม cap ที่เหลือ)").
 *
 * What this can and cannot honestly show: `PaymentCertificateDto` carries `RetentionAmount` —
 * already the backend's cap-aware $R_k$ (`CertificateCalculator`, payment-retention.md §1: $R_k =
 * \max(0, \min(\frac{r}{100}G_k,\ R^{max} - R^{cum}_{k-1}))$) — but *not* the project's retention
 * rate/cap/contract value, and there is no endpoint anywhere that exposes the *cumulative* retention
 * held so far ($R^{cum}_{k-1}$ — `IProjectFinanceLedgerReader` has no controller). So this module can
 * compute and show the **cap ceiling itself** ($R^{max}$, reusing `features/info/retentionCap.ts`'s
 * already-accepted formula verbatim rather than recomputing it a second way) and can *infer* whether
 * the cap (or some other clamp) reduced *this period's* retention below the naive
 * $\frac{r}{100}G_k$ figure — but it cannot show a running "remaining headroom after this
 * certificate" balance, because $R^{cum}_{k-1}$ is not derivable from any contract this frontend can
 * call. Labelled accordingly in `components/CertificatePanel.tsx` (a project-wide ceiling + a
 * per-period "is it binding" signal, never a fabricated running balance) — see this sprint's
 * frontend report for the gap.
 */
export type RetentionCapStatus =
  /** The project's retention/contract data has not loaded yet — render nothing definitive. */
  | { kind: 'unknown' }
  /** `Project.RetentionCapPercentage` is `null` (Thai-standard contract, payment-retention.md §2). */
  | { kind: 'uncapped' }
  | {
      kind: 'capped'
      /** $R^{max} = \frac{c}{100}C$ — the project-wide ceiling, not a per-certificate figure. */
      capAmount: number
      /** `true` when this certificate's actual `RetentionAmount` is below the naive
       * $\frac{r}{100}G_k$ figure — i.e. the cap (or another clamp) is binding *this period*.
       * `false` (not merely "no") whenever the rate is unknown, since binding cannot be determined
       * from unknowns — never treated the same as "confirmed not binding" by callers that also
       * check `retentionRatePct !== null`. */
      isBindingThisPeriod: boolean
    }

export interface RetentionCapInput {
  /** $G_k$ — this certificate's period gross (`PaymentCertificateDto.grossCertifiedAmount`). */
  grossCertifiedAmount: number
  /** $R_k$ — this certificate's actual, already cap-aware retention (`PaymentCertificateDto.retentionAmount`). */
  actualRetentionAmount: number
  /** $r$ — `Project.RetentionRate`, `null` if the project has not loaded or the rate is unset. */
  retentionRatePct: number | null
  /** $c$ — `Project.RetentionCapPercentage`, `null` = uncapped. */
  retentionCapPercentage: number | null
  /** $C$ — `Project.ContractValue`, `null` only while the project has not loaded yet. */
  contractValue: number | null
}

/** Rounds like every other money figure in this codebase — half-away-from-zero to 2dp
 * (`.claude/knowledge/domain/payment-retention.md` fixture P7's banker's-rounding trap). */
export function resolveRetentionCapStatus(input: RetentionCapInput): RetentionCapStatus {
  const { grossCertifiedAmount, actualRetentionAmount, retentionRatePct, retentionCapPercentage, contractValue } =
    input

  if (contractValue === null) return { kind: 'unknown' }
  if (retentionCapPercentage === null) return { kind: 'uncapped' }

  const capAmount = calculateRetentionCapAmount(contractValue, retentionCapPercentage)
  if (capAmount === null) return { kind: 'uncapped' } // unreachable given the guard above; keeps types honest

  const nominalRetention =
    retentionRatePct === null ? null : roundHalfAwayFromZero((retentionRatePct / 100) * grossCertifiedAmount, 2)

  const isBindingThisPeriod =
    nominalRetention !== null && roundHalfAwayFromZero(actualRetentionAmount, 2) < nominalRetention

  return { kind: 'capped', capAmount, isBindingThisPeriod }
}
