# Runbook B — Azure Container Apps

**Status: PLANNING DOCUMENT. Nothing in this runbook has been provisioned, deployed, or executed
against Azure.** No Azure subscription, resource group, Container Apps Environment, Azure SQL
Database, or Storage Account exists for this project today. Every step below is a *specification*,
written from the actual shape of the repository (Dockerfiles, CI, migrations, config surface) as of
Sprint 15 — not a log of work already done. Where a claim needs a real Azure subscription/CLI/portal
to verify, that is called out explicitly instead of asserted.

**Author:** `devops-engineer` · **Task:** S15-DO-02 (`docs/10.` §9, ADR-0010) · **Companion documents:**
[`deploy-aws-ecs.md`](./deploy-aws-ecs.md) (the alternative path),
[`cloud-decision-brief.md`](./cloud-decision-brief.md) (cost/risk/recommendation — read that first if
you are the human choosing between the two).

**Upstream:** ADR-0010, ADR-0014, ADR-0021, `docs/db-conventions.md` §5, `docs/security/secrets-
policy.md`, `.github/workflows/ci.yml`, `infra/docker/*`.

---

## 0. What this runbook deploys, precisely

Same application, same artifacts as the AWS runbook §0 — restated briefly, full detail there:

- API: `infra/docker/api.Dockerfile`, .NET 10 Kestrel on `:8080`, no TLS/CORS/HSTS/CSP inside the
  image, `/health/live` + `/health/ready` mapped and anonymous.
- Web: `infra/docker/web.Dockerfile`, static Vite build on nginx `:8080`, correct cache headers
  already baked into `infra/docker/web.nginx.conf`.
- Database: MSSQL, 25 EF Core migrations exist, **none ever applied to a persistent SQL Server** —
  only proven idempotent against CI's ephemeral, throwaway container (`migration-smoke` job).
- Object storage: `LocalDiskFileStorage` today, defaulting to the OS temp dir (M-3, §6). The
  Azure Blob adapter for `IFileStorage` **does not exist in this codebase yet** — post-gate work.
- PDF renderer: **does not exist in this codebase.** ADR-0014's container-separation decision has
  nothing built against it yet — §7 describes intended topology only.
- Background service: `IdempotencyKeyCleanupService`, in-process singleton `BackgroundService`,
  hourly sweep + one on startup — see §8 for the Container Apps scaling implication, which is a
  materially different risk shape here than on ECS because of scale-to-zero.

---

## 1. Service mapping

| Concern | Azure service | Why |
| --- | --- | --- |
| Compute | **Azure Container Apps** (Consumption or Dedicated plan, on a Container Apps Environment) | Bundles ingress, TLS termination, revision management, and autoscaling (including scale-to-zero) into one managed construct — materially less infrastructure to hand-assemble than ECS+ALB+VPC (§2 has a shorter prep list because of this). |
| Container registry | **Azure Container Registry (ACR)**, Basic tier is sufficient at this scale | Same mirror-vs-pull-direct choice as the AWS runbook: CI already pushes to GHCR; either add an ACR-mirror push step, or configure the Container App to pull from GHCR with a registry credential. Neither exists in CI yet (§9). |
| Database | **Azure SQL Database** (a General Purpose, provisioned or serverless tier — not SQL Managed Instance, which is a heavier/pricier near-100%-compatibility option this app does not need) | A PaaS SQL Server-compatible engine, not the boxed engine itself — verified against what this app actually uses: `rowversion`, filtered unique indexes (ADR-0021's `WHERE IsActive = 1 AND ProjectId IS NULL` pattern), `STRING_AGG`, ANSI NULL semantics are all supported on Azure SQL Database's current compatibility level. This app uses no SQL Agent jobs, no cross-database queries, and no CLR — the features that would actually force a step up to Managed Instance. Compatibility risk here is genuinely low, not zero-by-definition the way RDS is (see the decision brief for how this trades off against cost). |
| Object storage | **Azure Blob Storage**, one private container, no public read access | Behind the still-unwritten Blob adapter for `IFileStorage`. |
| Secrets | **Azure Key Vault**, referenced by the Container App via a **managed identity** (no client secret to rotate for the app-to-Key-Vault link itself) | Container Apps has native Key Vault secret references — the running container's environment variable resolves from Key Vault at start, and the container never holds a static Key Vault credential. |
| Ingress / TLS | **Container Apps' built-in ingress** (managed TLS certificate, or bring-your-own via a custom domain binding) | This *does* give HSTS/TLS termination "for free" at the platform layer, unlike the AWS ALB, which does not rewrite response headers — still does not give CORS/CSP for the API, which remains an application- or Container-Apps-ingress-rule concern to configure explicitly, not automatic. |
| Logs / metrics | **Application Insights + Log Analytics workspace**, with alert rules on request failure rate and p95/p99 latency | Same CLAUDE.md requirement as the AWS runbook (alert on WBS endpoint latency > 100 ms) needs a route-scoped metric, not just the platform's blended request latency — not yet designed on either platform. |
| Scheduled/one-off jobs | **Azure Container Apps Jobs** (a first-class job resource, not a hack) | Used for the migration-apply step (§4) — Container Apps Jobs are actually a slightly cleaner fit for this than ECS `RunTask`, since they're a named, schedulable, manually-triggerable resource rather than an ad hoc task-definition override. |

---

## 2. What must be prepared in advance (Azure-specific)

- [ ] An Azure subscription with billing owned by the human sponsor; a resource group for this
      project (naming convention TBD by whoever executes this).
- [ ] Identity: a service principal or (preferred) **federated OIDC credential for GitHub Actions**
      (`azure/login@v2` with `client-id`/`tenant-id`/`subscription-id`, no long-lived secret) scoped
      to exactly the resource group and the ACR push / Container Apps deploy actions needed.
- [ ] A Container Apps Environment (this creates the underlying Log Analytics workspace + VNet
      integration point in one step — less manual subnet/route-table work than the AWS VPC design).
- [ ] A registered domain + certificate (Container Apps can manage this via a free managed
      certificate on a custom domain, or bring an existing one) for the API and web hostnames.
- [ ] Quota check: verify the target region has capacity for the chosen Azure SQL Database tier and
      Container Apps Consumption plan before relying on this at the gate decision.
- [ ] Region + data-residency decision: **Southeast Asia** (Singapore) is Azure's nearest region to
      Thailand as of this writing; same open question as the AWS runbook about whether this satisfies
      any contractual/regulatory residency requirement — this runbook cannot answer that, a human must.
- [ ] **A real SQL Server (Azure SQL Database) to finally run the 25 migrations against** — same
      blocker as the AWS runbook, restated because it is provider-independent and must not be
      forgotten just because this runbook is shorter in other places.
- [ ] Decide the ACR-mirror-vs-GHCR-pull-direct question (§1) before CI is pointed at anything.
- [ ] If the pilot customer's own IT environment is Microsoft/Entra ID-centric (a real possibility —
      see the decision brief), decide now whether SSO/Entra ID integration is an actual near-term
      requirement, since that would change identity-provider choices beyond what this runbook covers
      (this app's own auth is a self-issued JWT today, per Sprint 2 — federating that to Entra ID is
      out of scope here and would need its own design work).

---

## 3. Deploy sequence — first deployment (staging or production)

1. **Create the resource group + Container Apps Environment** (this also stands up the Log Analytics
   workspace referenced in §1).
2. **Provision Azure SQL Database**, note the server name/connection endpoint, create the admin
   credential in Key Vault immediately.
3. **Create the application database and least-privilege login** — same caveat as the AWS runbook:
   `infra/docker/mssql/init/*.sql` are written for the local Docker entrypoint, not directly portable
   to a PaaS bootstrap; run the equivalent T-SQL (`CREATE USER ... FROM LOGIN`, granted the minimum
   this app needs — today's local dev grants `db_owner` for Phase-0 simplicity per `docs/db-
   conventions.md` §7, which is itself flagged there as something to narrow later) against the Azure
   SQL Database using the admin credential, once. Azure SQL Database's own contained-database model
   means the login/user step is slightly different in mechanics from box SQL Server (contained users
   are more idiomatic here than server-level logins) — whoever executes this should use Azure SQL
   Database's own recommended approach (Azure AD-based or SQL auth contained user) rather than
   assuming the on-box script translates line-for-line.
4. **[HUMAN APPROVAL] Run the pre-flight dedup check, then apply migrations** — see §4.
5. **Provision the Blob Storage account and private container**, wire the (not-yet-written) Blob
   `IFileStorage` adapter's configuration once it exists (`S16-DO-05`, provider-specific half).
6. **Create Key Vault secrets**: `Jwt__SigningKey` (fresh per environment), `Excel__CommercialLicenseKey`
   (see §10 — likely still unset, do not fabricate one), the app DB connection string, Blob Storage
   access (prefer a managed identity with an RBAC role assignment on the storage account over an
   account key, exactly the same "no static credential where a managed identity can do it" reasoning
   as the AWS task-role approach).
7. **Deploy the Container Apps** (`api`, `web`), each referencing the image by digest, secrets
   referenced from Key Vault via managed identity, ingress rules configured (path-based or
   hostname-based routing to the two apps), health probe pointed at `/health/ready`.
8. **Smoke test**: `/health/live`/`/health/ready` return 200 through the Container Apps ingress,
   login round-trip works, web bundle cache headers verified end-to-end (Container Apps' own ingress
   does not rewrite response headers by default, but confirm — same caution as the CDN note in the
   AWS runbook §5.3 applies if a separate CDN/Front Door is ever added in front).
9. **[HUMAN APPROVAL] Confirm traffic is stable** on the new revision (Container Apps' revision model
   supports weighted traffic splitting natively, which is a genuinely convenient built-in canary
   mechanism worth using even for the first real deploy) before considering the deploy done.

### 3.1 Deploy sequence — subsequent deployments (steady state)

1. CI builds + tests + scans (existing), pushes to GHCR (existing), mirrors to ACR (new work, §9).
2. If the deploy includes a migration: regenerate the idempotent script, run its pre-flight dedup
   check (§4), **[HUMAN APPROVAL]** before applying — never implicit at container start, same rule
   as the AWS runbook and as `docs/db-conventions.md` §5 already states project-wide.
3. Deploy a **new revision** of the Container App pointing at the new image digest. Container Apps'
   revision model means the old revision keeps running until traffic is explicitly shifted — this
   makes a controlled rollout (and rollback, §11) a native platform feature rather than something to
   build.
4. Health-probe gate applies the same way it does on ECS — and has the same caveat (§10 item 4):
   `/health/ready` doesn't check anything today.
5. Smoke test, watch alerts for a few minutes before calling it final.

---

## 4. Migration application — the load-bearing step

Identical policy to the AWS runbook §4 (this is provider-independent, restated for a self-contained
document, not duplicated by accident):

1. Never applied from application startup — no such call exists in `Program.cs` and none should be
   added.
2. **Pre-flight dedup check** before any migration creating a filtered unique index over a nullable
   discriminator column (ADR-0021's own precedent:
   `artifacts/migrations/20260811_sprint15_approvalpolicy_split_singleactive_index.PREFLIGHT.sql`).
   On Azure, run this via an **Azure Container Apps Job** (one-off, using the API image with an
   override command invoking `sqlcmd`, or `az sql db query`-style tooling) against the Azure SQL
   Database — inspect the result, stop and remediate per that file's documented procedure if it
   returns any row.
3. Apply the idempotent script (`artifacts/migrations/<name>.sql`) with a runner that has
   `QUOTED_IDENTIFIER ON` (same `docs/db-conventions.md` §5 caveat — `sqlcmd -I`, capital I, or an
   ADO.NET-based runner; do not use bare `sqlcmd -i`).
4. First deployment applies all 25 migrations in order (cumulative, individually idempotent, or one
   generated end-to-end script).
5. **[HUMAN APPROVAL]** required before touching any shared Azure SQL Database.
6. Record the outcome the same way as the AWS runbook — no dedicated deploy-log tooling exists yet.

---

## 5. Networking, TLS, and the PWA cache headers

### 5.1 TLS/HSTS — better default than ALB, CORS/CSP still an app concern

Container Apps' managed ingress terminates TLS and can be configured to require HTTPS with an HSTS
response header at the platform layer — a genuine advantage over the AWS ALB, which does not rewrite
response headers on its own. This still does **not** give the API a CORS policy or a Content-Security-
Policy header for free; `Program.cs` has neither (`UseCors`/`AddCors` absent, confirmed by grep), and
that gap is provider-independent — someone still has to decide and configure it, whether that lands as
an ingress-level rule or inside the API itself.

### 5.2 Ingress topology

```
Internet → Container Apps ingress (managed TLS) → "web" Container App (nginx:8080)
                                                  → "api" Container App (kestrel:8080)
```
Two separate Container Apps with their own ingress/hostnames (or path-based routing at a shared
ingress, if using an Azure Front Door/Application Gateway in front) — mirrors the AWS ALB's two
target-group split conceptually, with the routing decision itself simpler here since Container Apps
handles TLS/cert renewal natively per app.

### 5.3 PWA cache headers — same caution as the AWS runbook

`infra/docker/web.nginx.conf` already sets correct cache headers at the origin (§0). If Azure Front
Door or any CDN is later placed in front of the Container App serving `web`, its caching rules must
be configured to honor origin `Cache-Control` headers rather than substitute platform defaults — same
"stale service worker is a production incident" risk as the AWS runbook §5.3, not solved automatically
by choosing Azure.

### 5.4 `VITE_BUILD_ID` — same open gap, provider-independent

Identical situation to the AWS runbook §5.4: `web/vite.config.ts` reads `process.env.VITE_BUILD_ID`
at build time; neither `infra/docker/web.Dockerfile` nor `.github/workflows/ci.yml` currently sets it
(`S13-DO-01`, still open). Fix (add `ARG`/`ENV VITE_BUILD_ID` to the Dockerfile, pass
`--build-arg VITE_BUILD_ID=${{ github.sha }}` from CI) is identical regardless of which cloud consumes
the resulting image — not duplicated work, just genuinely provider-independent.

---

## 6. Secrets — what goes where, and what must never happen

| Secret | Source of truth on Azure | Never |
| --- | --- | --- |
| `Jwt__SigningKey` | Key Vault, fresh per environment | Reused across environments, or the `.env.example` placeholder value |
| DB connection string (`ConnectionStrings__CmPlusDatabase`) | Key Vault, built from the Azure SQL Database endpoint + the least-privilege app login/contained user (§3 step 3) | The Azure SQL admin credential — the running app must never hold admin rights |
| `Excel__CommercialLicenseKey` | Key Vault, **once a real commercial EPPlus license is procured** (§10 — currently unset) | A fabricated placeholder that looks like it's configured when it isn't |
| Blob Storage access | A **managed identity** on the Container App with an RBAC role (`Storage Blob Data Contributor`, scoped to the one storage account/container) | A static storage account key baked into a secret when a managed identity can do this with zero long-lived credential |
| GHCR/ACR pull credentials | Container Apps' managed-identity-based ACR pull (no credential needed if the identity has `AcrPull` role), or a registry credential secret if pulling from GHCR directly | A personal access token committed anywhere |

Same standing rule as the AWS runbook and `docs/security/secrets-policy.md`: nothing above is ever a
Docker `ARG`/`--build-arg`, nothing above is ever baked into `appsettings.json`.

---

## 7. The PDF renderer — intended topology (not yet built)

Identical starting point to the AWS runbook §7: **this feature does not exist in the codebase yet**
(`S8-BE-03` traded to a later sprint). When it ships:

- A second Container App, `pdf-renderer`, from a Playwright-with-browsers base image (same
  `mcr.microsoft.com/playwright:...` family works identically here — it's a plain OCI image, nothing
  Azure-specific), with the required Thai fonts (IBM Plex Sans Thai, Bai Jamjuree) baked in per
  ADR-0014's own DoD, or Thai text renders as tofu boxes.
- Configured with **no external ingress** (Container Apps supports internal-only ingress scoped to the
  Container Apps Environment's own VNet — the API reaches it over the internal environment network,
  nothing public-facing).
- Called by the API over HTTP with an explicit timeout and a surfaced generating/failed UI state
  (`S8-FE-03`'s own DoD), same as the AWS runbook.
- Not covered by this runbook's cost figures in the decision brief (feature doesn't exist); budget an
  additional Container App, likely scale-to-zero-eligible since PDF export is a bursty, on-demand
  action rather than constant traffic — a genuinely good fit for Container Apps' consumption model
  specifically, worth remembering when this feature is actually built.

---

## 8. Background services vs. Container Apps scaling — the correctness trap, sharper here than on ECS

Same starting fact as the AWS runbook §8: `IdempotencyKeyCleanupService` is a singleton in-process
`BackgroundService`, no leader election, no distributed lock, runs on every replica that exists.

**Container Apps' scale-to-zero makes this a genuinely different risk shape than ECS, not just the
same trap restated:**

- **Multiple replicas (minReplicas ≥ 2):** identical situation to the AWS runbook — the sweep's own
  work is idempotent under concurrent execution, so this is redundant load, not corruption, and is
  acceptable collateral at this project's pilot scale.
- **Scale-to-zero (minReplicas = 0):** during any period with no HTTP traffic, **there are zero
  running replicas, so the sweep does not run at all** — not "runs less often," genuinely does not
  execute. This is *not* a data-safety problem for this specific job: nothing depends on the sweep
  having run recently (`IdempotencyKeyCleanupService`'s own code comment states this explicitly — "a
  failed sweep is never fatal to the API... retention is a housekeeping concern, not a correctness
  one"), and the retention windows (90 days completed, 1 day abandoned in-progress,
  `docs/db-conventions.md` §10) are generous enough that a scaled-to-zero night or weekend does not
  meaningfully erode them. **But this must be an explicit, understood trade-off, not a surprise** — if
  a future background job is added that genuinely needs to run on a schedule regardless of traffic
  (e.g. the sketched-but-unbuilt `CpmRun` retention job, `docs/db-conventions.md` §7.1), an in-process
  `BackgroundService` inside a scale-to-zero Container App is the **wrong** place to put it — that
  belongs in a Container Apps Job on a cron trigger, which runs on its own schedule independent of
  whether the API has any active replica.
- **Cold start compounds this.** A scale-to-zero Container App's first request after idle pays a real
  cold-start cost (container start + Kestrel startup + EF Core's first-connection warm-up) — not yet
  measured for this app, but ASP.NET Core cold starts in the low single-digit seconds are typical for
  an image this size. For a construction PM tool used in client meetings or site walk-throughs, a
  multi-second stall on the first request of the day is a real, user-visible cost that a
  minReplicas ≥ 1 configuration avoids entirely, at the cost of losing the idle-time cost savings that
  make scale-to-zero attractive in the first place. This exact trade-off is worked through with actual
  numbers in `cloud-decision-brief.md` §2 and §4 — it is one of the concrete inputs to the
  recommendation there, not a side note.
- **Recommendation for this runbook**: for a pilot, prefer `minReplicas = 1` over `minReplicas = 0`
  specifically to avoid the cold-start/readiness-probe-gap interaction (§10 item 4 — a health probe
  that doesn't check the DB, combined with a cold-starting container whose DB connection genuinely
  isn't warm yet, is a plausible source of a confusing false-healthy-but-erroring window right after a
  scale-from-zero event). If cost pressure later justifies scale-to-zero, re-verify `/health/ready`
  actually gates on DB connectivity before flipping it on (§10 item 4 should be closed first).

---

## 9. What is genuinely new work, not yet built, to execute this runbook

- No CD workflow exists in `.github/workflows/` (§9 of the AWS runbook — identical fact,
  provider-independent).
- No ACR mirror step exists in CI.
- No Blob Storage `IFileStorage` adapter exists in `CMPlus.Infrastructure` — `S16-DO-05`.
- No Azure subscription/Key Vault/Container Apps Environment exists — nothing to point at yet (§2).
- `VITE_BUILD_ID` is not wired into the web build (§5.4) — `S13-DO-01`.

---

## 10. Cross-cutting go-live blockers (apply regardless of AWS vs. Azure)

Identical list to the AWS runbook §10 — restated here so this document is self-contained, full detail
and ownership in `cloud-decision-brief.md` §3:

1. **EPPlus is still on the Polyform Noncommercial license** (`sprint-15-owasp.md` M-2, ADR-0014,
   originally Sprint-3 L-06). Legal/procurement blocker, not something this runbook can close.
2. **`Storage:LocalRootPath` defaults to the OS temp directory** (M-3). If local-disk storage is ever
   deployed to Container Apps before the Blob adapter lands, it needs a real persistent mount — Azure
   Container Apps supports Azure Files-backed volumes for exactly this; a container's own local
   filesystem does not survive a replica replacement, so `/tmp` (or any un-mounted path) loses every
   file on the next scale event or redeploy, which is strictly worse under Container Apps' more
   frequent scaling behavior than it would be on a fixed-`desiredCount` ECS service.
3. **25 migrations have never applied to a persistent SQL Server anywhere.** §4 is the first time,
   same as the AWS runbook.
4. **`/health/ready` does not check any dependency today** — confirmed identically to the AWS runbook
   (`AddHealthChecks()` with zero checks registered, no `AddDbContextCheck`/`AddSqlServer` anywhere).
   This matters *more* on Container Apps than on a fixed-replica ECS service specifically because of
   §8's scale-to-zero interaction — a probe that can't detect "DB not ready yet" is a bigger gap when
   cold starts are a routine, expected event rather than a rare one.
