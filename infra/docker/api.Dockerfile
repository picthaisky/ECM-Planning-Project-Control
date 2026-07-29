# infra/docker/api.Dockerfile
#
# Multi-stage build for CMPlus.WebApi (P0-DO-01). Build context is the REPO ROOT
# (see infra/docker/docker-compose.yml), because the solution spans backend/src/CMPlus.*.
# (Backend was moved into backend/ post-Phase-0 to sit alongside web/ and infra/ - the build
# context itself is unaffected, only the paths COPYed out of it below.)
#
# Base images are pinned by digest (not just tag) so a build is byte-for-byte
# reproducible even if the tag is later repointed upstream.
#
#   dotnet/sdk:10.0            -> sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664
#   dotnet/aspnet:10.0-alpine  -> sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a
#
# ---- Build stage: SDK image compiles + publishes the WebApi -----------------
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
WORKDIR /src

# Restore first, copying only project files, so `dotnet restore` is cached across
# builds that only change .cs files (ADR-0001: 4-project Clean Architecture layout).
COPY backend/Directory.Build.props ./
COPY backend/src/CMPlus.Domain/CMPlus.Domain.csproj src/CMPlus.Domain/
COPY backend/src/CMPlus.Application/CMPlus.Application.csproj src/CMPlus.Application/
COPY backend/src/CMPlus.Infrastructure/CMPlus.Infrastructure.csproj src/CMPlus.Infrastructure/
COPY backend/src/CMPlus.WebApi/CMPlus.WebApi.csproj src/CMPlus.WebApi/
RUN dotnet restore src/CMPlus.WebApi/CMPlus.WebApi.csproj

COPY backend/src/ src/
RUN dotnet publish src/CMPlus.WebApi/CMPlus.WebApi.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ---- Runtime stage: ASP.NET Core runtime only, not the SDK -------------------
# alpine variant chosen over the default/chiseled variants: default (~340MB) alone exceeds
# the 300MB image budget; chiseled has no shell/wget at all so it cannot run a HEALTHCHECK.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a AS final

# Real globalization (ICU), not invariant mode: Thai-first UI needs correct th-TH date/number
# formatting from the API (currency, dates) - see CLAUDE.md conventions.
RUN apk add --no-cache icu-libs tzdata \
    && rm -rf /var/cache/apk/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /app/publish .

# Non-root: the base image ships a pre-created unprivileged "app" user (uid 1654);
# no need to create one ourselves.
USER app

EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "CMPlus.WebApi.dll"]
