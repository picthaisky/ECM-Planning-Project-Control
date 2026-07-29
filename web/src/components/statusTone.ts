import type { StatusPillTone } from './StatusPill'

const STATUS_TONE_MAP: Record<string, StatusPillTone> = {
  // Issue / Action Log
  open: 'danger',
  doing: 'warning',
  closed: 'success',
  // Approval workflows (VO, Payment Certificate)
  approved: 'success',
  rejected: 'danger',
  pending: 'warning',
  // File import jobs (S3-FE-01, FileImportJob.Status)
  succeeded: 'success',
  failed: 'danger',
}

/** Map a raw status keyword (case-insensitive) to a design-token tone — never pick an ad hoc color per status. */
export function resolveStatusTone(status: string): StatusPillTone {
  return STATUS_TONE_MAP[status.trim().toLowerCase()] ?? 'neutral'
}
