# Cloud Decision Brief — AWS ECS Fargate vs. Azure Container Apps

**Status: ADVISORY PLANNING DOCUMENT.** Nothing described here has been provisioned, priced with a
real quote, or executed. Every cost figure below is a labeled, order-of-magnitude estimate derived
from publicly known pricing structures at the time of writing, not a vendor quote — verify against
each provider's current pricing calculator before committing budget. This document does not make the
provider choice; per ADR-0010(d), that decision belongs to the human at the Sprint 16B gate
(`S16-DO-04` in `docs/10.`). It gives a recommendation with reasoning, explicitly labeled as advisory.

**Author:** `devops-engineer` · **Task:** S15-DO-02 · **Companion documents:**
[`deploy-aws-ecs.md`](./deploy-aws-ecs.md), [`deploy-azure-container-apps.md`](./deploy-azure-container-apps.md)
— both are fully worked out and ready to execute once a provider is chosen; this brief exists so that
choice can be made with real information rather than a coin flip.

---

## 1. Scale assumption used for every cost figure below

Neither runbook nor this brief has real usage data (no production deployment has ever existed). The
figures in §2 are anchored to a stated, explicit **pilot/early-production** scale, chosen to match
what `docs/10.`'s own Sprint 16A staging + a first real customer rollout would plausibly look like —
change these assumptions and the numbers move with them:

- **3–5 tenant organizations**, **~50–150 total named users**, roughly **15–25 concurrent users at
  peak** (a single project team or a few, not a large multi-project portfolio yet).
- **Single region**, no multi-region DR requirement yet, no Multi-AZ/zone-redundant HA requirement
  for a pilot (a later, explicit upgrade once the customer base and contract terms justify it).
- **Business-hours traffic pattern** — Thai working hours, near-idle nights and weekends, consistent
  with how a construction PM tool is actually used (site/office hours, not 24/7 consumer traffic).
- **Modest file volume**: order 50–200 GB of photos/attachments/exports across all tenants at this
  scale (per ADR-0009's own row-volume note, the *data* growth that matters most — `ActivityProgressLog`
  — is small individual rows, not storage-heavy; photos are the storage-heavy item).
- **One production environment sized for this scale + one smaller staging environment.**

---

## 2. Cost comparison — order-of-magnitude estimates, explicitly labeled

**These are rough monthly figures for the scale in §1, not quotes.** Real numbers depend on exact
instance/tier choices, committed-use discounts, actual traffic shape, and pricing that changes over
time. Verify against `aws.amazon.com/pricing`/`azure.microsoft.com/pricing` calculators before
budgeting anything.

| Line item | AWS (rough, `ap-southeast-1`) | Azure (rough, Southeast Asia) | Note |
| --- | --- | --- | --- |
| Compute (api + web, always-on, small) | ~$60–120/mo (2–3 small Fargate tasks) | ~$40–100/mo (Consumption plan; less if genuinely scaling to zero nights/weekends, more if pinned to `minReplicas ≥ 1`) | Roughly comparable; Azure has more headroom to go lower if scale-to-zero is actually used (§4's cold-start caveat applies) |
| **Database (the dominant line item)** | **~$300–550/mo** — RDS for SQL Server, Standard Edition, license-included, even at a small instance class | **~$75–180/mo** — Azure SQL Database, General Purpose tier (provisioned small vCore or serverless with auto-pause) | **This is the single biggest driver of the total cost difference.** RDS for SQL Server Standard/Enterprise Edition bakes Microsoft's per-vCPU SQL Server licensing directly into the hourly instance rate — this is a well-known industry cost dynamic, not specific to this project. Azure SQL Database's PaaS pricing model does not carry that same discrete boxed-license markup structure at this tier. (RDS does offer SQL Server *Express* Edition free-tier-eligible instances, but Express caps database size at 10 GB and limited vCPU/RAM — too small for this app's projected `ActivityProgressLog` growth per ADR-0009's own ~350k-rows/project-over-8-months estimate, so it is not a realistic production option here.) |
| Object storage | ~$5–20/mo (S3, this file volume) | ~$5–15/mo (Blob Storage, same volume) | Negligible either way at this scale |
| Secrets | <$5/mo (Secrets Manager, few secrets) | <$5/mo (Key Vault, per-operation pricing) | Negligible either way |
| Ingress/TLS | ~$20–30/mo base (ALB) + usage | Bundled into Container Apps Environment pricing (no separate line item) | Azure's bundling is a real simplification, not just a pricing quirk — see §3 |
| Registry | Negligible (ECR) | Negligible (ACR Basic) | — |
| Logs/metrics | ~$5–15/mo (CloudWatch, this ingestion volume) | ~$10–30/mo (Log Analytics + App Insights, this ingestion volume) | Roughly comparable, Azure slightly higher at typical default ingestion settings |
| **Rough total** | **~$400–750/mo** | **~$150–350/mo** | **Azure's estimated total is meaningfully lower, almost entirely because of the database line item.** |

**What could change this materially:** a committed-use/reserved-capacity discount on either platform
(RDS Reserved Instances or Azure SQL Database reserved capacity can each cut the database line by
30–60% for a 1–3 year commitment — not modeled above because a pilot has no basis yet to commit to a
term); choosing RDS SQL Server Express if the pilot is genuinely small enough for its 10 GB cap
(unlikely to hold for more than the first few months at this app's data-growth profile); or a
customer-specific enterprise agreement/credit on either cloud that isn't visible from here.

---

## 3. Operational burden — who patches what, how much has to be hand-built

| Concern | AWS ECS Fargate | Azure Container Apps |
| --- | --- | --- |
| OS/runtime patching | Fargate itself is serverless (no EC2 to patch); RDS is fully managed (AWS patches the OS and, on a maintenance window, the SQL Server engine) | Container Apps is serverless; Azure SQL Database is fully managed (Azure patches everything, no maintenance-window decision even needed at the General Purpose tier) |
| Day-one infrastructure to hand-build | VPC, subnets (public+private, 2 AZs), security groups, NAT gateway/VPC endpoints, ALB + target groups + listener rules, ACM cert — all separate resources a human designs and wires together before the first request can be served | Container Apps Environment (one resource, brings its own VNet integration + Log Analytics workspace), managed ingress with built-in TLS — meaningfully less to hand-assemble for a small team's first deployment |
| Rollout/rollback mechanics | ECS rolling deployment via `desiredCount`/`minimumHealthyPercent`; blue/green needs CodeDeploy wired in separately if wanted | Native weighted revision traffic-splitting is a first-class Container Apps feature — a convenient built-in canary/rollback mechanism with no extra service to configure |
| Scheduled/one-off jobs (migrations) | ECS `RunTask` — works, but is an override of the same task-definition mechanism used for the live service, slightly less purpose-built | Container Apps Jobs — a dedicated, named, schedulable job resource; a cleaner conceptual fit for the migration-apply step specifically |
| Team learning curve | Depends entirely on the team's existing exposure — **this brief does not know the team's current skill mix, and that is a real input the human decision-maker has that this document does not** | Same caveat applies symmetrically |

**Net assessment:** Azure Container Apps has a lower day-one operational burden for a small team
standing this up for the first time, mainly because it bundles ingress/TLS/scaling into fewer moving
parts than ECS+ALB+VPC. This is a genuine, not marginal, difference in how much has to be designed and
wired by hand before the first deploy succeeds.

---

## 4. Risk comparison

### 4.1 Lock-in vs. the ADR-0010 agnostic posture

Both paths are equally compliant with ADR-0010's constraint as designed: no provider SDK reaches
`CMPlus.Application`/`CMPlus.Domain` on either path (enforced today by `CMPlus.Architecture.Tests`
`LayeringTests`, which already runs in CI regardless of which cloud is chosen), `IFileStorage` is the
only provider-specific seam, and both runbooks reuse the identical Dockerfiles/compose topology
unmodified. **Lock-in risk is therefore low and symmetric** — the real lock-in surface on either path
is the *managed database choice* (RDS SQL Server vs. Azure SQL Database), and even that is mitigated
by both being genuine SQL Server-compatible T-SQL engines this app's EF Core migrations already target
without provider-specific extensions.

### 4.2 SQL Server compatibility risk — asymmetric, but small on both

- **AWS RDS for SQL Server is the literal SQL Server engine** (the same image family this project
  develops/tests against locally and in CI, `mcr.microsoft.com/mssql/server:2022-latest`) — by
  definition, zero compatibility risk.
- **Azure SQL Database is a PaaS-compatible variant**, not the boxed engine. Checked directly against
  what this app actually uses: `rowversion`, filtered unique indexes (ADR-0021's split-index pattern),
  `STRING_AGG`, ANSI NULL semantics, `datetimeoffset` — all supported on Azure SQL Database's current
  compatibility level. This app uses no SQL Agent jobs, no cross-database queries, and no CLR — the
  features that would actually force a step up to the pricier SQL Managed Instance tier. **Assessed
  risk: low, not zero** — the honest gap between "verified against the documented feature list" and
  "verified by actually running the 25 migrations against a real Azure SQL Database," which nobody has
  done (§5, this environment cannot provision anything).

### 4.3 The singleton-`BackgroundService`-vs-scaling trap — present on both, sharper on one

`IdempotencyKeyCleanupService` is a singleton in-process `BackgroundService` with no leader election
and no distributed lock (confirmed directly from the source, `backend/src/CMPlus.Infrastructure/
Idempotency/IdempotencyKeyCleanupService.cs`). Full detail in each runbook's §8; summarized for the
decision:

- **On ECS Fargate** (no default scale-to-zero — a service always runs at least `desiredCount`
  tasks), the trap is "N replicas each run their own redundant hourly sweep." The sweep's own work is
  idempotent under concurrent execution (deleting an already-expired row is a no-op), so this is
  wasted-but-harmless redundant load at this project's scale, not a correctness bug.
- **On Azure Container Apps with `minReplicas = 0`**, the trap inverts: during any scale-to-zero
  window, the sweep **does not run at all** (not "runs less often" — zero replicas means zero
  executions). For *this specific job*, that is provably safe — its own code comment states a failed/
  skipped sweep is never fatal, and the retention windows (90 days / 1 day, `docs/db-conventions.md`
  §10) comfortably absorb a scaled-to-zero night or weekend. **But it is a trap for whatever gets
  added next** — a future job that genuinely needs a real schedule (the sketched, not-yet-built
  `CpmRun` retention job, `docs/db-conventions.md` §7.1) must not be implemented as an in-process
  `BackgroundService` if it's going to live inside a scale-to-zero Container App; it needs a Container
  Apps Job on a cron trigger instead. This is a design discipline the team has to actively maintain
  on the Azure path that simply doesn't arise the same way on a fixed-replica ECS service.

### 4.4 Cold-start impact of scale-to-zero on a construction PM app

Azure Container Apps' `minReplicas = 0` option (not available in the same form on ECS Fargate, which
has no zero-replica idle state for a running service) trades idle-time cost savings against a real,
user-visible cold-start delay on the first request after an idle period — likely low single-digit
seconds for an ASP.NET Core container of this size, not yet measured against a real deployment. For a
tool used in client meetings and site walk-throughs, a multi-second stall opening the app for the first
time that morning is a tangible UX cost, and it compounds with the `/health/ready` gap (§5.3 in each
runbook — the readiness probe does not currently check DB connectivity, so a cold-starting replica
could be marked healthy before its DB connection is actually warm). **This is avoidable** by pinning
`minReplicas = 1`, which forfeits most of the idle-time cost savings that make scale-to-zero attractive
in the first place but removes the cold-start/health-probe interaction entirely — a real, quantifiable
trade a human should make deliberately, not one this brief makes on their behalf.

---

## 5. What this document cannot tell you

Stated plainly, because a document that hides its own limits is more dangerous than one that states
them:

- **No load has ever been run against either platform.** The cold-start figures, the compute cost
  figures, and the "compatibility is low-risk" assessment for Azure SQL Database are all reasoned from
  documentation and this codebase's known behavior, not from an execution.
- **The team's existing AWS vs. Azure operational skill is unknown to this document.** If the team (or
  whoever will operate this in production) already has deep expertise on one platform and none on the
  other, that learning-curve cost is real and could dominate the dollar difference in §2 — this is
  exactly the kind of input the human decision-maker has and this document does not.
- **The pilot customer's own IT environment is unknown to this document.** Many large Thai contractors
  and government bodies are Microsoft/Entra ID-centric shops; if the actual pilot customer has strong
  preferences or existing Azure AD/SSO requirements — or, conversely, an existing AWS relationship or a
  data-residency/certification requirement that names a specific accredited cloud — that is a business
  fact this document cannot see and could reasonably override the reasoning below.

---

## 6. Recommendation

**Recommendation: Azure Container Apps + Azure SQL Database**, as the default choice absent a
business-context reason to prefer AWS (§5 lists what those reasons could be).

**Top 2 reasons:**

1. **Materially lower estimated cost at this pilot scale, driven almost entirely by the database
   line item (§2).** Azure SQL Database's PaaS pricing does not carry the discrete SQL Server
   Standard-Edition licensing markup that dominates AWS RDS for SQL Server's cost at a small instance
   class — this is the single largest and most confident line in the whole comparison, and it alone
   accounts for most of the ~$250–400/mo gap between the two rough totals.
2. **Lower day-one operational burden (§3).** Container Apps bundles ingress, TLS, revision management,
   and scaling into one managed construct; ECS needs a VPC, subnets, security groups, an ALB, target
   groups, and TLS certs hand-assembled before the first deploy. For a small team executing this
   runbook for the first time, that is meaningfully less to design, wire, and get wrong.

**What would change this recommendation:**

- **The team has strong existing AWS expertise and little-to-no Azure exposure** (or the reverse) —
  the learning-curve cost on an unfamiliar platform can easily exceed the ~$250–400/mo cost gap; if
  this is true, recommend the platform the team already knows.
- **A specific customer requirement names a cloud** — a Thai government RFP or enterprise procurement
  process that specifies an accredited/approved cloud provider, or a data-residency clause this brief
  cannot evaluate, overrides the cost argument outright.
- **The pilot customer's IT environment is a Microsoft/Entra ID shop with a near-term SSO requirement**
  — this would *reinforce* the Azure recommendation, not change it, but is worth stating as a factor
  that specifically strengthens rather than weakens the case above.
- **Cost sensitivity turns out not to matter** (e.g. the sponsor has committed AWS credits, an existing
  AWS enterprise agreement, or the ~$250–400/mo gap is immaterial at the deal size involved) — in that
  case, §4.2's SQL Server compatibility argument (RDS is the literal engine, zero risk by definition)
  becomes the deciding factor instead, and AWS becomes the reasonable choice on engineering-risk
  grounds alone.

**This recommendation is advisory only.** Per ADR-0010(d) and `docs/10.` `S16-DO-04`, the human makes
this choice, and the choice must be recorded in writing — decision-maker, date, and reasoning — as its
own follow-on ADR superseding/extending ADR-0010. A blank template for that record follows.

---

## 7. Decision record (to be completed by the human at the Sprint 16B gate)

```
Decision:        [ ] AWS ECS Fargate     [ ] Azure Container Apps
Decided by:       ______________________
Date:             ______________________
Reasoning:        ______________________________________________________
                   ______________________________________________________
Deviates from the recommendation above?  [ ] No  [ ] Yes — why: ___________
```

Once completed, `knowledge-curator` records this as an ADR extending ADR-0010 (per `docs/10.`
`S16-DO-04`'s own DoD), and execution proceeds against the matching runbook
(`deploy-aws-ecs.md` or `deploy-azure-container-apps.md`).
