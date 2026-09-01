# syntax=docker/dockerfile:1

# Stage 1: build the frontend in a Node environment.
FROM node:22-alpine AS frontend-build
# This gives us a standard Node toolchain for Vite and keeps the build dependencies isolated from the final runtime image.
WORKDIR /src

# Copy only the package manifests first so dependency restore can be cached independently from the app source.
COPY package*.json ./
# `npm ci` is deterministic because it uses the existing lockfile and is the correct choice for container builds.
RUN npm ci

# Copy the frontend configuration and source after dependencies are restored so a source-only change does not invalidate the restore cache.
COPY index.html ./
COPY tsconfig.json ./
COPY tsconfig.app.json ./
COPY tsconfig.node.json ./
COPY vite.config.ts ./
COPY src ./src

# Build the production bundle that will be served by the ASP.NET host.
RUN npm run build

# Stage 2: publish the .NET API in Release mode.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-publish
# We use the SDK image here because publishing requires a toolchain that can compile and package the ASP.NET app.
WORKDIR /src

# Copy the solution and project files first so restore is cached separately from the rest of the source tree.
COPY api/TheBlock.slnx ./api/
COPY api/TheBlock.Data/TheBlock.Data.csproj ./api/TheBlock.Data/
COPY api/TheBlock.Domain/TheBlock.Domain.csproj ./api/TheBlock.Domain/
COPY api/TheBlock.Application/TheBlock.Application.csproj ./api/TheBlock.Application/
COPY api/TheBlock.Infrastructure/TheBlock.Infrastructure.csproj ./api/TheBlock.Infrastructure/
COPY api/TheBlock.Api/TheBlock.Api.csproj ./api/TheBlock.Api/
COPY api/TheBlock.Tests/TheBlock.Tests.csproj ./api/TheBlock.Tests/
# This restore is intentionally separate from source-copy so incremental Docker builds stay fast.
RUN dotnet restore api/TheBlock.slnx

# Copy the rest of the backend source after restore so code changes do not force a dependency re-restore.
COPY api ./api

# Read the target framework from the project files instead of hard-coding it; the repo is currently on net10.0.
RUN set -eu; \
    TARGET_FRAMEWORK="$(grep -R -h -m1 --include='*.csproj' -Eo '<TargetFramework>[^<]+</TargetFramework>' /src/api | sed -E 's#<TargetFramework>([^<]+)</TargetFramework>#\1#' | head -n 1)"; \
    [ -n "${TARGET_FRAMEWORK}" ] || { echo "No TargetFramework found under /src/api" >&2; exit 1; }; \
    echo "Using target framework: ${TARGET_FRAMEWORK}"; \
    dotnet publish api/TheBlock.Api/TheBlock.Api.csproj -c Release --no-restore -f "${TARGET_FRAMEWORK}" -o /app/publish

# Stage 3: final runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The runtime image is the correct final stage for a production container because it does not include the SDK or compilers.
WORKDIR /app

# Install curl so the runtime can run a real HTTP healthcheck without pulling in the SDK image.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# The aspnet base image ships a built-in non-root user named 'app' (APP_UID) for exactly this purpose;
# creating another one collides with it, so the built-in user is used instead.
# The published app and static frontend content need to be owned by the runtime user.
COPY --chown=app:app --from=api-publish /app/publish/ /app/
# The API reads project docs and the dataset from the repo root at runtime, so they must be present in the container.
COPY --chown=app:app README.md ./
COPY --chown=app:app docs ./docs
COPY --chown=app:app data ./data
COPY --chown=app:app infra ./infra
# The built frontend bundle is copied into wwwroot so the ASP.NET API can serve it and provide SPA fallback routing.
COPY --chown=app:app --from=frontend-build /src/dist/ /app/wwwroot/

# Build provenance, baked in at image build so the running container can report
# exactly which build it is. The API serves these at /api/version and the page
# footer renders them; the ship pipeline passes both arguments (ADR-005).
ARG APP_VERSION=dev
ARG APP_COMMIT=local
ENV APP_VERSION=${APP_VERSION}
ENV APP_COMMIT=${APP_COMMIT}

# Set the runtime URL to port 8080, which matches the container runtime conventions we want for a single-process app.
ENV ASPNETCORE_URLS=http://+:8080
# Expose the application port so orchestrators and humans know which port to publish and probe.
EXPOSE 8080

# Healthcheck against a real API endpoint to confirm the app is accepting traffic, not just that the process is alive.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -fsS http://localhost:8080/healthz || exit 1

# Run as a non-root user to reduce attack surface and comply with container hardening best practices.
USER app

# Start the hosted API; it serves both the API endpoints and the SPA shell.
ENTRYPOINT ["dotnet", "TheBlock.Api.dll"]
