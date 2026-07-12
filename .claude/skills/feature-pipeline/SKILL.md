---
name: feature-pipeline
description: Automated multi-agent delivery pipeline for CM+ features — orchestrates po-analyst → domain-expert → system-architect → parallel implementation → QA → review/security → knowledge capture. Invoke with the feature request as argument, e.g. /feature-pipeline สร้างโมดูล Weather Log.
---

# CM+ Feature Delivery Pipeline

You are the Orchestrator. Run the following stages for the requested feature using the
Agent tool. Derive a `<feature-slug>` (kebab-case English) and use `docs/specs/<feature-slug>/`
as the artifact directory. Track stages with TodoWrite.

## Stages

**[1] Requirements — `po-analyst`**
Prompt it with the raw request + feature slug. Expect `docs/specs/<slug>/story.md`.
If it reports open domain questions → they go to stage 2; open product questions → ask the human now.

**[2] Domain rules — `domain-expert`** *(skip only if the feature has zero domain logic)*
Expect `docs/specs/<slug>/domain-rules.md` with LaTeX formulas and worked numeric examples.

**[3] Design — `system-architect`**
Expect `docs/specs/<slug>/design.md` with a task breakdown per implementer, plus ADRs if any.
**Gate:** if the design introduces a schema change or new external dependency, pause and
present the design summary to the human for approval before implementing (Human-in-the-loop).

**[4] Implementation — parallel**
Launch in ONE message (parallel Agent calls) per the design's task breakdown:
- `backend-developer` (server code) — include paths to design.md + domain-rules.md
- `frontend-developer` (UI) — include the frontend contract section
- `database-engineer` (schema/migrations) — only if data model changed
Each must report real build results.

**[5] Verification — `qa-engineer`**
Must map every acceptance criterion to a test and show real test output.
Failures → send findings back to the responsible implementer(s), re-run stage 5.
Max 3 fix cycles, then stop and escalate to the human with a status report.

**[6] Review — parallel**
- `code-reviewer` (always)
- `security-auditor` (if the diff touches auth, tenant data, uploads, payment, or parsers)
Release-blocking findings → back to implementers → re-review. Same 3-cycle escalation rule.

**[7] Learning — `knowledge-curator`**
Send it: what was built, what failed along the way, review findings, human corrections,
decisions made. It updates `.claude/knowledge/` and promotes patterns/rules.

## Final report to the human

Feature summary, artifact paths, test results (per acceptance criterion), outstanding
findings/risks, and what the team learned. Do not commit or merge unless the human asks.
