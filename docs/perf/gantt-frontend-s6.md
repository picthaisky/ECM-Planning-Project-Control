# Gantt Frontend Performance & Memoization Evidence — Sprint 6 (S6-FE-01/02/03)

**Author:** `frontend-developer` · **Date:** 2026-07-30 · **Sprint:** 6
**DoD sources:** `docs/10. แผนพัฒนารายเฟสโดยละเอียด (Detailed Phase Execution Plan).md` §7 Sprint 6
(frontend-developer row S6-FE-01/02/03), ADR-0004 (virtualized rendering), US-6.1/US-6.2
(`docs/specs/master-plan/backlog-detailed.md`).
**Code under test:** `web/src/features/gantt/`

This follows the same "real numbers, reproducible runbook, no fabricated claims" standard
`qa-engineer` established in `docs/perf/baseline.md` (S4-QA-01) — every number below is copy-pasted
from an actual command run in this session, not estimated.

---

## 1. Row-prop memoization proof (S6-FE-03 DoD)

DoD text: "React DevTools profiler ยืนยันว่าแถวที่ props ไม่เปลี่ยนไม่ re-render (บันทึกผลไว้ในชุด
perf)".

### 1.1 First attempt — `React.Profiler`'s public `onRender`, rejected after actually running it

The obvious approach is wrapping each `GanttLabelRow` in its own `<Profiler onRender={...}>` (this
*is* literally the instrumentation React DevTools' own Profiler tab is built on) and counting
callback invocations across an unrelated parent re-render (standing in for a scroll-driven
virtualizer update). This was tried first and its real output was:

```
FAIL  src/features/gantt/hooks/useGanttRowViewModels.test.tsx > … > a row whose data has not
      changed does not re-render across repeated unrelated parent re-renders
AssertionError: expected [ 4, 4, 4, 4, 4 ] to deeply equal [ 1, 1, 1, 1, 1 ]
```

I.e. after 1 mount + 3 no-op parent re-renders (`activities` array unchanged throughout),
`onRender` fired **4 times per row** — it does not distinguish "this Profiler boundary was part of
a commit" from "the memoized component underneath actually re-executed its render body". Profiler
alone would have made this test **pass trivially regardless of whether `React.memo` worked at
all**, which is not a real proof. This is recorded here specifically so nobody re-introduces bare
`onRender`-based row-memoization tests believing them to be conclusive.

### 1.2 What actually works — a direct render-body invocation counter (`onRenderProbe`)

`GanttLabelRow` (`web/src/features/gantt/components/GanttLabelRow.tsx`) exposes an optional,
production-harmless `onRenderProbe?: () => void` prop, called synchronously once per actual
invocation of the component's render body. This answers the literal question "did this memoized
component re-render" unambiguously — it is what the DevTools Profiler flamegraph visualizes under
the hood; a real DevTools browser-extension screenshot isn't automatable in this headless
environment, so this is the honest, verifiable programmatic equivalent.

Real, current output (`npx vitest run src/features/gantt/hooks/useGanttRowViewModels.test.tsx --reporter=verbose`):

```
✓ useGanttRowViewModels + GanttLabelRow memoization (S6-FE-03, ADR-0004) > a row whose data has
  not changed does not re-render across repeated unrelated parent re-renders (the real scroll
  scenario) 195ms
✓ useGanttRowViewModels + GanttLabelRow memoization (S6-FE-03, ADR-0004) > recomputes every row
  view-model (new references) when the underlying activities array is reloaded — the memo does
  not silently go stale 3ms
✓ GanttLabelRow in isolation (sanity check: memoization is not vacuously true) > does not
  re-render when given the exact same props, but does re-render when a prop actually changes 1ms

Test Files  1 passed (1)
     Tests  3 passed (3)
```

Concretely, with 5 rows and 3 forced unrelated parent re-renders:

| Scenario | Expected render-body invocations per row | Actual (asserted, passing) |
| --- | --- | --- |
| Initial mount | 1 | `[1,1,1,1,1]` |
| +3 unrelated parent re-renders, `activities` reference unchanged | still 1 (memo blocks all 3) | `[1,1,1,1,1]` |
| Genuine data reload (new `activities` array) | 2 (control case — memo is not vacuous) | `[2,2,2]` (3-row variant) |
| Same props object re-passed directly to `GanttLabelRow` | 1 | 1 |
| `top` prop actually changed | 2 | 2 |

This proves both directions: a row whose props are unchanged is skipped (the DoD requirement), and
a row whose props genuinely change still re-renders (the memo is not simply broken/always-bailing,
which would otherwise make the first result meaningless).

### 1.3 Why this generalizes to the real 10,000-activity scroll case

`hooks/useGanttRowViewModels.ts` maps the raw `GanttActivityDto[]` into the flattened, all-primitive
`GanttRowViewModel` shape `GanttLabelRow` actually renders, memoized on `[activities]` alone. Since
`GanttChart` never recreates the `activities` array in response to scrolling/zoom (both are pure
canvas/ref-driven, per §2 below), every row's view-model object keeps the same reference across any
scroll-driven re-render of the label pane — exactly the condition §1.2 proves `GanttLabelRow` skips.

---

## 2. Real end-to-end verification at the S4-DB-02 10,000-activity scale

### 2.1 Stack state found vs. corrected

The `infra/docker/docker-compose.yml` stack (`docker-api-1`/`docker-mssql-1`/`docker-minio-1`) was
already running (8h uptime) from earlier sprint work, but `docker-api-1` was **stale** — it predated
S6-BE-01 (`GET .../gantt` returned a bare `404` with no `WWW-Authenticate`/auth challenge at all,
i.e. the route itself didn't exist in that image, not an auth rejection). Corrected by:

```
cd infra/docker
docker compose build api   # picks up the current (uncommitted, on-disk) backend/src, incl. S6-BE-01
docker compose up -d api
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/health/ready   # -> 200
```

Migrations were already up to date (`dotnet ef database update` against the same connection string
`docs/perf/baseline.md` §1 uses: "No migrations were applied. The database is already up to date.").
The dev tenant/user seed and S4-DB-02's 10,000-activity/5,000-node/15,000-relation dataset
(project id `2193e79c-a4c8-e7a5-0ffe-fb3f581af585`) were already present in the persisted
`mssql-data` volume from earlier sessions, and CPM had already been run against it at some point
(`isCritical`/`totalFloat`/`freeFloat` were already populated, not null) — no reseed/recalculate was
needed.

### 2.2 Real HTTP call against the real endpoint

```
$ TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"pm@siam-construction.dev","password":"Dev@CMPlus2026!"}' | ...)
$ curl -s -o gantt_check.json -w "HTTP:%{http_code} time:%{time_total}s size:%{size_download} bytes\n" \
    -H "Authorization: Bearer $TOKEN" \
    "http://localhost:5000/api/v1/projects/2193e79c-a4c8-e7a5-0ffe-fb3f581af585/gantt"
HTTP:200 time:0.115428s size:3315233 bytes
```

Payload shape confirmed to match the frontend's `GanttActivityDto` field-for-field
(`activityCode,actualFinish,actualStart,freeFloat,id,isCritical,name,plannedFinish,plannedStart,totalFloat,wbsNodeId`
— exactly the 11 fields in `web/src/features/gantt/types.ts`, nothing more/less):

```
activities.length: 10000
critical count: 9985  non-critical: 15
with actualStart: 0
null totalFloat: 0
```

(The 9985/15 critical/non-critical split and zero recorded `actualStart` values are properties of
this specific deterministic seed dataset, not a frontend concern — flagged here only for
transparency, not as a defect.)

### 2.3 Real component render against the real payload (no mock)

`web/src/features/gantt/components/GanttChart.liveSmoke.test.tsx` — an opportunistic test that
soft-skips without a live backend (same convention as
`backend/tests/CMPlus.Integration.Tests/Persistence/MigrationAndSeedSmokeTests.cs`), run for real
this session:

```
$ VITE_CMPLUS_LIVE_API_BASE_URL="http://localhost:5000/api/v1" npx vitest run \
    src/features/gantt/components/GanttChart.liveSmoke.test.tsx

 Test Files  1 passed (1)
      Tests  1 passed (1)
```

This fetched the real 10,000-activity payload over a real HTTP call and rendered the real
`GanttChart` against it, asserting:

- exactly **2** `<canvas>` elements total (header + body) — never one DOM node per bar, regardless
  of the 10,000 real activities;
- the virtualized label pane mounted **fewer than 60** `[data-gantt-row]` DOM nodes (viewport +
  overscan), not 10,000;
- no crash/throw decoding or rendering the real payload shape.

Without the env var set, this same test file soft-skips (`1 skipped`), confirmed separately so it
never becomes a hard dependency for `npm run test` on a machine without the docker stack running.

### 2.4 What this does *not* cover

jsdom has no real layout engine, GPU, or paint pipeline, so none of the above measures actual frame
rate during a continuous scroll gesture — only "did the real payload decode/render correctly and
stay DOM-bounded". The 30-second continuous-scroll frame-rate measurement against this same
10,000-activity dataset, in a real browser, is `qa-engineer`'s S6-QA-01
(`web/e2e/gantt-perf.spec.ts`, not yet created) — this document is the frontend-developer-side
input to that task, not a substitute for it.

### 2.5 Synthetic-data DOM-count assertions (permanent CI suite, no live backend needed)

Independent of the live-backend check above, `components/GanttChart.test.tsx` asserts the same
virtualization bound against 10,000 locally-generated synthetic activities on every `npm run test`
run (CI-safe, no docker dependency):

```
✓ ADR-0004 (S6-FE-01 DoD, "ไม่มีดีไซน์ DOM-per-bar"): bars are drawn on canvas — 10,000 activities
  never produce 10,000 DOM bar elements, and there are still only 2 <canvas> elements total
✓ ADR-0004: the virtualized label pane mounts only the visible window + overscan, never one DOM
  row per activity
```

---

## 3. S6-QA-01/02 execution status — specs written and harness-verified, real measurement NOT YET TAKEN

**Author:** orchestrator · **Date:** 2026-07-30

`web/e2e/gantt-perf.spec.ts` and `web/e2e/visual/gantt-visual.spec.ts` now exist (Playwright
installed, config + npm scripts + Chromium in place). **The real 30-second frame-rate measurement
and the real visual-regression comparison have not been run**, so no frame-rate baseline is
recorded here or in `docs/perf/baseline.md` yet. Per this project's standing rule — and S6-QA-01's
own DoD wording ("ผลรันจริงถูกแนบ · ห้ามอ้างลอย ๆ") — no number is reported that was not measured.

### Why it is blocked

Docker Desktop on this workstation cannot start (`Docker Desktop is unable to start`), so the
MSSQL + API stack the specs need is unavailable. Root cause was traced to the privileged helper
service `com.docker.service` being **Stopped**; starting it fails with `Cannot open
com.docker.service service on computer '.'` — an access-denied that requires an **Administrator**
action. This is a machine-state problem, not a repo or code problem: `wsl --shutdown` plus a full
Docker Desktop restart did not clear it.

### What *was* verified without the backend (real runs, not assumptions)

Running the perf spec anyway exercised everything up to the first backend call, which confirmed:
Playwright + Chromium launch, the config's `webServer` auto-start, the Vite dev server, spec
compilation, `page.goto('/login')`, the CM+ login form rendering, and form fill/submit all work.
It then fails exactly where a missing backend must — `waitForURL` after login, because the login
POST has nothing to answer it. So the harness is sound; only the measurement is outstanding.

### A real bug this shakeout caught (fixed)

The first run drove **an entirely different application**. `playwright.config.ts` used Vite's
default port 5173 with `reuseExistingServer: !process.env.CI`; another project's dev server was
already on that port, and Playwright reused it, so the suite drove that app's login page. It
failed noisily only by luck — an unrelated app with similar labels could have produced *passing*
garbage. Fixed two ways: (1) a CM+-specific default port (5273, override via `E2E_PORT`) with
`reuseExistingServer: false` and `--strictPort`, so a collision is a loud startup error rather
than a silent wrong-target; (2) an explicit app-identity assertion in
`e2e/support/gantt.ts#loginAndOpenGantt` that fails with a clear diagnostic if the CM+ login
heading is absent — this catches the whole failure class regardless of port.

### To complete S6-QA-01/02

With Administrator access: start `com.docker.service` (or repair/reinstall Docker Desktop), then
follow `docs/perf/baseline.md` §1's runbook to bring up the stack, apply migrations, run the dev
seed and `tests/perf/seed-large-project.sql`, and run `npm run test:e2e:perf` and
`npm run test:e2e:visual` from `web/`. Record the real numbers here and add the frame-rate
baseline section to `docs/perf/baseline.md`. Follow Sprint 4's precedent for a noisy first
measurement: if runs vary materially, report the range across several runs rather than one
cherry-picked figure.
