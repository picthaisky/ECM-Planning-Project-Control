# ECM Planning Project Control (CM+ Project Control)

Enterprise Construction Management Planning & Project Control platform
(ระบบควบคุมโครงการก่อสร้างระดับองค์กร) — a single source of truth for WBS/Activity planning,
Gantt/CPM scheduling, S-Curve, Earned Value Management (EVM), Cash Flow forecasting, Payment
Certificates, Variation Orders, Weather Log (EOT evidence), Photo Progress, Man/Equipment
tracking, and Baseline comparison.

## Repository Layout

The repository is organized by responsibility so backend, frontend, infrastructure, tests, and
documentation stay separate and easy to evolve.

```text
.
├── backend/                   # .NET 10 solution (CMPlus.sln) — moved here post-Phase-0 to sit
│   │                           # alongside web/ and infra/ as a proper top-level split
│   ├── src/
│   │   ├── CMPlus.Domain/         # Pure domain model: entities, value objects, enums, rules
│   │   ├── CMPlus.Application/    # Use cases, CQRS handlers, abstractions, validators
│   │   ├── CMPlus.Infrastructure/ # EF Core, persistence, external integrations, storage
│   │   └── CMPlus.WebApi/         # API host, middleware, DI, controllers, auth
│   └── tests/                     # xUnit: Domain / Application / Infrastructure / Architecture
├── web/                       # React 19 + TypeScript frontend
│   ├── src/features/          # Module-based UI screens and feature slices
│   └── public/                # Static assets and PWA files
├── infra/                     # Docker, local environment, and future IaC/deployment assets
├── tests/                     # Cross-stack tooling only (perf scripts, shared dataset
│                               # generators) — not backend- or frontend-owned; see
│                               # docs/specs/master-plan/design.md §0
├── docs/                      # Product, architecture, design, and planning documents
└── .claude/                   # AI team constitution, skills, and shared knowledge base
```

Ownership rules:

- Backend business logic lives in `backend/src/CMPlus.Domain` and `backend/src/CMPlus.Application`.
- Persistence and external system access live in `backend/src/CMPlus.Infrastructure`.
- HTTP composition and API surface live in `backend/src/CMPlus.WebApi`.
- UI implementation lives in `web/` and should not mirror server state into local UI stores.
- Infra code and deployment helpers stay under `infra/`.
- Cross-cutting verification belongs in `tests/`.
- Product and architecture decisions belong in `docs/` and `.claude/knowledge/`.

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

**Phase 0 (Setup) complete. Phase 1 (Foundation & Data Sync, Sprints 1–4) complete.** See
`docs/10. แผนพัฒนารายเฟสโดยละเอียด (Detailed Phase Execution Plan).md` for the full sprint-by-sprint
plan and `.claude/knowledge/architecture/decisions.md` for the ADRs governing what's built.

Implemented so far: the Clean Architecture solution skeleton with tenant isolation and full CI
(build/test/lint/vulnerability-scan/secret-scan); domain entities (`Project`, `WBSNode`,
`Activity`, `ActivityRelation`, `ActivityProgressLog`, `Calendar`); JWT auth with a full
amount-tiered approval-policy engine (ADR-0008); XER/MSPDI/Excel file import with security
hardening (size caps, magic-byte checks, XXE/formula-injection/zip-bomb defenses); the WBS tree
read API (verified P95 well under the 100 ms target at low-to-moderate concurrency — see
`docs/perf/baseline.md` for a documented, still-open concurrency-scaling question); project-info
editing; batch progress recording; and the frontend app shell, login, Project Info, and WBS &
Activity screens, all built against the real running API (not mocks).

**Not yet started:** Phase 2 (Sprint 5–8 — CPM engine, Gantt, EVM, Cash Flow, Dashboard), Phase 3
(Sprint 9–12 — Payment Certificates, Variation Orders, Weather Log, Photo Progress, Man/Equipment),
Phase 4 (Sprint 13–16 — offline-first PWA, Baseline module, full security hardening, deployment —
cloud provider AWS vs Azure is a deliberately deferred decision, ADR-0010).

Nothing has been committed to version control yet — all work described above exists in the
working tree pending human review.
