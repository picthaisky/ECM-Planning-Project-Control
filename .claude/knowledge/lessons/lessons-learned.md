# Lessons Learned

Append-only, newest first. Written by knowledge-curator (or via `/learn`).
Every entry must end with an actionable rule. QA turns recurring lessons into permanent tests.

Entry format:

```
## YYYY-MM-DD — <short title>
Context: <task/feature>
What happened: <the failure or correction, 1–3 lines>
Root cause: <why>
Rule: <what every agent does differently next time>
```

---

## 2026-07-11 — Knowledge base initialized
Context: Multi-agent system bootstrap from docs/1–8.
What happened: Team, skills, and knowledge base created from the product documentation.
Root cause: —
Rule: Agents consult INDEX.md before non-trivial work; work that reveals a gap in this
knowledge base must end with a `/learn` capture so the gap closes permanently.

## 2026-07-12 — Design system was built from prose before a working prototype existed
Context: The team's first pass at `/cmplus-ui` and `CLAUDE.md` encoded a "warm orange-brown"
theme purely from `docs/3.`'s narrative description. The user then supplied a working HTML
prototype (`docs/ECM Planning Prototype.dc.html`) with a different, concrete navy/gold theme
and a 13-screen nav that doesn't exactly match `docs/6.`'s 15-module list.
What happened: Had the prototype been checked first, the initial design system would have
been correct on the first pass instead of needing a correction (ADR-0006).
Root cause: Text specs describe *intent*; a working prototype is *ground truth* and can
diverge from earlier prose as the product evolves. We designed from the older artifact only.
Rule: When both a prose doc and a working prototype/mockup exist for the same subject, always
open and defer to the prototype — treat prose docs as historical intent, not current spec.
When a new prototype/mockup file appears in the repo, proactively diff it against the current
design system and knowledge base before doing unrelated work.

## 2026-07-27 — Parallel po-analyst/domain-expert passes diverged on schema shape
Context: Detailed phase-plan expansion (docs/9. §11 open items → docs/10.). `po-analyst` and
`domain-expert` were run in parallel against the same five human decisions, each without seeing
the other's artifact.
What happened: On the two questions that were genuinely data-model-shaped (EAC variant scope,
approval-authority model), both agents independently proposed reasonable but incompatible
shapes — `po-analyst` scoped a 3-value EAC enum and a role-only boolean approval matrix;
`domain-expert` (unaware of those choices) recommended a 5-variant PF engine and an amount-tiered
policy model with different field/table names. `system-architect` reconciled both correctly
(ADR-0007, ADR-0008), but that reconciliation was an extra full agent pass that sequencing or
tighter prompts would have avoided.
Root cause: Domain formulas/rules usually *drive* what shape a schema needs, so running the
schema-adjacent agent (po-analyst) before the domain agent has finished loses the dependency; and
neither prompt asked agents to keep schema proposals directional-only.
Rule: For a planning task where a decision implies new entities/enums/field names, either (a) run
`domain-expert` before `po-analyst` (domain rules first, since they constrain the schema), or (b)
if they must run in parallel, instruct both explicitly to keep schema proposals minimal/directional
and defer concrete field naming to `system-architect`. Promoted to `CLAUDE.md` Learned Rules.

## 2026-07-27 — Validation must run from repo root or with explicit absolute paths
Context: Solution validation after implementation of master-plan design (docs/specs/master-plan/).
Repository already physically separated (src/, web/, infra/, tests/, docs/).
What happened: `dotnet test` run from `web/` directory (PowerShell CWD) failed to find the solution
or projects, causing a false start. Re-running from repo root with `dotnet test .\CMPlus.sln`
succeeded immediately. Validation confirmed: dotnet build + dotnet test pass; only non-blocking
warnings remain (NU1903 CVE in OpenApi 2.0.0, xUnit discovery for empty test projects).
Root cause: Relative paths in dotnet commands resolve from CWD. `web/` is the frontend folder and
lacks .sln context; PowerShell had navigated there during frontend work, breaking backend validation.
Rule: When running solution-wide validation commands (dotnet build/test/restore), always run from
repo root or use explicit absolute paths. Before reporting a build/test failure, verify CWD matches
the expected location for the command.

## 2026-07-27 — "Restructure" requests may already be physically satisfied
Context: User request to "restructure the repository" for backend/frontend separation. Inspection
revealed src/, web/, infra/, tests/, docs/ folders already existed with proper separation.
What happened: The requested structure was already in place; the task was validation and wiring
checks (solution references, build scripts), not a physical file reorganization.
Root cause: "Restructure" is ambiguous — can mean physical file layout OR logical wiring (imports,
solution structure, build config). We assumed the former before checking repo state.
Rule: When asked to "restructure" or "reorganize" a repository, first scan the actual folder
structure. If physical separation already matches the request, reframe the task as "validate and
wire the existing structure" instead of moving files. Report the existing layout and ask if the
user means logical wiring, build config, or something else.

## 2026-08-09 — UUIDv7 is time-ordered only to the millisecond; never sort by it for "newest first"
Context: `ListPaymentCertificatesQuery` (Sprint 9 read-side) needed "newest first" ordering. The
entity base uses `Guid.CreateVersion7()`, whose whole selling point is being time-ordered, so
`OrderByDescending(c => c.Id)` looked like the obvious free win.
What happened: `backend-developer` empirically probed it before trusting it and found ~80% of
triples created in a tight loop came back in the wrong order under every comparison strategy tried.
Root cause: UUIDv7's timestamp has **millisecond** resolution and .NET's `CreateVersion7()` adds no
monotonic counter within a millisecond — the remaining bits are random. Anything created in the
same millisecond (bulk import, seeding, a fast loop, a busy endpoint) sorts arbitrarily. The
"time-ordered UUID" name promises more than the implementation delivers at sub-millisecond scale.
Additional trap layered on top: sorting Guids **in SQL Server** uses `uniqueidentifier` collation,
which does not compare bytes left-to-right, so pushing such an `OrderBy` to the database is wrong
in a second, independent way.
Rule: Never order by a UUIDv7 primary key to mean "creation order." Add an explicit `CreatedAt
DateTimeOffset` column and sort on that (with the id as a deterministic tie-break if needed). If a
Guid must be compared for ordering at all, do it client-side on the canonical hex form, never via
SQL Server's `uniqueidentifier` collation. Applies to every entity in this codebase, since they all
inherit `Entity`'s `Guid.CreateVersion7()` id.
