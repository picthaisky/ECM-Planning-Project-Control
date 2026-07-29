# infra/docker/mssql/Dockerfile
#
# CM+ Project Control — SQL Server container (P0-DB-01).
# Wraps the official mssql/server image with idempotent init scripts (create the app
# database + a least-privilege app login) so the container is usable standalone for
# database-engineer/backend-developer local dev, and as the `mssql` service that
# devops-engineer wires into the top-level docker-compose.yml (ADR-0010: same topology
# in dev/staging/prod, config only via environment variables, no hardcoded secrets).
#
# Base image: Microsoft's official, supported SQL Server 2022 image (Ubuntu 22.04 base,
# ships mssql-tools18 / sqlcmd at /opt/mssql-tools18/bin). Digest-pinned (P0-SEC-01, matching
# the api/web/minio images) rather than the floating "2022-latest" tag; digest re-resolved
# and re-verified pullable at the time of this pin:
# mcr.microsoft.com/mssql/server:2022-latest
FROM mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89

USER root

WORKDIR /usr/src/app

# Idempotent SQL init scripts, applied in lexical order by entrypoint.sh on every
# container start. Scripts MUST use IF NOT EXISTS guards — they are not "run once"
# migration files, they are safe-to-repeat bootstrap steps (actual schema migrations
# are EF Core's job, applied by backend-developer/CI, never by this container).
COPY init/ ./init/
COPY entrypoint.sh ./entrypoint.sh

RUN chmod +x ./entrypoint.sh \
    && chown -R mssql:root /usr/src/app

USER mssql

# No EXPOSE needed beyond the image default (1433); documented in docker-compose.mssql.yml.
ENTRYPOINT ["./entrypoint.sh"]
