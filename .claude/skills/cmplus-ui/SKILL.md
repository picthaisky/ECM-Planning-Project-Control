---
name: cmplus-ui
description: CM+ frontend design system and React patterns — warm orange-brown theme tokens, Tailwind config, module structure, Gantt/chart performance patterns, and PWA offline-first recipes. Load before writing or reviewing any frontend code or UI design.
---

# CM+ Frontend: Design System & React 19 Patterns

## Theme (Professional, Futuristic, yet Warm)

Tailwind theme tokens — never hardcode hex in components:

| Token | Hex | Use |
| --- | --- | --- |
| `primary` | `#E26D5C` | Primary actions, progress bars, brand |
| `amber-deep` | `#A1523E` | Critical path bars, urgent emphasis, key menus |
| `slate-warm` | `#2B2523` | Main text, dark surfaces (premium feel) |
| `ivory` | `#FDFBF7` | App background (low eye strain) |
| `success` | `#52B788` | Completed activities/status |
| `forecast` | `#4EA8DE` | Forecast lines (EAC), prediction tools |

- Fonts: `Inter` (numerals/tables) + `IBM Plex Sans Thai`; tabular numerals for money columns.
- Cards: soft rounded corners, subtle shadows; light glassmorphism on Header/Sidebar only.
- UI copy Thai-first; keep EVM terms English (SPI, CPI, EAC). Money: 2 decimals + thousand
  separators; dates: Thai locale display, ISO in transport.

## Structure & state

```
src/features/<module>/   # planning/ evm/ site-logs/ payment/ vo/ ...
  components/ hooks/ api.ts types.ts index.ts
src/components/          # shared Button, Modal, DataTable, ChartCard
src/services/            # axios instance, React Query client
src/store/               # Zustand slices (UI state only — server data lives in React Query)
```

- Server state: React Query (query keys `[module, entity, params]`, tenant-aware).
- Local/UI state: Zustand; never duplicate server data into the store.

## Performance patterns (Gantt & big tables)

- Virtualize rows (react-window pattern); render only visible rows; `React.memo` row components
  with stable props; scroll/zoom state outside React (refs) to avoid re-render storms.
- Heavy computation (CPM preview, S-Curve series over 10k activities) → Web Worker.
- Canvas or single-SVG layer for Gantt bars; DOM nodes only for interactive overlays.
- Charts follow the dataviz skill; S-Curve: PV solid, EV success-green, AC amber, EAC forecast-blue dashed.

## PWA / Offline-first (site modules)

- Service Worker: precache shell; runtime cache API GETs (stale-while-revalidate);
  never cache POSTs.
- Writes offline: enqueue in IndexedDB (`outbox` store with ULID, payload, retry count) →
  Background Sync flush on reconnect → reconcile server IDs; show per-item sync status chips.
- Photos: compress client-side (target ≤ 300 KB, keep aspect), store blob in IndexedDB until synced.
- Conflict rule: server wins on schedule data; last-write-wins with audit trail on site logs.
