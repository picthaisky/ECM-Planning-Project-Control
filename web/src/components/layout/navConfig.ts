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
 * S8-FE-01/02 — `payment`/`weather`/`photo` (linked from Dashboard's KPI tiles/photo strip) are
 * deliberately *not* added yet: Sprint 9/12's backends they'd need don't exist. */
export const IMPLEMENTED_SCREENS: ReadonlySet<ScreenId> = new Set([
  'dashboard',
  'info',
  'wbs',
  'gantt',
  'evm',
  'cash',
])
