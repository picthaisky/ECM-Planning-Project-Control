# Runbook A — AWS ECS Fargate

**Status: PLANNING DOCUMENT. Nothing in this runbook has been provisioned, deployed, or executed
against AWS.** No AWS account, VPC, ECS cluster, RDS instance, or S3 bucket exists for this project
today. Every step below is a *specification* of what would need to happen, written from the actual
shape of the repository (Dockerfiles, CI, migrations, config surface) as of Sprint 15 — not a log of
work already done. Where a claim needs a real AWS account/CLI/console to verify, that is called out
explicitly instead of asserted.

**Author:** `devops-engineer` · **Task:** S15-DO-02 (`docs/10.` §9, ADR-0010) · **Companion documents:**
[`deploy-azure-container-apps.md`](./deploy-azure-container-apps.md) (the alternative path),
[`cloud-decision-brief.md`](./cloud-decision-brief.md) (cost/risk/recommendation — read that first if
you are the human choosing between the two).

**Upstream:** ADR-0010 (cloud-agnostic containers; provider choice deferred to the Sprint 16B gate),
ADR-0014 (PDF renderer is its own container), ADR-0021 (filtered-unique-index pre-flight dedup
pattern), `docs/db-conventions.md` §5 (migration policy), `docs/security/secrets-policy.md`,
`.github/workflows/ci.yml` (the CI this runbook builds on top of), `infra/docker/*` (the images this
runbook deploys as-is, unmodified).

---

## 0. What this runbook deploys, precisely

Confirmed directly against the repository, not assumed:

| Component | Repo artifact | Notes |
| --- | --- | --- |
| API | `infra/docker/api.Dockerfile` → `.NET 10` / Kestrel, listens on `8080` inside the container | No TLS, CORS, HSTS, or CSP inside the API itself — these are deploy-topology concerns (§5). `/health/live` and `/health/ready` are both mapped and `[AllowAnonymous]`. |
| Web (PWA) | `infra/docker/web.Dockerfile` → static Vite build served by nginx on `8080`, non-root | Cache headers are already correct **inside the image** (`infra/docker/web.nginx.conf`): immutable/hashed `/assets/`, `no-cache` on `index.html` and `sw.js`. A CDN placed in front of this must not override those headers (§5.3). |
| Database | MSSQL Server — the app targets `mcr.microsoft.com/mssql/server:2022-latest` in dev/CI (`infra/docker/mssql/Dockerfile`) | 25 EF Core migrations exist in `backend/src/CMPlus.Infrastructure/Migrations/` as of this sprint (verified by listing the directory directly — not the "21" figure sometimes quoted informally). **None have ever been applied to a persistent SQL Server.** They *have* been applied, and proven idempotent, against an ephemeral, throwaway SQL Server container inside CI (`migration-smoke` job) — that is real evidence the migration path works, but it is not the same as a production database existing anywhere. |
| Object storage | `IFileStorage` → `LocalDiskFileStorage` today (`backend/src/CMPlus.Infrastructure/Storage/`), local disk under `Storage:LocalRootPath` | **Defaults to the OS temp directory** (`Path.GetTempPath()/cmplus-file-storage`) if unconfigured — flagged by security review `sprint-15-owasp.md` M-3 and repeated in §6 below. The S3-API adapter (`IFileStorage` implementation actually calling AWS S3) **does not exist in this codebase yet** — it is scoped to post-gate work (`S16-DO-05` in `docs/10.`). This runbook describes S3 as the intended target and what has to change to get there; it is not describing shipped code. |
| PDF renderer | **Does not exist in this codebase.** No Playwright/Chromium reference anywhere in `backend/`. | ADR-0014 decided headless Chromium in its own container, but the feature that needs it (`S8-BE-03`, Executive Summary PDF export) has not been implemented — traded to a later sprint per ADR-0013's consequences. §7 describes the container topology this runbook *will* need the day that feature ships; do not read it as "already running." |
| Background services | `IdempotencyKeyCleanupService` (`backend/src/CMPlus.Infrastructure/Idempotency/`) — a `BackgroundService` registered as a singleton inside the API process, sweeps expired `IdempotencyKey` rows hourly (`Idempotency:CleanupInterval`), plus once immediately on startup | The only in-process background job that exists today. See §8 for the ECS scaling implication. |

---

## 1. Service mapping

| Concern | AWS service | Why |
| --- | --- | --- |
| Compute | **ECS on Fargate** (serverless containers, no EC2 to patch) | Matches ADR-0010's "no provider SDK, generic container" posture; Fargate needs nothing beyond an OCI image, which CI already produces. |
| Container registry | **Amazon ECR**, two repositories (`cmplus/api`, `cmplus/web`) | CI already pushes to GHCR (`.github/workflows/ci.yml` `images` job) tagged by commit SHA. ECS tasks can pull directly from a public/authenticated GHCR image with an `ecr:GetAuthorizationToken`-style pull-through cache or a registry credential — **or** CI adds an ECR-mirror push step. Either works; mirroring to ECR is the more conventional AWS-native path and is what §4 assumes, but it is an *additional* CI job that does not exist yet (see §9). |
| Database | **RDS for SQL Server** (Standard Edition, license-included) | The literal SQL Server engine, not a compatibility-layer PaaS variant — zero risk of hitting a T-SQL surface this app already depends on (filtered unique indexes, `rowversion`, `STRING_AGG`, ANSI NULL semantics per ADR-0021) that a managed variant might not support. Single-AZ is sufficient for a pilot; Multi-AZ is a later HA upgrade, not a day-one requirement. |
| Object storage | **S3** (one bucket, Block Public Access on, default SSE-S3 or SSE-KMS) | Behind the still-unwritten S3 adapter for `IFileStorage` (§0). |
| Secrets | **AWS Secrets Manager**, injected as ECS task-definition `secrets` (not `environment`) entries | Task definition `secrets` block resolves at container start from Secrets Manager ARNs — the value never appears in the task definition JSON itself, in CloudTrail, or in an image layer. |
| Ingress / TLS | **Application Load Balancer** (public subnets) terminating TLS via **ACM**, forwarding to the `web` and `api` target groups in private subnets | This is also where CORS/HSTS/CSP should terminate — none of that exists in the API today (`Program.cs` has `UseHttpsRedirection` but no `UseHsts`/`UseCors`; security review `sprint-15-owasp.md` §7 already flags this as "expected to terminate at the nginx proxy" — for this runbook, the ALB or an nginx sidecar is that proxy, and someone has to actually configure it; it is not automatic). |
| Logs / metrics | **CloudWatch Logs** (task `awslogs` driver) + **CloudWatch Alarms** on ALB 5xx rate and target-group p95/p99 latency | CLAUDE.md requires alerting on WBS endpoint latency > 100 ms specifically — that needs a CloudWatch metric filter or a custom EMF metric scoped to that route, not just the ALB's blended latency; not yet designed. |
| Scheduled/one-off jobs | **ECS `RunTask`** (standalone task using the API's own image, different entrypoint/command) | Used for the migration-apply step (§4) — never run as part of the API's own startup. |

---

## 2. What must be prepared in advance (AWS-specific)

Checklist — nothing here can be done from this repository; all require an actual AWS account and a
human with the right IAM permissions:

- [ ] An AWS account (or a dedicated sub-account) with billing owned by the human sponsor.
- [ ] IAM: a deploy role for CI (OIDC federation from GitHub Actions is the modern no-long-lived-key
      approach — `permissions: id-token: write` in the workflow, an IAM role trusting GitHub's OIDC
      provider, scoped to exactly the ECR push / ECS deploy actions needed) and a separate,
      narrower human/break-glass role.
- [ ] VPC design: at minimum 2 public + 2 private subnets across 2 AZs (ALB needs 2 AZs minimum),
      NAT gateway or NAT instance for the private subnets' outbound (image pulls, Secrets Manager
      calls) unless VPC endpoints are used instead (cheaper, more setup).
- [ ] A registered domain + ACM certificate for the API and web hostnames (or a wildcard cert).
- [ ] Quota check: default Fargate vCPU/memory quotas are generous for this scale, but RDS SQL
      Server Standard Edition license-included instances are not available in every account/region
      by default — verify the instance class you plan to use is actually orderable before the gate
      decision, not after.
- [ ] Region + data-residency decision: `ap-southeast-1` (Singapore) is the nearest AWS region to
      Thailand as of this writing; confirm this satisfies any contractual/regulatory data-residency
      requirement for the pilot customer (product docs reference Thai government procurement
      contexts — this is a real question, not a formality, and this runbook cannot answer it).
- [ ] **A real SQL Server to finally run the 25 migrations against.** Nothing has ever applied them
      outside an ephemeral CI container. RDS must exist and be reachable *before* §4 can run for the
      first time — this is the actual first-ever production-shaped migration apply for this project.
- [ ] Decide and provision the ECR repositories (or the GHCR-pull-into-ECS path) before CI is
      pointed at them.

---

## 3. Deploy sequence — first deployment (staging or production)

Numbered, in order. Steps marked **[HUMAN APPROVAL]** must not be automated without an explicit gate
per CLAUDE.md's human-in-the-loop rule.

1. **Provision networking** (VPC, subnets, security groups — API/web SGs allow inbound only from the
   ALB SG; RDS SG allows inbound only from the API task SG on 1433; nothing is open to `0.0.0.0/0`
   except the ALB's 443).
2. **Provision RDS for SQL Server**, note the endpoint, create the `sa`-equivalent admin credential
   in Secrets Manager immediately — never leave it as a console-visible plaintext value.
3. **Create the application database and least-privilege login**, the same shape as
   `infra/docker/mssql/init/01-create-database.sql` / `02-create-app-login.sql` do for local dev —
   these scripts are not directly portable to RDS master-user bootstrapping (RDS doesn't give you a
   root shell to run an entrypoint script), so they must be run as ordinary T-SQL against the RDS
   endpoint using the master credential, once, by a human or a bootstrap job. Store the resulting
   app-login connection string in Secrets Manager as its own secret — never reuse the master
   credential for the running application (`docs/security/secrets-policy.md` "least-privilege"
   principle, same reasoning as the local `cmplus_app` login).
4. **[HUMAN APPROVAL] Run the pre-flight dedup check, then apply migrations** — see §4. This is the
   single most important gate in this whole runbook: it is the first time these migrations touch
   real, persistent data.
5. **Provision S3 bucket**, Block Public Access on, and wire the (not-yet-written) S3 `IFileStorage`
   adapter's configuration once that adapter exists (`S16-DO-05`).
6. **Create Secrets Manager entries**: `Jwt__SigningKey` (fresh, high-entropy, never reused from any
   other environment — see §6), `Excel__CommercialLicenseKey` (see §6 — this one may not have a
   value yet, see §10 blocker list), the app DB connection string, S3 credentials/role.
7. **Register ECS task definitions** for `api` and `web`, each referencing the image by digest (not
   a mutable tag) and the Secrets Manager ARNs from step 6 via the task definition's `secrets` block.
8. **Create the ECS service** behind the ALB, health check path `/health/ready`, health check
   grace period generous enough for EF's connection warm-up (30–60s is a reasonable starting point,
   tune from observed cold-start time — not yet measured, see §10).
9. **Smoke test**: confirm `/health/live` and `/health/ready` both return 200 through the ALB,
   confirm a login round-trip, confirm the web bundle loads and its `index.html`/`sw.js` responses
   carry `Cache-Control: no-cache` end-to-end (verify the ALB/CDN, if any, isn't overriding — §5.3).
10. **[HUMAN APPROVAL] Promote traffic** (if this is a blue/green or weighted rollout) or confirm the
    single ECS service is stable (steady-state, no task restarts) before calling the deploy done.

### 3.1 Deploy sequence — subsequent deployments (steady state)

1. CI builds + tests + scans (existing `ci.yml`), pushes the image to GHCR by commit SHA (existing),
   then (once built) mirrors to ECR by the same tag — **this CI step does not exist yet**, it is new
   work for whoever executes this runbook (§9).
2. If the deploy includes a migration, generate/regenerate the idempotent SQL script
   (`dotnet ef migrations script --idempotent`, per `docs/db-conventions.md` §5), commit it to
   `artifacts/migrations/`, and run its pre-flight dedup check (§4) — **[HUMAN APPROVAL]** before
   applying to any shared environment, exactly as today, never implicitly at container start.
3. Update the ECS task definition to the new image digest; update the service (rolling deployment,
   ECS's default `minimumHealthyPercent`/`maximumPercent` control the rollover shape).
4. ECS waits for the new tasks to pass `/health/ready` before draining old ones — this is only as
   good as that endpoint actually meaning something; see §10's readiness-probe caveat.
5. Smoke test again; watch the CloudWatch alarms from §1 for a few minutes before considering the
   deploy final.

---

## 4. Migration application — the load-bearing step

This project's migration policy (`docs/db-conventions.md` §5) already mandates an idempotent,
human-approved apply — this runbook does not invent a new process, it specifies *where* that process
runs on AWS.

1. **Never apply migrations from application startup.** No `Database.MigrateAsync()` call exists in
   `CMPlus.WebApi/Program.cs` today (confirmed) — keep it that way. Migrations are applied by a
   separate, explicit job.
2. **Pre-flight dedup check, every time a migration adds a filtered-unique-index over a nullable
   discriminator (ADR-0021).** The concrete precedent already in this repo —
   `artifacts/migrations/20260811_sprint15_approvalpolicy_split_singleactive_index.PREFLIGHT.sql` —
   is a `GROUP BY ... HAVING COUNT(*) > 1` query that must return **zero rows** before the paired
   migration script is applied, because `CREATE UNIQUE INDEX` validates uniqueness over *existing*
   rows and fails outright (not gracefully) if the corruption it's meant to prevent already exists.
   Run this pattern for *any* future migration of the same shape (the general rule is stated in
   `docs/db-conventions.md` §6's `ApprovalPolicy` row: "any future filtered unique index whose key
   includes a nullable column must be split the same way"). Concretely on AWS: run the
   `.PREFLIGHT.sql` file via `RunTask` (a one-off Fargate task using the API image with an override
   command that shells out to `sqlcmd`, or an AWS Systems Manager `Run Command`/session against a
   bastion with `sqlcmd` installed) against the RDS endpoint, inspect the result set, and stop if it
   returns any row — remediate per that file's own documented one-row-at-a-time procedure before
   proceeding.
3. **Apply the idempotent script**, e.g. `artifacts/migrations/<name>.sql`, with a tool that runs
   with `QUOTED_IDENTIFIER ON` (`docs/db-conventions.md` §5 flags this explicitly — filtered indexes
   need it, and bare `sqlcmd -i` runs with it off by default; use `sqlcmd -I` capital-I, or an
   ADO.NET-based runner). This is the exact same script CI's `migration-smoke` job already proves
   idempotent against a fresh container — running it against RDS is the first time it runs against a
   database that is not thrown away five minutes later.
4. **For the very first deployment**, this means applying all 25 migrations in sequence (they are
   individually idempotent and cumulative, so this is one script-per-migration run in order, or a
   single generated end-to-end idempotent script covering the whole history — `dotnet ef migrations
   script --idempotent` with no `--from`/`--to` bounds produces that in one file).
5. **[HUMAN APPROVAL] required before this step touches any shared database** — dev-local and CI's
   ephemeral container are exempt (nothing shared, nothing persistent); staging and production are
   not.
6. Record the result (which migrations applied, pre-flight check outcome, who approved) — this
   project has no dedicated deploy-log tooling yet; at minimum, the CD job's own execution log serves
   as the record until something better exists.

---

## 5. Networking, TLS, and the PWA cache headers

### 5.1 TLS/HSTS/CORS/CSP — a real gap, not yet anyone's job in code

`Program.cs` has `UseHttpsRedirection()` but **no `UseHsts()`, no `UseCors()`/`AddCors()`, and no CSP
middleware** (confirmed by direct grep; also independently flagged in
`docs/security/reviews/sprint-15-owasp.md` §7 as "expected to terminate at the nginx proxy — code-
verified only", meaning nobody has actually stood up that proxy yet either). On ECS, the natural home
for HSTS response headers and a CSP is either the ALB (limited — ALBs can't rewrite response headers
without a Lambda@Edge-style hook, which AWS ALBs don't support directly) or an nginx/Envoy sidecar in
front of the API task. **This runbook does not resolve that gap — it flags it as a concrete decision
someone (`system-architect` + `devops-engineer`) needs to make before production traffic, not
something ECS solves by default.**

### 5.2 Ingress topology

```
Internet → ALB (443, ACM cert) → target group "web" (nginx:8080) → ECS task "web"
                                → target group "api" (kestrel:8080) → ECS task "api"
```
Path-based routing (`/api/*` → api target group, everything else → web target group) is the simplest
shape and matches how the local dev proxy in `web/vite.config.ts` already routes.

### 5.3 PWA cache headers — already correct in the image, watch the layer above it

`infra/docker/web.nginx.conf` already sets the right headers at the origin: immutable, 1-year cache
for hashed `/assets/*`, `no-cache` for `index.html` and any `service-worker`/`sw.js` file. **If a CDN
(CloudFront) is later placed in front of the ALB**, its default caching behavior must be configured to
respect origin `Cache-Control` headers rather than substituting its own defaults — a CDN that caches
`index.html` regardless of the origin's `no-cache` header would silently reintroduce the exact "stale
service worker" incident CLAUDE.md calls out as a production incident, and the origin would be doing
everything right while the CDN quietly undoes it. Not provisioned in this pilot-scale plan (§ cost in
the decision brief) — noted here so it isn't forgotten if/when one is added.

### 5.4 The PWA build itself needs `VITE_BUILD_ID` wired, and it isn't yet

`web/vite.config.ts`'s service-worker plugin reads `process.env.VITE_BUILD_ID` at build time to stamp
the service worker's own cache version (`web/src/sw.ts`); it falls back to `dev-<timestamp>` if unset.
**Neither `infra/docker/web.Dockerfile` nor `.github/workflows/ci.yml` currently sets this variable**
(confirmed — no `ARG`/`ENV VITE_BUILD_ID` in the Dockerfile, no `env:`/`--build-arg` in the CI build
step). This is tracked as `S13-DO-01` in `docs/10.` and is still open. It does not currently break
anything (each CI-built image still gets *some* distinct version string), but it means the shipped
service worker's version identity is not deterministically tied to the git commit that produced it,
which undermines auditability ("which SW version is live" should be answerable from the image tag
alone). **Fix before this runbook is executed for real**: add `ARG VITE_BUILD_ID` /
`ENV VITE_BUILD_ID=$VITE_BUILD_ID` to `web.Dockerfile` before its `RUN npm run build` line, and pass
`--build-arg VITE_BUILD_ID=${{ github.sha }}` from the CI `images` job. This is a small, separate,
already-tracked change — not part of this runbook's own deliverable, but a precondition worth closing
before Sprint 16A.

---

## 6. Secrets — what goes where, and what must never happen

| Secret | Source of truth on AWS | Never |
| --- | --- | --- |
| `Jwt__SigningKey` | Secrets Manager, generated fresh for this environment (≥ 32 bytes, high entropy) | Reused between dev/staging/production, or the placeholder value in `infra/docker/.env.example` |
| DB connection string (`ConnectionStrings__CmPlusDatabase`) | Secrets Manager, built from the RDS endpoint + the least-privilege `cmplus_app`-equivalent login (§3 step 3) | The RDS master/admin credential — the running app must never hold admin rights on the database |
| `Excel__CommercialLicenseKey` | Secrets Manager, **once a real commercial EPPlus license is procured** — see §10, this is currently unset because no license exists | A value that doesn't exist yet — do not fabricate a placeholder that looks configured |
| S3 access | An ECS task IAM role scoped to exactly that one bucket (read/write/delete on its objects only) | A static access key/secret pair baked into a secret when a task role can do this with zero long-lived credential at all |
| GHCR/ECR pull credentials | Task execution role (`ecr:GetAuthorizationToken`, `ecr:BatchGetImage`, etc.) if pulling from ECR; a registry credential secret if pulling from GHCR directly | A personal access token committed anywhere |

Standing rule, restated from `docs/security/secrets-policy.md`: nothing above is ever a Docker `ARG`/
`--build-arg` (build args persist in image history) and nothing above is ever baked into
`appsettings.json` — configuration reaches the app through `IConfiguration`/environment only, which
for ECS means the task definition's `secrets` block (resolved from Secrets Manager at container
start), never the `environment` block (which is visible in the task definition JSON and CloudTrail).

---

## 7. The PDF renderer — intended topology (not yet built)

ADR-0014 requires the headless-Chromium PDF renderer to be its own container, separate from the API
image, specifically because Phase 0 set a 300 MB API image budget and a real browser exceeds that on
its own. **As of this sprint, no such feature exists in the codebase** (§0) — this section describes
what to stand up *when* it does, so the topology is decided ahead of time rather than improvised under
schedule pressure.

- A second ECS service/task definition, `pdf-renderer`, built from a Playwright-with-browsers base
  image (Microsoft's official `mcr.microsoft.com/playwright:...` images already ship the pinned
  Chromium build Playwright expects, and the fonts problem — ADR-0014's own DoD requires the renderer
  to actually ship IBM Plex Sans Thai / Bai Jamjuree, or Thai text renders as tofu boxes — is solved by
  baking those font files into that image, not the API image).
- The API calls it over an internal-only HTTP endpoint (no public ingress for this service; security
  group allows inbound only from the API task's security group) with an explicit timeout and a
  surfaced failed/generating state to the user (`S8-FE-03`'s own DoD already requires this UI state —
  this runbook doesn't invent it, just confirms the container boundary the API is calling across).
- Not covered by this runbook's cost estimate in the decision brief (feature doesn't exist yet); when
  it ships, budget an additional small always-on or on-demand Fargate task for it.

---

## 8. Background services vs. ECS scaling — the correctness trap

`IdempotencyKeyCleanupService` (§0) is a singleton `BackgroundService` **inside the API process
itself** — there is no separate worker service, no leader election, and no distributed lock around it
(confirmed by reading the class directly). It runs once on startup and then on an hourly timer for as
long as that process is alive.

**On ECS Fargate with `desiredCount > 1`** (multiple API tasks for availability), **every task runs
its own independent copy of this sweep.** Concretely:

- The sweep's actual work (`DELETE` rows past their retention window) is naturally idempotent —
  deleting an already-deleted or already-expired row is a no-op, so running it N times concurrently
  from N tasks does not corrupt data or double-charge anything.
- It *does* mean N tasks each issue a `DELETE` query against the shared RDS instance roughly once an
  hour, and — because the service also sweeps once immediately on startup — a rolling deployment that
  cycles several tasks in a short window causes a small burst of redundant sweeps, not a steady drip.
  At this project's pilot scale this is noise, not a real load problem; it stops being noise if
  `desiredCount` grows much larger without anyone revisiting this.
- **The actual correctness risk is not data corruption, it's silent redundant load with no
  supervision** — nothing currently logs "N tasks are sweeping the same table" as a signal, so this
  would only surface as an unexplained RDS CPU/IO blip someone has to go dig for. If/when `CpmRun`
  retention (`docs/db-conventions.md` §7.1, explicitly "a sketch, not a decision" today) or any other
  background job is added, **check whether it is naturally idempotent under concurrent execution the
  way this one is before assuming multi-task ECS is safe for it** — a job that isn't (e.g. one that
  reads-then-writes without an atomic guard) would need either `desiredCount=1` pinned specifically for
  that responsibility, a distributed lock, or a genuine move to a separate scheduled ECS task
  (EventBridge Scheduler → `RunTask`) instead of an in-process `BackgroundService`.
- **Recommendation for this runbook**: leave `desiredCount ≥ 2` for the API's HTTP-serving role (that
  is the availability point of running more than one task), and treat the idempotency sweep as
  acceptable collateral redundancy at this scale — but write this down as a known, accepted trade-off
  rather than an oversight, and revisit if RDS load ever becomes a real constraint.

---

## 9. What is genuinely new work, not yet built, to execute this runbook

So this doesn't read as more finished than it is:

- No `cd.yml` (or equivalent CD workflow) exists in `.github/workflows/` — `ci.yml` builds, tests,
  scans, and publishes to GHCR on push to `main`; nothing deploys anywhere today. This runbook assumes
  such a workflow gets written (Sprint 16 territory per `docs/10.` §13), not that it exists.
- No ECR mirror step exists in CI (§1).
- No S3 `IFileStorage` adapter exists in `CMPlus.Infrastructure` (§0) — `S16-DO-05`.
- No AWS Secrets Manager / IAM / networking exists — nothing to point at yet (§2).
- `VITE_BUILD_ID` is not wired into the web build (§5.4) — `S13-DO-01`, small and separately tracked.

---

## 10. Cross-cutting go-live blockers (apply regardless of AWS vs. Azure)

These gate production go-live on **either** provider — restated here so this runbook is self-
contained, full detail and ownership in `cloud-decision-brief.md` §3:

1. **EPPlus is still on the Polyform Noncommercial license** (`docs/security/reviews/sprint-15-
   owasp.md` M-2, ADR-0014, originally Sprint-3 finding L-06). No commercial key is configured. A
   production deploy with a real customer using the Excel import/export feature under this license is
   a legal exposure, not a technical one — this runbook cannot close it; procurement + `backend-
   developer` wiring `Excel__CommercialLicenseKey` must happen first.
2. **`Storage:LocalRootPath` defaults to the OS temp directory** (`sprint-15-owasp.md` M-3), which on
   a Linux container is `/tmp` — ephemeral (wiped on container restart, meaning every photo/attachment
   written since the last restart is lost) and not backed by any provisioned volume. §0/§2 already flag
   that the real S3 adapter isn't built yet; **even before that lands**, if this app is ever deployed
   to ECS with local-disk storage still in play (e.g. as an interim step), the container needs a real
   persistent, non-temp mount (an EFS-backed volume for Fargate, since Fargate has no durable local
   disk across task replacement) with `Storage:LocalRootPath` pointed at it explicitly — this is not
   optional and not automatic.
3. **25 migrations have never applied to a persistent SQL Server anywhere.** §4 is the actual first
   time. Treat the first production migration apply with the seriousness that implies — it is not "just
   running the same script again," it is the first time.
4. **`/health/ready` does not check any dependency today.** `Program.cs` registers `AddHealthChecks()`
   with zero checks added (no DbContext health check — deliberately, to avoid pulling EF Core into
   WebApi per the architecture layering rule; confirmed by grep, no `AddDbContextCheck`/`AddSqlServer`
   call exists anywhere). This means `/health/ready` currently behaves identically to `/health/live` —
   an ECS target-group health check pointed at it will mark a task healthy even if it cannot reach the
   database. This is a real gap for a rolling deployment's safety net and should be closed (add a real
   DB connectivity check, in whichever layer keeps the architecture fitness tests green) before relying
   on `/health/ready` to gate a production rollout.
