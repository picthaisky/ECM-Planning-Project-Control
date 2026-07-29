# Secrets Policy — CM+ Project Control

**Author:** `security-auditor` · **Task:** P0-SEC-01 (`docs/10.` §5) · **Status:** Binding from Phase 0

**Upstream:** ADR-0010 (cloud-agnostic containerization; the AWS-vs-Azure choice is deferred to the
Sprint 16B gate), P0-DO-01/02/03 (`infra/docker/`, `.dockerignore`, `.github/workflows/ci.yml`),
P0-DB-01 (`infra/docker/mssql/`).

This policy binds every agent and every human contributor. It covers what must never enter git,
where a secret is allowed to live at each stage, and what to do when one leaks. It deliberately
does **not** name a cloud secret manager — ADR-0010 defers that choice, and pre-committing to one
here would contradict an accepted ADR.

---

## 1. What counts as a secret in this repo

Anything in this table is a secret. If a value grants access to data, money movement, or
infrastructure, treat it as a secret even if it is not listed.

| Class | Examples in this codebase | Blast radius if leaked |
| --- | --- | --- |
| Database connection strings | `ConnectionStrings__Default` (`infra/docker/docker-compose.yml`), any ADO.NET string carrying a literal password | Full read/write to every tenant's contracts, payment certificates and VOs — the worst case for a multi-tenant product |
| MSSQL passwords | `MSSQL_SA_PASSWORD`, `MSSQL_APP_PASSWORD` | `sa` is server-wide admin; the app login is `db_owner` on `CMPlusDb` (`infra/docker/mssql/init/02-create-app-login.sql`) |
| JWT signing keys | Sprint 2 auth (`S2-SEC-01`) | Forge any token, including another tenant's `TenantId` claim and an approver role — a direct path to unauthorized VO / payment approval |
| Object storage credentials | `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`, `FileStorage__AccessKey` / `FileStorage__SecretKey` | All site photos and exported documents across all tenants; photos carry site and PII content |
| Cloud provider credentials | none yet; created after the ADR-0010 Sprint 16B gate | Full environment compromise |
| CI and registry tokens | GHCR push tokens, any PAT | Supply chain: push a poisoned image tagged with a trusted commit SHA |
| Third-party API keys | future weather-data provider, email/SMS sender | Vendor billing abuse, data exfiltration |

Not secrets, and safe to commit: hostnames, ports, bucket *names*, `ASPNETCORE_ENVIRONMENT`,
image digests, and any `*.example` template whose values are documented placeholders.

---

## 2. Where dev secrets live

Confirmed against what `devops-engineer` and `database-engineer` actually built, not assumed:

| Item | Reality on disk |
| --- | --- |
| Tracked template | `infra/docker/.env.example` — documents every variable consumed by `infra/docker/docker-compose.yml`. Secondary template `infra/docker/mssql/.env.mssql.example` for the standalone DB stack. |
| Real local file | `infra/docker/.env` (and `infra/docker/mssql/.env`), created by copying the template, never committed. |
| Ignore rule | Root `.gitignore` line 27 is a bare `.env` — it matches a file literally named `.env` at any depth while leaving `*.example` trackable. `infra/docker/mssql/.gitignore` repeats the rule locally. |
| Image hygiene | Root `.dockerignore` excludes `**/.env`, `**/.env.*` (with a `!**/.env.example` re-include), `*.pem`, `*.key`, `*.pfx`, `secrets.json` — no credential can reach an image layer. |
| Compose contract | Every secret-bearing variable uses the required form with a `:?` message, so `docker compose up` fails loudly on an unset secret instead of silently starting with a default credential. This is correct and must be preserved. |

Rules:

- **Placeholder convention.** Template values use a `Ch4ngeMe!<Purpose>` form. The scanner allowlist
  in `.gitleaks.toml` recognises it, so keep it. A template value that looks like a real credential
  will fail CI — by design.
- **Never** put a real secret in `appsettings.json` or `appsettings.Development.json`. Both are
  tracked and currently contain only logging configuration. For local backend runs outside Docker
  use .NET user-secrets (`dotnet user-secrets`), which stores values outside the repo tree.
- **Frontend.** Anything reaching a `VITE_*` variable is compiled into the shipped bundle and is
  public by definition. No secret may ever be a `VITE_*` value; the PWA obtains data only through
  an authenticated API call.
- **Configuration reaches the app through `IConfiguration` and environment variables only**
  (ADR-0010). No provider SDK, no secret file baked into an image.
---

## 3. Where staging and production secrets live

Deliberately a placeholder until the ADR-0010 Sprint 16B gate picks AWS ECS Fargate or Azure
Container Apps. Naming a manager now would pre-empt a decision the human owns.

| Need | Decision |
| --- | --- |
| CI-time secrets (registry push, staging deploy credentials) | **GitHub Actions encrypted secrets / environment secrets.** Provider-neutral, available today, no new dependency. Use a GitHub *Environment* with required reviewers for anything that can reach staging. |
| Runtime secrets (staging and production) | **A managed secret store, injected as environment variables at container start.** The concrete product is chosen at the Sprint 16B gate together with the compute platform. Until then, Sprint 16A staging may use a root-owned `0600` env file on the host referenced by compose `env_file:` — acceptable only for a non-production environment holding no customer data, and it must be replaced at the gate. |
| Selection criteria at the gate | Versioned secrets, automated rotation, identity-scoped read access, an audit log of every read, and injection without writing plaintext into the container image. |

Standing rules regardless of provider:

- Staging and production **never** share a credential value with dev, and never with each other.
- The MinIO root credential is a dev and staging convenience. Production object storage uses a
  scoped, least-privilege identity, not a root key.
- No secret is ever passed as a Docker `ARG` or `--build-arg`; build args persist in image history.

---

## 4. Automated enforcement

`.github/workflows/ci.yml` job `secret-scan` runs gitleaks `v8.30.1` (pinned by digest) over the
**full history** of the PR branch, and is in the `needs:` list of the `ci-required` gate job, so a
detection blocks merge.

The ruleset is `.gitleaks.toml` at the repo root: the upstream default rules **plus** two project
rules. Those extra rules are not decoration. Before this policy was written the scanner was run
against a disposable repo seeded with this project's own secret classes:

| Seeded value | Default gitleaks rules | With `.gitleaks.toml` |
| --- | --- | --- |
| AWS access key id | caught (`aws-access-token`) | caught |
| AWS secret access key | caught (`generic-api-key`) | caught (`cmplus-service-credential`) |
| GitHub personal access token | caught (`github-pat`) | caught |
| JWT signing key (high entropy) | caught (`generic-api-key`) | caught |
| A literal MSSQL sa password assignment | **MISSED** | caught (`cmplus-password-assignment`) |
| A literal MinIO root password assignment | **MISSED** | caught (`cmplus-password-assignment`) |
| A connection string carrying a literal password | **MISSED** | caught (`cmplus-password-assignment`) |

The three misses are the highest-value secrets in a multi-tenant construction platform: there is no
default rule for plain password or connection-string assignments, and real database passwords sit
below the `generic-api-key` entropy floor. A stock-configuration secret-scan job would have reported
green while missing all three.

The ruleset was then re-run over the current working tree and reports **zero** findings, so the gate
starts green rather than being switched off on day one.

Scanner hygiene:

- `--redact` is mandatory. Without it gitleaks prints the matched secret into the workflow log and
  into the uploaded report, both readable by anyone with repository read access. The scan must not
  become the disclosure.
- The checkout is mounted read-only and the SARIF report is written to a separate volume.

Optional local pre-commit check, before anything reaches a remote:

    docker run --rm -v "$PWD:/repo" zricethezav/gitleaks:v8.30.1 protect --source=/repo --staged --config /repo/.gitleaks.toml --redact
---

## 5. What happens when the scan fires

**A detected secret is a compromised secret.** Deleting the commit does not undo disclosure. The
value may already sit in a fork, a runner cache, a CI log, a mirror, an editor local-history folder,
or a scraper index — credentials pushed to a public repo are harvested within minutes.

In order, no steps skipped:

1. **Rotate first, before touching git history.** Change the value at its source: the SQL login, the
   MinIO/S3 key, the signing key, the token. Until it is rotated the leak is live and rewriting
   history is only cosmetic.
2. **Assess exposure.** How long was it reachable, was the repository public or the branch pushed to
   a fork, and what does the credential reach? Anything touching tenant data, payment certificates
   or VO approval is escalated to the human immediately, not handled quietly by an agent.
3. **Invalidate derived access.** A leaked JWT signing key means every issued token is forgeable —
   rotate the key *and* force re-authentication. A leaked database credential means reviewing the
   audit log for use of that login.
4. **Purge from history** (`git filter-repo` or BFG) and force-push the branch. This is cleanup,
   never the remedy. Never rewrite `main` without human approval.
5. **Close the hole.** Add the ignore rule or template placeholder that would have prevented it, and
   add a rule to `.gitleaks.toml` if the pattern was missed entirely.
6. **Record it.** Trigger `/learn` so `knowledge-curator` files the incident in
   `.claude/knowledge/lessons/lessons-learned.md`.

**False positives.** Never disable the job and never add a blanket path allowlist. Fix the value by
using the placeholder convention, or add a narrow, commented rule allowlist in `.gitleaks.toml`. Only
if neither applies, pin the specific finding by fingerprint in a `.gitleaksignore` with a comment
naming the reviewer and the reason. A path allowlist over `**/*.example` is explicitly rejected:
pasting a real secret into a template file is a common way secrets actually leak.

**Note on history-wide scanning.** Because the job scans full history, a secret that lands on a
long-lived branch keeps the check red until history is purged. That is intended — it makes the
rotation-and-purge procedure above non-optional rather than something a team defers indefinitely.

---

## 6. Review checkpoints

This policy is re-verified at every `security-auditor` gate in `docs/10.` §12, in particular
`S2-SEC-01` (JWT key handling and token storage). At the ADR-0010 Sprint 16B gate, section 3 must be
rewritten from placeholder to the chosen secret manager, and this sentence removed.