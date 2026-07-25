# syntax=docker/dockerfile:1

# --- Build (Native AOT needs clang + zlib) ---------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

COPY global.json ./
COPY FordConnectToAbrpSync/FordConnectToAbrpSync.csproj FordConnectToAbrpSync/
RUN dotnet restore FordConnectToAbrpSync/FordConnectToAbrpSync.csproj \
    -r "linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64)"

COPY FordConnectToAbrpSync/ FordConnectToAbrpSync/
RUN dotnet publish FordConnectToAbrpSync/FordConnectToAbrpSync.csproj \
    -c Release \
    -r "linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64)" \
    -o /app

# --- Runtime (self-contained native binary; minimal deps) ------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
WORKDIR /app
COPY --from=build /app/FordConnectToAbrpSync ./
COPY --from=build /app/appsettings.json ./

# Token file + Data Protection key ring live on a mounted volume.
ENV Ford__TokenFilePath=/data/ford-token.json \
    Ford__KeysDirectory=/data/keys \
    DOTNET_ENVIRONMENT=Production
VOLUME /data

# Rolling log files (Run mode) are written to ./logs relative to WORKDIR.
VOLUME /app/logs

# Heartbeat file for the healthcheck subcommand (see Health/HeartbeatWriter.cs,
# ADR 0006), written to ./heartbeat relative to WORKDIR. Not a VOLUME: it
# carries no state worth persisting across restarts. Written once at worker
# startup and again after every Sync Cycle, so --start-period only needs to
# cover container/runtime startup, not a full Sync Cycle interval. Docker only
# reports status via `docker inspect`/`docker ps` here; a plain `docker run`
# or Compose won't restart an unhealthy container on its own (only Swarm/k8s
# act on it).
HEALTHCHECK --interval=60s --timeout=5s --start-period=30s --retries=3 \
    CMD ["/app/FordConnectToAbrpSync", "healthcheck"]

ENTRYPOINT ["./FordConnectToAbrpSync"]
