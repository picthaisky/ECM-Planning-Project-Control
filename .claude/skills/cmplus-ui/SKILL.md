---
name: cmplus-ui
description: CM+ frontend design system and React patterns — navy/gold professional theme tokens (from the working prototype), Tailwind config, 13-screen nav structure, table/card/Gantt component patterns, and PWA offline-first recipes. Load before writing or reviewing any frontend code or UI design.
---

# CM+ Frontend: Design System & React 19 Patterns

**Source of truth:** `docs/ECM Planning Prototype.dc.html` (editable source) and
`docs/ECM Planning Prototype (Standalone).html` (static export). These supersede the
"warm orange-brown" narrative in `docs/3.` — that doc predates the working prototype;
treat the prototype as authoritative for every visual/UX decision (see ADR-0006).

## Theme tokens (navy/gold professional)

| Token | Hex | Use |
| --- | --- | --- |
| `navy` | `#0F2542` | Sidebar bg, headings, primary buttons, dark accents, data-date line on charts |
| `gold` | `#C9A227` | Brand mark, active nav item, EV line, baseline bar, "active/starred" indicators |
| `bg` | `#F4F5F7` | App/page background |
| `surface` | `#fff` | Card/table background |
| `border` | `#E3E5EA` | Card/table borders |
| `border-subtle` | `#F0F1F4` | Row dividers inside tables |
| `text` | `#1A2433` | Primary text |
| `text-muted` | `#5A6472` | Secondary values (table cells) |
| `text-faint` | `#8A8F99` | Labels, captions, placeholders |
| `success` | `#1F7A4D` | Approved, under budget, CPI ≥ 1, closed status |
| `danger` | `#B23A3A` | Critical path, over budget/behind schedule, open/urgent status, AC line |
| `secondary` | `#33507A` | Non-critical Gantt bars, MS Project icon, forecast/EAC dashed line |
| `warning-text` | `#9A7B1B` | Pending/in-progress status text (paired with a light amber chip bg) |

Fonts: **IBM Plex Sans Thai** for body/UI text; **Bai Jamjuree** (weights 500–700) for
headings, stat-tile numbers, and anything numeric that needs emphasis. Never use Inter.

Card shape: `border-radius: 8px`, `1px solid` border token, no heavy shadows (flat, clean —
the prototype does not use glassmorphism or soft shadows).

## Screen / navigation structure (13 screens, in nav order)

1. Executive Dashboard (`dashboard`) — KPI tiles + S-Curve preview + critical-path preview + WBS rollup + latest photos
2. ข้อมูลโครงการ / Project Info (`info`) — project master data + file import panel + import history
3. WBS & Activity (`wbs`) — collapsible tree, weight/duration/progress/status columns
4. Gantt / CPM (`gantt`) — critical (red) vs non-critical (slate-blue) bars, gold baseline bar underneath, data-date gold vertical line, white translucent progress overlay
5. EVM S-Curve (`evm`) — PV/EV/AC/EAC chart + 12-tile metric grid (BAC, PV, EV, AC, SV, CV, SPI, CPI, EAC, ETC, VAC, %Complete)
6. Cash Flow (`cash`) — per-period bar chart (actual vs forecast bars) + cumulative summary tiles (received, AC, retention, net cash position)
7. Payment Certificate (`payment`) — milestone table (left) + certificate detail panel (right, sticky) with retention/net-payment breakdown
8. Photo Progress (`photo`) — zone filter chips + 4-column photo grid bound to activity code
9. Variation Order (`vo`) — approved/pending/BAC-adjusted summary tiles + table with inline approve/reject actions
10. Issue / Action Log (`issue`) — open/doing/closed counters + status-advance table
11. Weather Log (`weather`) — rainfall/EOT counters + tomorrow's forecast warning card + daily log table
12. Man / Equipment (`maneq`) — daily labor/equipment counters + productivity index + 7-day histogram
13. Baseline (`baseline`) — baseline list with activate/star action + current-vs-baseline delta tiles

Reconcile against `docs/6.`'s 15-module list: the prototype folds **Executive Summary**
into the Dashboard and does not show a standalone **Daily/Weekly Progress** screen (progress
entry happens inline via WBS rows / Photo Progress / Issue log). If a spec calls for those as
separate screens, flag it to `po-analyst` as a scope question — don't silently add or drop
screens. See `.claude/knowledge/domain/modules-map.md`.

## Component patterns (copy these, don't reinvent)

- **Stat tile:** white card, label `11px` `text-faint`, value `21–24px` Bai Jamjuree bold in
  a status color, sub-line `10–10.5px` `text-faint` (or a status color for emphasis).
- **Status pill:** `padding:3px 9px; border-radius:5px; font-weight:600; font-size:10.5px`,
  paired light-bg + saturated-fg color (e.g. success bg/fg, danger bg/fg, warning bg/fg).
- **Data table:** header row `background:#F7F8FA`, `10.5px` `text-faint` labels; body rows
  `border-top:1px solid #F0F1F4`; numeric columns right-aligned; row click for drill-down
  where applicable (WBS, Payment, Baseline).
- **Progress bar cell:** `8px` height, `4px` radius, track `#EDEEF2`/`#D8DCE4`, fill `navy`
  (or status color); render inline in table cells, not as a separate row.
- **Gantt bar layering:** critical bars `danger`, non-critical `secondary`, both with a
  `rgba(255,255,255,.35)` translucent overlay sized to actual `%` progress; a thin (`4px`)
  `gold` baseline bar renders below the main bar; a dashed `navy` vertical line marks the
  data date across the whole chart.
- **Toast:** fixed bottom-right, `navy` bg, white text, small `gold` status dot, `8px` radius.
- **Top bar actions:** outlined pill buttons per action (⟳ คำนวณ, Excel, MS Project, P6 XER
  each in their semantic color), with a solid `navy` "Export PDF" button last.

## Structure & state

```
src/features/<module>/   # planning/ evm/ site-logs/ payment/ vo/ ...
  components/ hooks/ api.ts types.ts index.ts
src/components/          # shared Button, Modal, DataTable, ChartCard, StatTile, StatusPill
src/services/            # axios instance, React Query client
src/store/                # Zustand slices (UI state only — server data lives in React Query)
```

- Server state: React Query (query keys `[module, entity, params]`, tenant-aware).
- Local/UI state: Zustand; never duplicate server data into the store.
- Numbers: money and MB (ล้านบาท) figures follow the prototype's compact style (e.g. `466.3`
  suffixed "MB" as a unit label, not embedded in the number); percentages 1 decimal to match
  prototype cadence (`54.2%`), full 2-decimal precision still stored/computed per convention.

## Performance patterns (Gantt & big tables)

- Virtualize rows (react-window pattern); render only visible rows; `React.memo` row components
  with stable props; scroll/zoom state outside React (refs) to avoid re-render storms.
- Heavy computation (CPM preview, S-Curve series over 10k activities) → Web Worker.
- Canvas or single-SVG layer for Gantt bars; DOM nodes only for interactive overlays.

## PWA / Offline-first (site modules)

- Service Worker: precache shell; runtime cache API GETs (stale-while-revalidate);
  never cache POSTs.
- Writes offline: enqueue in IndexedDB (`outbox` store with ULID, payload, retry count) →
  Background Sync flush on reconnect → reconcile server IDs; show per-item sync status chips.
- Photos: compress client-side (target ≤ 300 KB, keep aspect), store blob in IndexedDB until synced.
- Conflict rule: server wins on schedule data; last-write-wins with audit trail on site logs.
