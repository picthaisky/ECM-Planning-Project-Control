# ECM Planning Project Control (CM+ Project Control)

Enterprise Construction Management Planning & Project Control platform
(ระบบควบคุมโครงการก่อสร้างระดับองค์กร) — a single source of truth for WBS/Activity planning,
Gantt/CPM scheduling, S-Curve, Earned Value Management (EVM), Cash Flow forecasting, Payment
Certificates, Variation Orders, Weather Log (EOT evidence), Photo Progress, Man/Equipment
tracking, and Baseline comparison.

## Stack

- **Backend:** C# .NET 10, EF Core 10, Web API, Clean Architecture + DDD, CQRS (MediatR),
  FluentValidation, MSSQL Server (multi-tenant).
- **Frontend:** React 19 + TypeScript + Vite + Tailwind CSS, offline-first PWA (IndexedDB +
  Service Worker + Background Sync), Zustand, React Query/Axios.
- **Integrations:** Primavera P6 `.XER` parser, MS Project via MSPDI (MPXJ), Excel (EPPlus), PDF export.

## Documentation

Product and design documentation lives in [`docs/`](docs/):

| File | Contents |
| --- | --- |
| `1. รายงานการวิจัยและวิเคราะห์โครงการ.md` | Industry pain points, competitive analysis |
| `2. แผนแม่บทโครงการ.md` | Project phases, WBS, risk plan |
| `3. พิมพ์เขียวสถาปัตยกรรมระบบ.md` | Architecture blueprint, frontend structure (design-system narrative superseded — see below) |
| `4. เอกสารการออกแบบและข้อกำหนดทางเทคนิค.md` | DB schema, CPM & EVM formulas |
| `5. พรอมต์สำหรับออกแบบระบบ AI.md` | Prompt template for AI-assisted module generation |
| `6. สรุปแนวทางพัฒนา โมดูลและ Future Enhancements.md` | The 15 core modules, future roadmap |
| `7. โครงสร้างทีมงานและทักษะที่จำเป็น.md` | AI agent team concept (PO/SA/BE/FE/QA) |
| `8. สถาปัตยกรรม Multi AI Agents.md` | **Implemented** multi-agent architecture blueprint (this repo's `.claude/`) |
| `ECM Planning Prototype.dc.html` / `(Standalone).html` | **Working interactive prototype — the authoritative UI/UX reference**, 13 screens, navy/gold theme |

> **Design system note:** the prototype HTML files reflect the current, concrete visual
> direction (navy `#0F2542` / gold `#C9A227`, IBM Plex Sans Thai + Bai Jamjuree). The
> orange-brown palette described in doc `3.` predates the prototype and is superseded
> (see ADR-0006 in `.claude/knowledge/architecture/decisions.md`).

## AI Development Team

This repository is developed by an orchestrated team of specialized AI agents (Claude Code),
defined in [`.claude/`](.claude/) and governed by [`CLAUDE.md`](CLAUDE.md):

- **Agents** (`.claude/agents/`): `po-analyst`, `domain-expert`, `system-architect`,
  `backend-developer`, `frontend-developer`, `database-engineer`, `qa-engineer`,
  `security-auditor`, `devops-engineer`, `code-reviewer`, `knowledge-curator`.
- **Skills** (`.claude/skills/`): `/cm-domain`, `/clean-architecture-dotnet`, `/cmplus-ui`,
  `/file-integration`, `/feature-pipeline`, `/learn`.
- **Knowledge base** (`.claude/knowledge/`): EVM/CPM formula references with test fixtures,
  architecture decision records (ADRs), coding conventions, and a running lessons-learned log
  that the team updates after every significant piece of work (self-learning loop).

To build a feature end-to-end (requirements → domain rules → design → parallel
backend/frontend/database implementation → tests → review/security → knowledge capture), run:

```text
/feature-pipeline <feature description>
```

See `docs/8. สถาปัตยกรรม Multi AI Agents.md` for the full architecture diagram and coordination protocol.

## Status

Early stage — architecture, AI team, and UI prototype are defined; implementation of the
.NET/React solution has not started yet.
