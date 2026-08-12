# infra/docker/web.Dockerfile
#
# Multi-stage build for the React/Vite PWA (P0-DO-01). Build context is the REPO ROOT
# (see infra/docker/docker-compose.yml) so it can reach infra/docker/web.nginx.conf.
#
# Base images are pinned by digest:
#   node:22-alpine      -> sha256:16e22a550f3863206a3f701448c45f7912c6896a62de43add43bb9c86130c3e2
#   nginx:1.29-alpine   -> sha256:5616878291a2eed594aee8db4dade5878cf7edcb475e59193904b198d9b830de
#
# ---- Build stage: install deps + produce the static Vite build --------------
FROM node:22-alpine@sha256:16e22a550f3863206a3f701448c45f7912c6896a62de43add43bb9c86130c3e2 AS build
WORKDIR /app

# Copy lockfile + manifest first so `npm ci` is cached across builds that only change
# application source.
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ .

# S13-DO-01 (ADR-0005 US-13.1): the Service Worker cache version is stamped from VITE_BUILD_ID at
# build time (vite.config.ts). Passed in as a build-arg by CI's image job (--build-arg
# VITE_BUILD_ID=<sha>); when unset (a bare `docker build` locally) vite.config.ts falls back to a
# dev-<timestamp> so the build still succeeds and the SW still versions, just not from the commit.
ARG VITE_BUILD_ID=""
ENV VITE_BUILD_ID=${VITE_BUILD_ID}
RUN npm run build

# ---- Runtime stage: static files served by nginx, not the Node toolchain ----
# 1.29-alpine (bumped from 1.27-alpine, S1-DO-01 finding): Trivy found 35 High + 2 Critical
# OS-package CVEs on 1.27-alpine with no newer build of that tag available. 1.29-alpine alone
# cut that to 13 High/0 Critical - and `apk upgrade` below (verified: pulls the same fixed
# package versions Alpine already publishes, e.g. libcrypto3/libssl3 3.5.6-r0 -> 3.5.7-r0)
# clears the rest to 0/0. This re-applies on every build, so it also covers whatever drifts
# stale between this file's edits and the next one, not just today's list.
FROM nginx:1.29-alpine@sha256:5616878291a2eed594aee8db4dade5878cf7edcb475e59193904b198d9b830de AS final

RUN apk update && apk upgrade --no-cache && rm -rf /var/cache/apk/*

COPY infra/docker/web.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

# Non-root: nginx:alpine ships a pre-created unprivileged "nginx" user (uid 101), but the
# stock image runs its master process as root by default - reassign ownership of the
# directories nginx writes to at runtime and switch USER so the process itself is non-root.
RUN chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /var/log/nginx /etc/nginx/conf.d \
    && touch /var/run/nginx.pid \
    && chown nginx:nginx /var/run/nginx.pid
USER nginx

EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/index.html || exit 1

CMD ["nginx", "-g", "daemon off;"]
