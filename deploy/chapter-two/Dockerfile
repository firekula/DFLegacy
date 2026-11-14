# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json server.json ./
COPY src/ src/
RUN case "$TARGETARCH" in amd64) rid=linux-x64;; arm64) rid=linux-arm64;; *) exit 2;; esac \
    && dotnet publish src/DFLegacy.Manager/DFLegacy.Manager.csproj -c Release -r "$rid" --self-contained false -o /out \
    && dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -r "$rid" --self-contained false -p:BuildRandomNativeReference=false -o /out/runtime/server

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble
ARG DFLEGACY_RELEASE_VERSION
ARG DFLEGACY_RELEASE_COMMIT
LABEL org.opencontainers.image.title="DFLegacy Chapter Two" \
      org.opencontainers.image.version="$DFLEGACY_RELEASE_VERSION" \
      org.opencontainers.image.revision="$DFLEGACY_RELEASE_COMMIT"
WORKDIR /app
COPY --from=build /out/ ./
COPY deploy/chapter-two/templates/ templates/
COPY LICENSE ./
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /app/resources && chown app:app /app/data \
    && printf '{"version":"%s","sourceCommit":"%s"}\n' "$DFLEGACY_RELEASE_VERSION" "$DFLEGACY_RELEASE_COMMIT" > release-manifest.json
ENV ASPNETCORE_HTTP_PORTS= ASPNETCORE_URLS=
USER app
VOLUME ["/app/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s \
    CMD curl -fsS http://127.0.0.2:48092/health || exit 1
STOPSIGNAL SIGTERM
ENTRYPOINT ["/app/DFLegacy.Manager", "--root", "/app", "--data-dir", "/app/data", "--listen", "127.0.0.2", "--port", "48092", "--no-browser"]
