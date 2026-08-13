# Staging Runbook — S16-DO-01

How to stand up, verify, and tear down the provider-agnostic staging environment for UAT (US-16.1).
This is the *operator* companion to [`staging-deploy-checklist.md`](staging-deploy-checklist.md) (the
ordered pre-flight/what-can-go-wrong reference) — start there for the deploy-time traps, come here for
the actual commands.

**Topology:** `proxy` (Caddy, TLS) → `web` (SPA) + `api` (.NET), `api` → `mssql`. Defined in
[`infra/staging/docker-compose.yml`](../../infra/staging/docker-compose.yml). The proxy is the only
host-published service; the app is reachable only over HTTPS through it.

---

## 1. Prerequisites

- A single node with Docker + Docker Compose v2 (or k3s — the same images apply).
- The **git SHA** of a green CI build (CI pushed `ghcr.io/<owner>/cmplus/{api,web}:<sha>` on merge).
- Read access to GHCR: `echo <token> | docker login ghcr.io -u <user> --password-stdin`.
- Secrets ready: `JWT_SIGNING_KEY` (≥32 bytes entropy), `MSSQL_SA_PASSWORD`, `MSSQL_APP_PASSWORD`.

## 2. Bring up

```bash
cd infra/staging
cp .env.staging.example .env
# Edit .env: IMAGE_REGISTRY, IMAGE_TAG=<the CI SHA>, STAGING_DOMAIN, and all secrets.

docker compose --env-file .env pull        # fetch the exact CI-built api/web images
docker compose --env-file .env up -d        # mssql builds locally; proxy/api/web start
docker compose --env-file .env ps           # all services should become healthy
```

The `mssql` image builds from `infra/docker/mssql` (CI does not publish it); `api`/`web` are pulled,
never built — staging runs byte-for-byte what CI tested.

## 3. Apply migrations (gated, separate from bring-up)

Migrations are **not** applied by `up`. Use the gated `migrate` job in
[`.github/workflows/cd.yml`](../../.github/workflows/cd.yml) (approve the `staging` environment), or
for a manual first bring-up run the EF bundle against the DB from the operator box:

```bash
dotnet ef migrations bundle --self-contained -r linux-x64 \
  --project ../../backend/src/CMPlus.Infrastructure/CMPlus.Infrastructure.csproj \
  --startup-project ../../backend/src/CMPlus.WebApi/CMPlus.WebApi.csproj -o efbundle
./efbundle --connection "Server=localhost,1433;Database=CMPlusDb;User Id=cmplus_app;Password=<app-pw>;TrustServerCertificate=True"
```

Run the §0 pre-flight dedup checks from the deploy checklist first on any DB that is not brand-new.

## 4. TLS

`STAGING_DOMAIN=localhost` uses Caddy's internal CA (self-signed) — expect a browser trust prompt on
staging; that is correct. For a staging host with a real public hostname and reachable :80/:443,
delete the `tls internal` line in [`infra/staging/Caddyfile`](../../infra/staging/Caddyfile) and Caddy
fetches a real certificate automatically.

## 5. F-1 — make the login rate limiter see the real client IP

The api sits behind the proxy, so without forwarded-header handling the per-IP login limiter throttles
on the proxy's IP (sprint-16.md **F-1**, a production blocker). After first `up`, find the compose
network subnet and set it so the api trusts forwarded headers **only** from the proxy:

```bash
docker network inspect staging_default -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}'
# put the result in .env as PROXY_KNOWN_NETWORK, then: docker compose --env-file .env up -d api
```

> The Program.cs wiring that consumes `ForwardedHeaders__KnownNetwork` is in place
> (`ForwardedHeadersSetup`, F-1 fixed). Setting `PROXY_KNOWN_NETWORK` now takes effect immediately;
> leaving it blank trusts no forwarded headers — the limiter degrades to per-proxy but is **not**
> spoofable (the safe failure direction).

## 6. Smoke verification (before handing to UAT)

Run against `https://$STAGING_DOMAIN`. These map to the §7 checks in the deploy checklist and gate the
UAT hand-off ([`docs/qa/uat-plan.md`](../qa/uat-plan.md) §6.4):

- `GET /api/health/ready` → **200** with `database` healthy (proves the api↔mssql wiring).
- Security headers present on an API response: `X-Content-Type-Options: nosniff`, `X-Frame-Options:
  DENY`, `Referrer-Policy`; HSTS present over HTTPS (Staging env emits it).
- Login with a seeded user → 200; wrong password and unknown email → same generic error.
- Repeated failed logins from one client → 429 (and, once F-1 is wired, not affecting other clients).
- Cross-tenant id probe → 404/403, never another tenant's data.

Automated smoke (`web/e2e/smoke.spec.ts`, S16-QA-02) should be pointed at this URL.

## 7. Logs, reset, teardown

```bash
docker compose --env-file .env logs -f api          # tail the api
docker compose --env-file .env down                 # stop, keep data volumes
docker compose --env-file .env down -v              # stop AND wipe DB + storage (full reset)
```

## 8. Known deviation — storage

Staging uses the **local-disk** file adapter on the `cmplus-storage` volume, not object storage — the
one intentional gap from the target production shape, pending the S3/Blob adapter (M-4) after the 16B
gate. The object-storage shape (MinIO + `FileStorage__*`) is already sketched in
[`infra/docker/docker-compose.yml`](../../infra/docker/docker-compose.yml) and drops in when M-4 lands.
Ensure `cmplus-storage` is a real persistent volume (it is, by default) so uploaded evidence survives
restarts.

---

*S16-DO-01. Pairs with [`staging-deploy-checklist.md`](staging-deploy-checklist.md) (pre-flight),
[`backup-restore.md`](backup-restore.md) (S16-DO-03), and the `cd.yml` promote pipeline (S16-DO-02).*
