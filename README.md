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

## Building & Testing

All commands are run from the repository root; the backend solution is `backend/CMPlus.sln`.

```bash
# Backend (.NET 10) — build must be 0 warnings, 0 errors
dotnet build backend/CMPlus.sln
dotnet test  backend/CMPlus.sln          # xUnit: Domain / Application / Architecture / Integration

# Frontend (web/)
cd web
npm ci
npm run lint        # typecheck + eslint
npm run build       # Vite production build (emits the Service Worker)
npm run test        # Vitest unit/component suite
npx playwright test # E2E (offline→sync, service-worker versioning, EAC-advanced, …)
```

**Testing philosophy (enforced, not aspirational).** A passing suite is treated as necessary but
never sufficient: guards are proved by *making them fail* (mutation/canary probes), and assertions
target the durable artifact — stored bytes, row counts, the persisted value — not an in-memory
return. See `.claude/knowledge/lessons/lessons-learned.md` for the recurring cases where a fully
green suite sat on a real defect, and the `CMPlus.Architecture.Tests` project for the structural
fitness functions (Clean-Architecture layering, no cross-tenant filter bypass, no duplicated EVM
calculation) that fail the build if an invariant is violated.

**Environment note.** The integration tests run against the **EF Core InMemory** provider, which is
evidence about C# logic only — it ignores unique indexes and does not roll back a failed
`SaveChanges`. Storage-engine guarantees (unique/filtered indexes, `rowversion` concurrency, NULL
semantics, seek-vs-scan, migration application) are therefore verified against **SQLite** as a
constraint-enforcing stand-in where possible, and otherwise flagged as pending a real SQL Server. A
local MSSQL container (`infra/docker/`) is the intended target; several checks and the ~25 pending
EF migrations require it and are called out below.

## Status

**Phase 0 complete. Phases 1–3 (Sprints 1–15) implemented, tested, and security-reviewed. Phase 4
deployment (Sprint 16) is the remaining work and is environment-gated.** See
`docs/10. แผนพัฒนารายเฟสโดยละเอียด (Detailed Phase Execution Plan).md` for the sprint-by-sprint plan,
`.claude/knowledge/architecture/decisions.md` for the ADRs (through ADR-0021), and
`docs/security/reviews/` for the security gates.

**Built and verified (Sprints 1–15):**

- **Foundation (1–4):** Clean-Architecture solution with multi-tenant isolation (ADR-0002, proven
  across every `ITenantOwned` entity), JWT/RBAC auth, the amount-tiered version-pinned
  approval-policy engine (ADR-0008), XER/MSPDI/Excel import with security hardening, the WBS tree
  read API, the React app shell, Project Info, and WBS/Activity screens.
- **Core control engine (5–8):** the CPM scheduling engine, the virtualized Gantt, the EVM engine
  with the 5-variant EAC selector (ADR-0007) and immutable `EvmPeriodSnapshot` period-close
  (ADR-0009), the S-Curve, Cash Flow, and the Executive Dashboard.
- **Site management (9–12):** Payment Certificates (7-state machine, retention/advance math,
  append-only `ProjectFinanceLedger`), Variation Orders (chain + cumulative escalation +
  BAC/ContractValue rebaseline, ADR-0015/0016), Weather Log with a contemporaneous-CPM EOT evaluator
  (ADR-0019/0020), the Issue/Action log, offline-first Photo Progress, and Man/Equipment with the
  Productivity Index.
- **Optimization & hardening (13–15):** the generic offline outbox + Service Worker + Background
  Sync, server-side idempotency, the Baseline module + comparison engine, the routing simulator, a
  full-system tenant-isolation audit (all 59 CQRS handlers), an OWASP auth/upload/export review, and
  a CI security gate (High-or-above vulnerability fails the build; Dependabot enabled).

Every sprint that carried a security gate passed it — some only after a fix cycle that closed
execution-proven, money-moving defects (see the Sprint 9/10/12/15 reviews). At the last checkpoint:
backend **1696** tests green (0 warnings), frontend **1327** green, `dotnet list --vulnerable` and
`npm audit` both clean.

**Remaining — Sprint 16 (deployment), gated on a real environment and two human decisions:**

- Applying the ~25 EF migrations to a real MSSQL Server, the query-plan/perf re-check (WBS API
  < 100 ms; Gantt 10,000 activities), and the headless-Chromium PDF export (ADR-0014) all require
  Docker / SQL Server.
- **Cloud provider (AWS ECS vs Azure Container Apps)** — both runbooks and a comparison brief are
  ready in `docs/runbooks/`; the recommendation is Azure, the choice is the human's (ADR-0010).
- Two accepted, tracked findings need a product decision before production: per-project
  authorization (ADR-0018) and the EPPlus commercial licence (ADR-0014).

Version control: work is developed on the `supachai.nil` branch; nothing is merged to `main` —
humans approve designs and PRs (see `CLAUDE.md`).
