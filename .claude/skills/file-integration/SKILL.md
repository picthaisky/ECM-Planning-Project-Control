---
name: file-integration
description: Rules for CM+ file import/export — Primavera P6 .XER parsing, MS Project via MSPDI XML (MPXJ), Excel templates via EPPlus, and PDF export. Load before writing or reviewing any parser, importer, or exporter code.
---

# CM+ File Integration: XER / MSPDI / Excel / PDF

All imported files are **untrusted input**: enforce size limits, validate structure before
processing, parse in constrained code paths, and never trust file extensions.

## Primavera P6 (.XER)

- Tab-delimited text; tables start with `%T <name>`, fields `%F`, rows `%R`.
- Import these tables: `PROJ` (project), `PROJWBS` (WBS), `TASK` (activities),
  `TASKPRED` (relations: FS/SS/FF/SF + lag), `CALENDAR`, `TASKRSRC` (resource/cost).
- Skip bloat tables: `RISKTYPE`, `POBS` (data cleansing per docs/วิเคราะห์ฯ §2).
- Referential validation before commit: every TASK must reference an existing WBS row;
  every TASKPRED must reference two existing tasks; every task calendar must exist.
  Orphaned records → reject file with a row-level error report, not partial import.
- Percent-complete semantics differ by P6 type (duration/physical/units) — map explicitly;
  document the mapping in the import result.

## MS Project (.MPP → MSPDI)

- Do NOT parse binary .MPP directly or via Office interop (unstable in cloud — docs/วิเคราะห์ฯ).
- Use MSPDI (Microsoft Project Data Interchange XML) via MPXJ.Net: tasks, WBS outline,
  resources, assignments, calendars (incl. exceptions), baselines, milestones.
- XML hardening: disable DTD/external entities (XXE), cap file size and node depth.

## Excel (EPPlus)

- **Import (weekly progress):** protected structured template — locked layout, hidden
  `ActivityId` column as the join key, data validation on % (0–100). On upload: validate
  every row, report all errors at once (row + reason), apply as one transaction.
- **Export:** schedule + relations round-trip for external editing; escape any cell starting
  with `=`, `+`, `-`, `@` (formula injection); license EPPlus appropriately (commercial).

## PDF export

- Executive reports: header with project info + data date, S-Curve chart, EVM table
  (SPI/CPI/EAC), progress photos grid by zone, Thai fonts embedded (IBM Plex Sans Thai).

## Round-trip integrity (QA contract)

Import → export → re-import must be lossless for: WBS hierarchy + codes, activity dates and
durations, relation types + lags, budget values. Golden-file tests compare against reference
values produced by real P6/MSP (project risk #2, docs/2. — Critical severity).
