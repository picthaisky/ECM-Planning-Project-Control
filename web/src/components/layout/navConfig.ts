/**
 * The canonical 13-screen nav (S4-FE-01, US-4.2, ADR-0006) — transcribed verbatim, in order, from
 * `docs/ECM Planning Prototype.dc.html` / `docs/ECM Planning Prototype (Standalone).html`'s own
 * `pages` array (`[id, label]` pairs feeding `navRows`), not re-derived: `['dashboard',
 * 'Executive Dashboard'], ['info', 'ข้อมูลโครงการ'], ['wbs', 'WBS & Activity'], ['gantt', 'Gantt /
 * CPM'], ['evm', 'EVM S-Curve'], ['cash', 'Cash Flow'], ['payment', 'Payment Certificate'],
 * ['photo', 'Photo Progress'], ['vo', 'Variation Order'], ['issue', 'Issue / Action Log'],
 * ['weather', 'Weather Log'], ['maneq', 'Man / Equipment'], ['baseline', 'Baseline']`.
 *
 * Do not reorder or rename without re-checking the prototype file directly (`.claude/knowledge`
 * lessons/lessons-learned.md 2026-07-12: the prototype is ground truth, prose docs are historical
 * intent) — this is also cross-referenced in `.claude/knowledge/domain/modules-map.md` and the
 * `/cmplus-ui` skill's "Screen / navigation structure" section, kept in sync with this file.
 */
export type ScreenId =
  | 'dashboard'
  | 'info'
  | 'wbs'
  | 'gantt'
  | 'evm'
  | 'cash'
  | 'payment'
  | 'photo'
  | 'vo'
  | 'issue'
  | 'weather'
  | 'maneq'
  | 'baseline'

export interface NavEntry {
  id: ScreenId
  /** Route path segment under `/app/:projectId/<path>`. Same as `id` for every screen — kept as
   * a distinct field (rather than reusing `id` inline at each call site) so a future screen whose
   * route segment must differ from its id (unlikely, but see e.g. `vo` vs a hypothetical `/vo`
   * vs `/variation-orders`) is a one-line change here, not a hunt across `routes/`. */
  path: string
  /** Exact label text from the prototype — do not translate/shorten. */
  label: string
}

export const NAV_ENTRIES: readonly NavEntry[] = [
  { id: 'dashboard', path: 'dashboard', label: 'Executive Dashboard' },
  { id: 'info', path: 'info', label: 'ข้อมูลโครงการ' },
  { id: 'wbs', path: 'wbs', label: 'WBS & Activity' },
  { id: 'gantt', path: 'gantt', label: 'Gantt / CPM' },
  { id: 'evm', path: 'evm', label: 'EVM S-Curve' },
  { id: 'cash', path: 'cash', label: 'Cash Flow' },
  { id: 'payment', path: 'payment', label: 'Payment Certificate' },
  { id: 'photo', path: 'photo', label: 'Photo Progress' },
  { id: 'vo', path: 'vo', label: 'Variation Order' },
  { id: 'issue', path: 'issue', label: 'Issue / Action Log' },
  { id: 'weather', path: 'weather', label: 'Weather Log' },
  { id: 'maneq', path: 'maneq', label: 'Man / Equipment' },
  { id: 'baseline', path: 'baseline', label: 'Baseline' },
] as const

/** Screens with a real, working implementation so far. Every other nav entry still routes
 * (S4-FE-01 DoD: "build the shell/routing structure now so later sprints just add screen
 * content") but renders `ScreenPlaceholder` until its own sprint lands. `dashboard`/`cash` added
 * S8-FE-01/02; `payment` added S9-FE-01/02 (the milestone list + certificate panel load against
 * two endpoints that do not exist on the real backend yet — see `features/payment/api.ts`'s
 * remarks — but every approval-chain mutation is real and live). `vo` added S10-FE-01/02 — the VO
 * register, create/submit/approve chain, and content edit are all real, live endpoints (unlike
 * Payment's own list/get gap); only the approval-action history read 404s today, for a different,
 * narrower reason (`features/vo/api.ts#getVoApprovalActions`'s remarks). `weather`/`issue` added
 * S11-FE-01 (US-11.1/US-11.2) — the weather-log register/correction-chain/EOT evaluation
 * (`web/src/features/weather/`) and the issue/action log with its Open/Doing/Closed tile counts
 * (`web/src/features/issue/`) are all real, live endpoints. `photo`/`maneq` added S12-FE-01/02
 * (US-12.1/US-12.2) — offline photo capture + the IndexedDB outbox (`web/src/features/photo/`,
 * `web/src/services/outbox/`) and the Man/Equipment KPI/histogram/PI screen
 * (`web/src/features/maneq/`); both real, live endpoints, with the specific list/catalogue gaps on
 * the Sprint 12 backend documented at each feature's own call sites (`features/photo/PhotoPage.tsx`,
 * `features/maneq/ManeqPage.tsx`). `baseline` added S14-FE-01 (US-14.1/US-14.2) — capture/activate
 * and the current-vs-baseline comparison table (`web/src/features/baseline/`); capture/activate/
 * compare are all real, live endpoints, with the missing list-baselines gap documented at
 * `features/baseline/api.ts#listBaselines`'s own remarks. */
export const IMPLEMENTED_SCREENS: ReadonlySet<ScreenId> = new Set([
  'dashboard',
  'info',
  'wbs',
  'gantt',
  'evm',
  'cash',
  'payment',
  'photo',
  'vo',
  'weather',
  'maneq',
  'issue',
  'baseline',
])
