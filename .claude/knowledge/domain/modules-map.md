# The 15 Core Modules — Map

Source: docs/6. §1. Menu order matches the prototype UI (LINE_NOTE_260711_2.jpg).

| # | Module (เมนู) | Purpose | Key domain refs |
| --- | --- | --- | --- |
| 1 | ข้อมูล (Project Info) | Contract scope, data date management | — |
| 2 | กิจกรรม (WBS & Activity) | WBS tree, activity codes, weights, budgets | rollup rules in [evm-formulas.md](evm-formulas.md) |
| 3 | Gantt (Gantt & Critical Path) | Drag-adjust schedule, auto CPM, red critical bars | [cpm-method.md](cpm-method.md) |
| 4 | S-Curve | Cumulative plan vs actual curves + forecast | [evm-formulas.md](evm-formulas.md) |
| 5 | CashFlow | PV/EV/AC cumulative cash forecast | [evm-formulas.md](evm-formulas.md) |
| 6 | Dash (Dashboard) | Executive overview, project health | — |
| 7 | EVM | SPI/CPI/EAC dashboard | [evm-formulas.md](evm-formulas.md) |
| 8 | สรุป (Executive Summary) | Email-ready concise report | — |
| 9 | ก้าวหน้า (Daily/Weekly Progress) | Field progress % per activity | rollup rules |
| 10 | Issue (Issue & Weather) | Site issues + rainfall log for EOT claims | weather-EOT rules in `/cm-domain` skill |
| 11 | เบิกงวด (Payment Tracking) | Milestones, retention, advance recovery, certificates | payment math in `/cm-domain` skill |
| 12 | รูปถ่าย (Photo Progress) | Photo gallery bound to WBS/zone, offline-first | PWA recipes in `/cmplus-ui` skill |
| 13 | CO/VO (Variation Order) | Add/deduct works, approval workflow, BAC impact | evm-formulas rebaseline rule |
| 14 | Man/Eq (Manpower & Equipment) | Daily labor/machine usage tracking | — |
| 15 | Baseline | Lock target schedule, compare vs current | [cpm-method.md](cpm-method.md) |

Delivery phases (docs/2.): P1 foundation/parsers → P2 Gantt/EVM engines → P3 site modules
(9–14) → P4 PWA/security/launch.

Future enhancements (docs/6. §2): AI delay prediction, BIM/IFC 4D viewer, smart-contract payments.

## Reconciliation with the working prototype (2026-07-12)

`docs/ECM Planning Prototype.dc.html` (+ Standalone export) is a working 13-screen build that
is now the authoritative UI reference (ADR-0006). Its nav, in order: Executive Dashboard,
ข้อมูลโครงการ, WBS & Activity, Gantt/CPM, EVM S-Curve, Cash Flow, Payment Certificate,
Photo Progress, Variation Order, Issue/Action Log, Weather Log, Man/Equipment, Baseline.

Differences from this doc's 15-module list, **unresolved — confirm with `po-analyst`/human
before building either as a standalone screen:**
- **สรุป (Executive Summary, module #8)** — not a separate screen in the prototype; its
  content (EVM %, EAC, payment/weather rollups) already lives on the Executive Dashboard.
  Default assumption: no separate screen needed unless a print/email-specific format is required.
- **ก้าวหน้า (Daily/Weekly Progress, module #9)** — not a separate screen; progress entry is
  implied via WBS row editing, Photo Progress, and the Issue log. Default assumption: progress
  capture is a *component/action* (e.g. an edit affordance on WBS rows), not a top-nav page —
  confirm this before implementing module #9 as its own route.
- The prototype's **Issue / Action Log** and **Weather Log** are two separate screens, whereas
  this doc's module #10 describes them combined ("Issue & Weather Tracking"). Use the
  prototype's split (two screens) as current truth.
