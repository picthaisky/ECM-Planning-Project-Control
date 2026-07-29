/**
 * CM+ Project Control — design tokens (navy/gold theme).
 *
 * Single source of truth for every color, font and radius used across the app.
 * Components must NEVER hardcode hex values — either use the matching Tailwind
 * utility class (generated from this same object via `tailwind.config.ts`) or
 * import a constant from here for cases Tailwind classes can't reach (inline
 * SVG/canvas drawing, e.g. Gantt bars, chart lines).
 *
 * Source of truth: docs/ECM Planning Prototype.dc.html (ADR-0006), transcribed
 * via the `/cmplus-ui` skill token table. Do not edit values here without
 * updating the skill and vice versa.
 */

/** Brand / status color tokens. */
export const colors = {
  /** Sidebar bg, headings, primary buttons, dark accents, data-date line on charts. */
  navy: '#0F2542',
  /** Brand mark, active nav item, EV line, baseline bar, "active/starred" indicators. */
  gold: '#C9A227',
  /** App/page background. */
  bg: '#F4F5F7',
  /** Card/table background. */
  surface: '#FFFFFF',
  /** DataTable header row background (distinct from `surface`/`bg` for
   *  card-nested contrast) — `/cmplus-ui` "Data table" pattern. */
  tableHeaderBg: '#F7F8FA',
  /** Card/table borders. */
  border: '#E3E5EA',
  /** Row dividers inside tables. */
  borderSubtle: '#F0F1F4',
  /** Primary text. */
  text: '#1A2433',
  /** Secondary values (table cells). */
  textMuted: '#5A6472',
  /** Labels, captions, placeholders. */
  textFaint: '#8A8F99',
  /** Approved, under budget, CPI >= 1, closed status. */
  success: '#1F7A4D',
  /** Critical path, over budget/behind schedule, open/urgent status, AC line. */
  danger: '#B23A3A',
  /** Non-critical Gantt bars, MS Project icon, forecast/EAC dashed line. */
  secondary: '#33507A',
  /** Pending/in-progress status text (pairs with a light amber chip background). */
  warningText: '#9A7B1B',
} as const satisfies Record<string, `#${string}`>

export type ColorToken = keyof typeof colors

/** Font stacks. Never use Inter. */
export const fontFamily = {
  /** Body/UI text. */
  body: ['"IBM Plex Sans Thai"', 'system-ui', 'sans-serif'],
  /** Headings, stat-tile numbers, and numeric emphasis (weights 500-700). */
  heading: ['"Bai Jamjuree"', 'system-ui', 'sans-serif'],
} as const

/** Shared shape tokens. */
export const radius = {
  /** Card/table corner radius — flat design, no heavy shadows. */
  card: '8px',
} as const

export const theme = { colors, fontFamily, radius } as const

export default theme
