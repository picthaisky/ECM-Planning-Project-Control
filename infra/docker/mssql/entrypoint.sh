#!/bin/bash
# infra/docker/mssql/entrypoint.sh
#
# Starts SQL Server, waits for it to accept connections, applies the idempotent
# bootstrap scripts in init/ (create app database, create least-privilege app login),
# then hands control to the SQL Server process for the life of the container.
#
# All configuration comes from environment variables — no hardcoded credentials
# (ADR-0010). Required/optional variables:
#   ACCEPT_EULA        (required, must be "Y" — set by the image contract)
#   MSSQL_SA_PASSWORD   (required — sa password, complexity-checked by SQL Server itself)
#   MSSQL_APP_DB        (optional, default "CMPlusDb")
#   MSSQL_APP_USER      (optional, default "cmplus_app")
#   MSSQL_APP_PASSWORD  (required if MSSQL_APP_USER is created — no default, must be supplied)

set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD must be set (no default — see docs/db-conventions.md)}"

MSSQL_APP_DB="${MSSQL_APP_DB:-CMPlusDb}"
MSSQL_APP_USER="${MSSQL_APP_USER:-cmplus_app}"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

# Start SQL Server itself in the background so this script can run init steps against it.
/opt/mssql/bin/sqlservr &
SQL_PID=$!

echo "[mssql-init] waiting for SQL Server to accept connections..."
# Passed via SQLCMDPASSWORD (env var), not -P (P0-SEC-01): sqlcmd reads -P's argument off
# the process command line, which is visible to any other user/process on the host via `ps`.
export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
READY=0
for _ in $(seq 1 90); do
  if "$SQLCMD" -S localhost -U sa -C -N -l 5 -Q "SELECT 1" >/dev/null 2>&1; then
    READY=1
    break
  fi
  sleep 2
done

if [ "$READY" -ne 1 ]; then
  echo "[mssql-init] SQL Server did not become ready in time; aborting init (server process kept running for diagnostics)." >&2
else
  if [ -z "${MSSQL_APP_PASSWORD:-}" ]; then
    echo "[mssql-init] MSSQL_APP_PASSWORD not set — skipping app-login creation; only sa is available. See docs/db-conventions.md." >&2
  fi

  shopt -s nullglob
  for script in /usr/src/app/init/*.sql; do
    echo "[mssql-init] applying $script"
    # AppPassword is a sqlcmd scripting variable (-v), substituted into the .sql text itself
    # (e.g. inside a CREATE LOGIN ... WITH PASSWORD = '$(AppPassword)' statement) - it is not
    # sqlcmd's own auth secret, so it can't move to SQLCMDPASSWORD the way sa's password did
    # above. It's still process-argument-visible via `ps`; documented as a known limitation
    # in docs/db-conventions.md rather than silently left unflagged.
    "$SQLCMD" -S localhost -U sa -C -N \
      -v DbName="$MSSQL_APP_DB" AppUser="$MSSQL_APP_USER" AppPassword="${MSSQL_APP_PASSWORD:-}" \
      -i "$script"
  done
  shopt -u nullglob
  echo "[mssql-init] init scripts complete."
fi

# Keep the container alive on the SQL Server process; forward its exit code.
wait "$SQL_PID"
