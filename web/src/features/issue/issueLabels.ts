import type { IssueLogDto, IssueStatus } from './types'

/** Thai display text for `IssueStatus` — matches the prototype's own wording
 * (`docs/ECM Planning Prototype.dc.html` ~line 498-500: "เปิดอยู่ (Open)" / "กำลังแก้ไข" / "ปิดแล้ว"). */
export const ISSUE_STATUS_LABELS: Record<IssueStatus, string> = {
  Open: 'เปิดอยู่',
  Doing: 'กำลังแก้ไข',
  Closed: 'ปิดแล้ว',
}

/** `sequenceNo` (presentational, oldest = 1 — see `types.ts`'s remarks) formatted to match the
 * prototype's "ISS-024" style numbering. `null` (a just-created/just-advanced row that has not been
 * reloaded from the list yet) renders as an honest "—", never a fabricated number. */
export function formatIssueCode(sequenceNo: number | null): string {
  return sequenceNo === null ? '—' : `ISS-${String(sequenceNo).padStart(3, '0')}`
}

export interface IssueNextAction {
  /** `null` when `status === 'Closed'` — there is no next action, ever (terminal, no reopen). */
  label: string | null
}

/** The prototype's own `nextLabel`/`canNext` logic (`docs/ECM Planning Prototype.dc.html`
 * ~line 860), reproduced exactly: `Open -> "เริ่มแก้ไข →"`, `Doing -> "ปิดปัญหา ✓"`,
 * `Closed -> null` (no button; `IssueTable.tsx` shows "ปิดเมื่อ {date}" instead). Skipping `Doing`
 * is not permitted (domain-rules.md §9.1) — `advance-status` always moves exactly one rung, so this
 * table never offers an "Open -> Closed" shortcut. */
export function nextIssueActionLabel(status: IssueStatus): string | null {
  if (status === 'Open') return 'เริ่มแก้ไข →'
  if (status === 'Doing') return 'ปิดปัญหา ✓'
  return null
}

const DATE_FORMATTER = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeZone: 'Asia/Bangkok' })
const DATE_TIME_FORMATTER = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})

export function formatIssueDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_FORMATTER.format(parsed)
}

export function formatIssueDateTime(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_TIME_FORMATTER.format(parsed)
}

/** `<input type="date">` value -> UTC-midnight ISO instant — mirrors
 * `features/info/ProjectEditForm.tsx#toRequestDate` / `features/weather/weatherLabels.ts`'s
 * identical convention. `""` (no due date chosen) becomes `null`, never an invalid-date string. */
export function toIssueRequestDate(dateInputValue: string): string | null {
  return dateInputValue.trim() === '' ? null : new Date(`${dateInputValue}T00:00:00Z`).toISOString()
}

/** Case-insensitive substring match over title/detail/owner — **client-side only**, over the
 * already-fully-loaded `items` (`types.ts`'s remarks: the backend does not paginate/filter this list
 * yet). Never claims to be a server-side search. */
export function searchIssues(items: IssueLogDto[], query: string): IssueLogDto[] {
  const trimmed = query.trim().toLowerCase()
  if (!trimmed) return items
  return items.filter((issue) =>
    [issue.title, issue.detail, issue.owner].some((field) => field?.toLowerCase().includes(trimmed)),
  )
}
