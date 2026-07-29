# CM+ MSSQL container (P0-DB-01)

Wraps `mcr.microsoft.com/mssql/server:2022-latest` (official, supported image; Ubuntu
22.04 base, `sqlcmd` at `/opt/mssql-tools18/bin/sqlcmd`) with idempotent bootstrap
scripts that create the application database and a dedicated app login. No credentials
are hardcoded anywhere in this directory — everything comes from environment variables
(ADR-0010).

## Files

| Path | Purpose |
| --- | --- |
| `Dockerfile` | Adds `init/` scripts + `entrypoint.sh` on top of the official image |
| `entrypoint.sh` | Starts `sqlservr`, waits for it to accept connections, runs `init/*.sql` in lexical order, then blocks on the server process |
| `init/01-create-database.sql` | `CREATE DATABASE` if not exists (`IF DB_ID(...) IS NULL`) |
| `init/02-create-app-login.sql` | Creates a least-privilege SQL login/user for the app (`db_owner` on its own DB only — never `sa`), skipped if `MSSQL_APP_PASSWORD` is unset |
| `docker-compose.mssql.yml` | Standalone compose service for local testing; devops-engineer merges this service block into the top-level `infra/docker/docker-compose.yml` (P0-DO-02) |
| `.env.mssql.example` | Template — copy to `.env` (git-ignored) and fill in real local values |

## Environment variables

| Variable | Required | Default | Notes |
| --- | --- | --- | --- |
| `ACCEPT_EULA` | yes | — (set to `Y` in compose) | Microsoft image contract |
| `MSSQL_PID` | no | `Developer` | edition; do not use `Developer` outside local dev |
| `MSSQL_SA_PASSWORD` | yes | — | must satisfy SQL Server complexity policy |
| `MSSQL_APP_DB` | no | `CMPlusDb` | database created by `init/01` |
| `MSSQL_APP_USER` | no | `cmplus_app` | login/user created by `init/02` |
| `MSSQL_APP_PASSWORD` | yes (for app login) | — | if unset, app login step is skipped (fails loudly rather than creating a weak-password login) |
| `MSSQL_HOST_PORT` | no | `1433` | host-side port mapping in the standalone compose file |

## Run standalone

```bash
cp infra/docker/mssql/.env.mssql.example infra/docker/mssql/.env
# edit infra/docker/mssql/.env with real local passwords
docker compose -f infra/docker/mssql/docker-compose.mssql.yml --env-file infra/docker/mssql/.env up -d --build
docker compose -f infra/docker/mssql/docker-compose.mssql.yml ps   # wait for "healthy"
```

Verify:

```bash
docker exec <container> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<MSSQL_SA_PASSWORD>' -C -N \
  -Q "SELECT name FROM sys.databases; SELECT name FROM sys.server_principals WHERE type = 'S';"
```

Expect `CMPlusDb` (or your `MSSQL_APP_DB`) among the databases and `cmplus_app` (or your
`MSSQL_APP_USER`) among the server principals.

Note: EF Core migrations create/alter the actual application *schema* (tables, indexes,
constraints) — this container only guarantees the empty database and the login exist.
See `docs/db-conventions.md` for schema/index/migration policy.
