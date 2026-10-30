# syntax=docker/dockerfile:1
ARG DOCKER_CLI_IMAGE=docker:29-cli@sha256:5e6baf67325a79f64ed3bed6863954d5e05d0f3d4e57cbdb14809346814bbfdb
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
FROM ${DOCKER_CLI_IMAGE} AS docker-cli
FROM ${SDK_IMAGE}

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl git python3 python3-venv python3-pip skopeo xz-utils \
    && rm -rf /var/lib/apt/lists/*
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/ /usr/local/libexec/docker/cli-plugins/

ADD --checksum=sha256:674145e627e5c879d72c293a08991458cf73c81124e54afdd40cf6995098a01b --chmod=0755 \
    https://gitea.com/gitea/runner/releases/download/v5.2.4/gitea-runner-5.2.4-linux-amd64 /usr/local/bin/gitea-runner
ADD --checksum=sha256:df770b2a6f130ed8627c9782c988fda9669fa23898329a61a871e32f965e007d \
    https://nodejs.org/dist/v20.20.2/node-v20.20.2-linux-x64.tar.xz /tmp/node.tar.xz
RUN tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1 && rm /tmp/node.tar.xz \
    && mkdir -p /runner /work/jobs /work/tmp /cache/nuget /cache/pip

ENV DOCKER_HOST=unix:///var/run/docker.sock \
    DOCKER_CONFIG=/runner/docker \
    TMPDIR=/work/tmp \
    NUGET_PACKAGES=/cache/nuget \
    PIP_CACHE_DIR=/cache/pip \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PYTHONUTF8=1 \
    GIT_TERMINAL_PROMPT=0
WORKDIR /runner
ENTRYPOINT ["gitea-runner"]
CMD ["--config", "/runner/config.yaml", "daemon"]
