# Sprint 15 — Full Regression + Perf Re-check (S15-QA-01)

**Author:** `qa-engineer` (run by the orchestrator) · **Date:** 2026-08-11
**DoD (verbatim):** *"WBS API < 100 ms และ Gantt 10,000 กิจกรรมยังผ่านหลังจูน; fixture ทุกชุด
(CPM/EVM/EAC/Payment/Routing) ยังเขียว — แนบผลรันจริง"* — WBS API < 100 ms and Gantt-10,000 still
pass after tuning; every fixture suite (CPM/EVM/EAC/Payment/Routing) still green, with **real run
output attached**.

---

## Verdict

- **Fixture regression: PASS.** Every backend and frontend test is green; the numbers below are from
  a real run this session, per project (never a summed total, per the standing discipline).
- **Perf re-check (WBS < 100 ms, Gantt 10,000): NOT EXECUTED — Docker-blocked.** These targets can
  only be measured against a running API + real SQL Server, and Docker Desktop cannot start on this
  workstation (needs Administrator; `docs/perf/gantt-frontend-s6.md` §3). This half of the DoD is
  **deferred, not passed** — see §3 for the exact re-run steps and the perf docs that already hold
  the method.

---

## 1. Backend — real run output (per project)

`dotnet build backend/CMPlus.sln` → **0 Warning(s), 0 Error(s)**.
`dotnet test backend/CMPlus.sln`:

```text
Passed!  - Failed: 0, Passed: 406, Skipped: 0, Total: 406  — CMPlus.Domain.Tests.dll
Passed!  - Failed: 0, Passed: 695, Skipped: 0, Total: 695  — CMPlus.Application.Tests.dll
Passed!  - Failed: 0, Passed:  17, Skipped: 0, Total:  17  — CMPlus.Architecture.Tests.dll
Passed!  - Failed: 0, Passed: 575, Skipped: 0, Total: 575  — CMPlus.Integration.Tests.dll
```

**Total: 1693 passed, 0 failed, 0 skipped.**

The `CMPlus.Architecture.Tests` project (17) is the structural-invariant gate and runs in CI
(S15-DO-01): layering / no-cloud-SDK (ADR-0010), `TenantIsolationBypassGuardTests` (S15-SEC-01 L-01 —
`IgnoreQueryFilters`/raw-SQL only in sanctioned sites), and `EvmCalculationIsNotDuplicatedTests`.

### 1.1 Named fixture suites (DoD: CPM / EVM / EAC / Payment / Routing) — all green

These are not a separate run — they are subsets of the 1693 above, called out because the DoD names
them. All pass in the run above. Domain-fixture provenance (each traces to a `docs/specs/**/domain-rules.md`
or a `.claude/knowledge/domain/*.md` fixture table):

| Suite | Fixtures (origin) | Status |
| :-- | :-- | :-- |
| **CPM** | forward/backward pass, float, critical path — `cpm-method.md`; Sprint-5 golden network A(5)/B(3)/C(6)/D(4) | green |
| **EVM** | PV/EV/AC, SV/CV, SPI/CPI, S-Curve — `evm-formulas.md` | green |
| **EAC** | 5-variant engine + A4/A5 advanced inputs — `evm-formulas.md`, ADR-0007 | green |
| **Payment** | P1–P7 retention/advance, 7-state machine — `payment-retention.md` | green |
| **Routing** | R1–R10 amount-tiered chain, quorum, escalation — `approval-workflow.md`, ADR-0008; + the routing **simulator** agrees-with-real-submit tests (S15-BE-01) | green |
| *(also)* **VO** R1–R6/V-6..V-20, **EOT** W-01..W-20, **Baseline** delta/single-active, **Manpower** PI M-01..M-16 | respective domain-rules.md | green |

No fixture value was adjusted to match code this sprint; the two places QA/`domain-expert` found a
code/fixture disagreement earlier in the project (the EOT §5.3 cap, the baseline activation ordering)
were fixed in the **code**, and their fixtures remain as authored.

## 2. Frontend — real run output

`npm run build` → clean · `npm run lint` → clean · `npx tsc -b` → clean.
`npm run test`:

```text
Test Files  179 passed | 1 skipped (180)
Tests      1326 passed | 1 skipped (1327)
```

The 1 skip is the deliberate live-backend soft-skip (present since Sprint 6). E2E suites relevant to
recent sprints (`photo-offline` 13/13, `site-outbox-multi-kind` 8/8, `service-worker` 3/3,
`eac-advanced` 4/4) were green when last run in their own sprints; they are not re-run here because
they need the Vite/Playwright harness rather than the unit runner, and nothing this sprint touched
their code paths.

## 3. Perf re-check — deferred (Docker-blocked), with the exact re-run steps

The DoD's perf clause (WBS API < 100 ms; Gantt renders 10,000 activities after any Sprint-15 tuning)
**cannot be measured in this environment** — no running API, no SQL Server. This is not a pass and is
not claimed as one. When Docker/SQL Server is available, re-run:

1. **WBS tree API < 100 ms** — the method and the seed (10,000-activity project) are already written
   in `docs/perf/baseline.md` (S4-QA-01) and the k6/timing harness under `tests/perf/`. Re-run after
   applying the 21 pending migrations; capture P50/P95 against the < 100 ms target.
2. **Gantt 10,000 activities** — the frontend virtualization budget and its frame-rate check are in
   `docs/perf/gantt-frontend-s6.md` and the nightly `perf-nightly-frontend` job; re-run against the
   real API.
3. **Index/query-plan tuning (S15-DB-01)** — no query-plan review ran this sprint (same Docker
   blocker); when it does, the top-20 slowest queries must show **no table scan** on a large table,
   and the before/after belongs in `docs/perf/tuning-sprint-15.md`. The index designs added this
   project (WBS, VO escalation, CpmRun, Baseline split-index for ADR-0021, idempotency) were each
   derived from the real `ToQueryString()` output, but **seek-vs-scan is an execution-plan claim that
   only a real database can confirm** — the standing caveat across every DB row this project.

Note the project-wide lesson this depends on: EF Core InMemory (used for all backend integration
tests) ignores unique indexes and does not roll back a failed `SaveChanges`, so **no InMemory result
is evidence about the storage engine** — the perf and index-enforcement claims specifically require
the real database, and several genuine defects this project (the baseline activation ~30–50% failure,
the ADR-0021 two-active-policy corruption) lived exactly in that unverifiable-here layer until they
were caught on SQLite stand-ins.

## 4. What this artifact proves, and what it doesn't

**Proves:** the entire fixture/regression surface — every domain calculation, state machine, security
guard and cross-screen consistency check across Sprints 1–15 — is green in one real run, with the
structural gates (layering, tenant-bypass, no-duplicate-EVM) enforced in CI.

**Does not prove:** any latency/throughput target, any query plan, or any behaviour that requires a
real SQL Server or a running API. Those are the S15-DB-01 / perf-clause items, deferred to a
Docker-capable environment, with their re-run steps recorded above.
