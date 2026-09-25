# Server-only image. The launcher and the two native Windows modules are not
# part of it: the game client is a 32-bit Windows binary and cannot be
# containerised this way.
#
# Uses only classic Dockerfile constructs so both Docker and Podman/Buildah can
# build it (no BuildKit-only features).

# ---------------------------------------------------------------------------
# Build stage. This runs on Linux, where DFLegacy.Server.csproj's
# MSBuildRuntimeType conditions skip the Windows-only .vcxproj references.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from the project files alone so source edits reuse the cached layer.
# server.json sits at the repository root and is linked into the server project
# as content, so it has to be present before publishing.
COPY src/DFLegacy.Protocol/DFLegacy.Protocol.csproj src/DFLegacy.Protocol/
COPY src/DFLegacy.Server/DFLegacy.Server.csproj src/DFLegacy.Server/
COPY server.json ./
RUN dotnet restore src/DFLegacy.Server/DFLegacy.Server.csproj

COPY src/ src/
RUN dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj \
        -c Release --no-restore -o /app

# ---------------------------------------------------------------------------
# Runtime stage. The ASP.NET Core image ships ICU and tzdata, which the server
# needs for its CP936/CP949 code pages and its local-time fatigue reset.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The save file lives under /app/data; the /pvf mount point holds the client's
# Script.pvf (never shipped in the image, see DEPLOYING.md). curl backs the
# HEALTHCHECK below and doubles as an in-container tool for the admin API.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /pvf \
    && chown -R app:app /app/data
USER app

# Bind every interface inside the container; reachability is controlled by the
# published ports. Admin stays on the image default 127.0.0.1 so the
# unauthenticated admin API is not exposed unless an operator asks for it.
#
# DFLegacy__GameplayDatagram__AdvertisedHost must be supplied at run time: it is
# the address handed to clients for the gameplay UDP ports, and a container
# cannot discover the host address clients use.
ENV DFLegacy__ScriptPvfPath=/pvf/Script.pvf \
    DFLegacy__Entrance__Host=0.0.0.0 \
    DFLegacy__CharacterDatagram__Host=0.0.0.0 \
    DFLegacy__GameplayDatagram__Host=0.0.0.0

# The server binds every listener from its own configuration, so clear the base
# image defaults (ASPNETCORE_HTTP_PORTS=8080 / ASPNETCORE_URLS) that would
# otherwise emit a misleading "Overriding address(es)" warning. Use
# DFLegacy__Admin__Host / __Port to move the admin listener.
ENV ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=

# Login entry (TCP 2311), channel entry (TCP 7001), login extra port (TCP 8080),
# admin HTTP (TCP 8081), character datagram (UDP 2311) and gameplay UDP pair.
EXPOSE 2311/tcp 7001/tcp 8080/tcp 8081/tcp
EXPOSE 2311/udp 7002/udp 7003/udp

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8081/health || exit 1

ENTRYPOINT ["dotnet", "DFLegacy.Server.dll"]
