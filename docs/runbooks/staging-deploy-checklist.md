# Staging Deployment Checklist — Sprint 16A

**Provider-agnostic.** This is the execution-ordered runbook to take a green build to a running
staging environment. It captures the deploy-time traps this codebase actually surfaced across
Sprints 1–15 — do not skip a step because "the build is green"; every item here is something a
passing test suite does **not** prove (they run on EF InMemory/SQLite, never a real SQL Server, and
never against a database that has prior history).

Pair this with the provider-specific mechanics in
[`deploy-aws-ecs.md`](deploy-aws-ecs.md) or [`deploy-azure-container-apps.md`](deploy-azure-container-apps.md);
this file is the *what and in what order*, those are the *how* for a given cloud.

Prerequisite the team does not yet have in this environment: **Docker + a real SQL Server**
(migrations use SQL Server-specific DDL — `rowversion`, `decimal(18,2)` precision, and **filtered
unique indexes** — that will not apply to SQLite/InMemory). Nothing below is executable until that
prerequisite is met; that is exactly why none of it is covered by the automated suite.

---

## 0. Pre-flight — before the first migration touches the target database

The append-only guards and tenant filters protect a *running* system. The migrations that install
the unique indexes those guards rely on can themselves **fail or silently mis-backfill** on a
database that already has rows. Run these once per target database, in order, *before* `database
update`.

1. **Confirm the target is the database you think it is.** `SELECT DB_NAME(), @@SERVERNAME;` — a
   fat-fingered connection string pointed at the wrong environment is the highest-cost mistake here.
2. **Filtered-unique-index pre-flight (ADR-0021 / N-04).** Two migrations install *filtered* unique
   indexes (single-active approval-policy version per scope; single-active baseline per project).
   A filtered unique index **fails to create** if the existing data already violates it, and the
   failure aborts the whole `database update` mid-batch. Run the dedup probe first:
   - The Sprint-15 split-index case has a ready script:
     [`artifacts/migrations/20260811_sprint15_approvalpolicy_split_singleactive_index.PREFLIGHT.sql`](../../artifacts/migrations/20260811_sprint15_approvalpolicy_split_singleactive_index.PREFLIGHT.sql).
     It must return **zero rows** before you proceed. On a fresh staging DB it trivially does; keep
     it in the runbook anyway because prod will not be fresh.
   - For any other filtered unique index a migration adds, write the equivalent
     `GROUP BY <filter scope> HAVING COUNT(*) > 1 WHERE <filter predicate>` probe and require zero
     rows. **Never** let the migration be the thing that discovers the duplicate.
3. **`OriginalBac` / `OriginalContractValue` backfill sanity (H-02).** The Sprint-10 migration
   backfills `OriginalBac = BAC` and makes `OriginalContractValue` NOT NULL. This is correct **only**
   on a database with no approved Variation Order history (see the comment in
   `20260810045006_Sprint10_Project_OriginalBac.cs`). On a fresh staging DB that holds; if you ever
   run these migrations against a database that already carried live projects, assert
   `SELECT COUNT(*) FROM Projects WHERE BAC <> <intended original>` is 0 first, or the baseline
   denominator for VO-escalation will be silently wrong (a money-moving governance control — do not
   guess).

---

## 1. Secrets and configuration — must exist *before* the container starts

These are deliberately **absent from `appsettings.json`** and from every image layer. A missing one
is a start-time failure, not a runtime surprise — verify each is set in the platform's secret store,
never baked into the image or committed.

| Setting | Env var | Why it is not in the image |
| --- | --- | --- |
| JWT signing key | `Jwt__SigningKey` | Secret; forging it forges any user's session. See `docs/security/secrets-policy.md`. Must be ≥ 32 bytes of real entropy, unique per environment. |
| DB connection | `ConnectionStrings__CmPlusDb` (or platform equivalent) | Contains the DB credential; use a least-privilege app login, **not** `sa`. |
| EPPlus licence | `Excel__CommercialLicenseKey` | EPPlus 8 defaults to Polyform **Noncommercial**. CM+ is a commercial product — Excel import/export must not run under the noncommercial licence in production. **Human/legal action required before prod** (staging may run noncommercial for functional testing). |

Also confirm: `Jwt__Issuer`/`Jwt__Audience` match what the frontend expects, and the token
algorithm stays pinned to `HS256` (Program.cs `ValidAlgorithms` — the L-02 fix; do not widen it).

---

## 2. Apply the migrations (24 as of Sprint 15)

- Apply with the app's own migration path or `dotnet ef database update`, **against a real SQL
  Server** — never generate the schema with `EnsureCreated()` (it bypasses the migration history and
  the pre-flight guarantees above).
- The migration order is fixed by timestamp, `InitialCreate` → `Sprint15_ApprovalPolicy_SplitSingleActiveIndex`.
  Apply the **whole batch to a fresh DB in one run** — several backfills (esp. §0.3) are only correct
  because no application code has run between migrations to insert drifting rows.
- The CI `migration-smoke` job (`.github/workflows/ci.yml`) is the closest existing proof the batch
  applies cleanly to real MSSQL; if that job is green for this commit, the schema applies. If it was
  skipped (no `CMPLUS_TEST_MSSQL_CONNECTION` available), you are applying an **unverified** batch —
  do it against a throwaway DB first and diff the resulting schema against the model snapshot.
- After apply: `dotnet ef migrations has-pending-model-changes` (or the app's model-diff check) must
  report **no** pending changes — a drift there means the snapshot and the DB disagree.

---

## 3. Storage — the file/photo volume

- Uploaded files and progress photos go through `LocalDiskFileStorage`, which resolves every path
  under a configured root and **rejects traversal** (`IsPathWithinRoot`, the L-03 fix — case-sensitive
  match on Linux, case-insensitive on Windows). Set that root to a **mounted, persistent volume**,
  not the container's ephemeral layer and not `/tmp`: a container restart or scale event must not
  lose uploaded evidence.
- The volume needs write permission for the container's non-root user (see the Dockerfile's user).
- If you later move to object storage (S3/Blob — the M-4 adapter, not yet built), that is a code
  change behind the same `IFileStorage` seam, not a config toggle. Do not point the local adapter at
  a network share and assume traversal semantics still hold.

---

## 4. Frontend cache-busting — `VITE_BUILD_ID`

- The PWA Service Worker keys its cache on `VITE_BUILD_ID`. The web image takes it as a build **arg**
  (`infra/docker/web.Dockerfile`: `ARG VITE_BUILD_ID` → `ENV VITE_BUILD_ID`). Pass a **unique value
  per build** (the CI already wires the git SHA).
- Failure mode if you skip it: returning users keep a stale Service Worker and never see the new
  release — a silent "we deployed but nothing changed" that looks like a deploy that didn't take.
- After deploy, hard-verify: load the app, confirm the active SW cache name carries the new build id.

---

## 5. Background processing — the scale-to-zero / multi-instance trap

- Any `IHostedService`/`BackgroundService` (outbox drain, idempotency/sweep jobs) runs **in-process**.
  Two consequences the platform config must respect:
  - **Scale-to-zero** (Azure Container Apps default, some ECS setups) stops the background worker when
    no HTTP traffic arrives. If a sweep/drain must run on a schedule, either keep **min instances ≥ 1**
    or move that work to a platform scheduler — do not assume it runs while idle.
  - **Multiple instances** run the background job **once per instance**. Anything that must run singly
    (a global sweep) needs a leader/lock or a single-instance worker — otherwise you get duplicate
    work or contention. Confirm which jobs are safe to run N-up before scaling out.

---

## 6. Health probes — wire them to the right checks

- `/health/live` — liveness; process is up. Registered with `Predicate = _ => false`, so it runs
  **no** checks and never touches the database (a DB blip must not trigger a pod kill-and-restart
  loop). Both endpoints are `AllowAnonymous`.
- `/health/ready` — readiness (`Predicate = _ => true`); includes the **`database` check**
  (`DatabaseHealthCheck` →
  `IDatabaseConnectivityProbe.CanConnectAsync`, added this sprint). Returns **503** when the DB is
  unreachable, so the platform holds traffic until the DB is actually reachable. Point the platform's
  *readiness* probe here and its *liveness* probe at `/health/live` — swapping them causes restart
  storms during a transient DB outage.

---

## 7. Post-deploy smoke test (staging)

Run these against the deployed staging URL before handing to UAT. Each maps to a guard that only a
real environment exercises:

1. `GET /health/ready` → **200** with `database` healthy. Then, if you can, briefly cut the DB and
   confirm it flips to **503** (proves the probe is wired, not stubbed).
2. Login with a seeded user → **200 + token**; login with a wrong password and with an unknown email
   → **both** return the same generic `InvalidCredentials`, and take **comparably long** (the L-01
   timing-equalizer — a fast unknown-email response means `VerifyDummy` is not on that path).
3. Anonymous request to a protected endpoint → **401** (the L-05 `FallbackPolicy` — no endpoint is
   accidentally anonymous).
4. Tenant-isolation live check: as tenant A, request a known tenant-B resource id → **404/403**,
   never B's data. (InMemory ignores the global filter's teeth; only a real query proves it.)
5. Upload a file with a traversal-y name (`..\..\etc`) → stored safely under the root, path rejected
   (L-03).
6. Rapid repeated failed logins → **429 + Retry-After** with `application/problem+json` (the M-1 rate
   limiter — verify both per-IP and per-account limits, and the content-type).
7. Excel import of a small known workbook → succeeds and (staging) note whether it ran under the
   commercial or noncommercial EPPlus licence.

Any failure here is release-blocking for the promotion to UAT. Record the run in
`docs/qa/` alongside the Sprint-15 regression log.

---

## Known gaps this checklist cannot close from a dev machine

These remain open by nature and need the real environment or a human decision — they are **not**
oversights:

- Real MSSQL query-plan / index verification for the WBS-tree (<100 ms) and Gantt (10k+ activities)
  performance targets (S15-DB-01).
- Headless-Chromium PDF export path (needs the Chromium container).
- The EPPlus commercial licence key (legal/procurement).
- Cloud provider selection (AWS ECS vs Azure Container Apps — see
  [`cloud-decision-brief.md`](cloud-decision-brief.md)).
- Project-membership authorization (ADR-0018, accepted but not built — a full sprint of work).
