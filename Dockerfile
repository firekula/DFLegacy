# syntax=docker/dockerfile:1
# Windows assets are built once and verified against DFLEGACY_RELEASE_VERSION/DFLEGACY_RELEASE_COMMIT.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
ARG TARGETARCH
ARG DFLEGACY_RELEASE_VERSION
ARG DFLEGACY_RELEASE_COMMIT
ARG SOURCE_DATE_EPOCH
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends python3 \
    && python3 -c 'import sys; assert sys.version_info >= (3, 12), "Python 3.12+ required"' \
    && rm -rf /var/lib/apt/lists/* \
    && rm -f /var/log/apt/* /var/log/dpkg.log /var/log/alternatives.log /var/cache/ldconfig/aux-cache
COPY global.json ./
# Include every transitive project reference in the restore cache layer.
COPY src/DFLegacy.Manager/DFLegacy.Manager.csproj src/DFLegacy.Manager/
COPY src/DFLegacy.Server/DFLegacy.Server.csproj src/DFLegacy.Server/
COPY src/DFLegacy.Channel/DFLegacy.Channel.csproj src/DFLegacy.Channel/
COPY src/DFLegacy.Protocol/DFLegacy.Protocol.csproj src/DFLegacy.Protocol/
COPY src/DFLegacy.InterProcess/DFLegacy.InterProcess.csproj src/DFLegacy.InterProcess/
COPY server.json ./
RUN case "$TARGETARCH" in amd64) rid=linux-x64;; arm64) rid=linux-arm64;; *) exit 2;; esac \
    && dotnet restore src/DFLegacy.Manager/DFLegacy.Manager.csproj -r "$rid" \
    && dotnet restore src/DFLegacy.Server/DFLegacy.Server.csproj -r "$rid" \
    && dotnet restore src/DFLegacy.Channel/DFLegacy.Channel.csproj -r "$rid"
COPY src/ src/
COPY tools/release/ tools/release/
COPY deploy/ deploy/
COPY client-assets/ client-assets/
RUN python3 tools/release/release.py verify client-assets --client-assets --version "$DFLEGACY_RELEASE_VERSION" --commit "$DFLEGACY_RELEASE_COMMIT" \
    && case "$TARGETARCH" in amd64) rid=linux-x64;; arm64) rid=linux-arm64;; *) exit 2;; esac \
    && python3 tools/release/release.py publish --rid "$rid" --version "$DFLEGACY_RELEASE_VERSION" --commit "$DFLEGACY_RELEASE_COMMIT" \
       --client-assets client-assets --output /out/DFLegacy \
       --verification "Docker cross-build for $TARGETARCH; container execution is validated separately by CI"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS runtime
ARG DFLEGACY_RELEASE_VERSION
ARG DFLEGACY_RELEASE_COMMIT
ARG SOURCE_DATE_EPOCH
LABEL org.opencontainers.image.title="DFLegacy" \
      org.opencontainers.image.version="$DFLEGACY_RELEASE_VERSION" \
      org.opencontainers.image.revision="$DFLEGACY_RELEASE_COMMIT"
WORKDIR /app
COPY --from=build /out/DFLegacy/ ./
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && rm -f /var/log/apt/* /var/log/dpkg.log /var/log/alternatives.log /var/cache/ldconfig/aux-cache \
    && mkdir -p /app/data /pvf && chown app:app /app/data
ENV DFLEGACY_MANAGER_ROOT=/app \
    DFLEGACY_MANAGER_DATA=/app/data \
    DFLEGACY_MANAGER_LISTEN=0.0.0.0 \
    DFLEGACY_MANAGER_PORT=18080 \
    DFLEGACY_MANAGER_NO_BROWSER=true \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/data/.net \
    DFLegacy__SeedTestAccount=false \
    DFLegacy__Entrance__Host=0.0.0.0 \
    DFLegacy__CharacterDatagram__Host=0.0.0.0 \
    DFLegacy__GameplayDatagram__Host=0.0.0.0 \
    DFLegacy__Admin__Host=0.0.0.0 \
    ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=
USER app
VOLUME ["/app/data"]
EXPOSE 18080/tcp 8081/tcp 2311/tcp 7001/tcp 8080/tcp 7004/tcp 7005/tcp
EXPOSE 2311/udp 7002/udp 7003/udp 7005/udp
# A stopped game is a normal management state. /ready is separate.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS http://127.0.0.1:18080/health || exit 1
STOPSIGNAL SIGTERM
ENTRYPOINT ["/app/DFLegacy.Manager", "--root", "/app", "--data-dir", "/app/data", "--listen", "0.0.0.0", "--port", "18080", "--no-browser"]
