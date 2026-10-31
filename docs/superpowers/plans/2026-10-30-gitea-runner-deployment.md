# Gitea Runner deployment plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Register persistent Windows and Docker-hosted Linux runners, then verify an actual main pipeline publishes all platform archives and the multiarchitecture image.

**Architecture:** Windows uses a repository-scoped native runner and a limited current-user logon task. Linux uses a repository-scoped runner container, a dedicated DinD daemon, and shared Linux volumes with identical workspace/temp paths. Registration and publishing credentials stay outside Git.

**Tech Stack:** Gitea 1.26, Gitea Runner 5.2.4, .NET SDK 10.0.402, Node 20.20.2, Python 3.12, VS C++, Docker/buildx, Skopeo.

**Spec:** `docs/superpowers/specs/2026-10-30-cross-platform-deployment-release-design.md`; user additionally selected local Windows plus Docker-hosted Linux.

## Global Constraints

- Preserve the user's existing working files and running applications.
- Keep `dflegacy-windows-x64:host` and `dflegacy-linux-x64:host` labels and all existing test gates.
- Use a package-write-only token for Registry publishing; never commit tokens or original game resources.
- Main publishes development prereleases; formal `vX.Y.Z` releases require an explicit version tag.
- Keep TLS verification enabled by default; HTTP Registry access requires a separate explicit repository variable and per-command flags.

## Review Focus

- A sibling Docker container must see the exact temporary PVF path used by container tests.
- Restarting Docker/Windows must preserve registration and resume polling without exposing secrets.
- Opting into HTTP API access must not implicitly disable Registry TLS verification.
- A failed build, mismatched immutable image tag, or incomplete release must remain unpublished.
- Runner source checkouts must not inherit the user's unrelated dirty files or private game resources.

### Task 1: Deploy runners and Registry transport configuration

**Files:** `deploy/gitea-runners/{Dockerfile,compose.yaml,.env.example,README.md}`, `.gitea/workflows/release.yaml`, `.gitea/README.md`.

**Interfaces:** The Linux runner and DinD both mount `work:/work`, with `TMPDIR=/work/tmp`; Docker uses only a shared Unix socket. `DFLEGACY_ALLOW_HTTP_REGISTRY=true` adds Skopeo TLS flags only to the publishing commands. Durable private state lives in `D:\DFLegacyCI` on this installation.

- [x] Record HTTPS probe failure for the existing HTTP Registry, then add the explicit transport option without changing Docker global settings.
- [x] Build the Linux runner image, verify tool versions, matching workspace bind mounts, buildx amd64/arm64 support, and compose health.
- [x] Register both runners at repository scope, verify online labels and persistent startup configuration.
- [x] Validate shell/YAML syntax, run release tooling tests, and commit the sanitized deployment configuration.

### Task 2: Run and verify actual CI/CD

**Files:** `docs/superpowers/reports/2026-10-30-gitea-runner-deployment-validation.md` and documentation links.

**Interfaces:** A main push/dispatch must execute Windows and Linux tests, transfer same-SHA artifacts, build five archives and a dual-platform OCI image, then publish a complete development prerelease.

- [x] Push verified configuration, run the main pipeline, inspect actual failures, fix their causes, and repeat affected gates.
- [x] Verify all jobs, archive identity/checksums, complete Release assets and Registry platform/digest metadata from the published result.
- [x] Restart idle runners and verify durable registration/health; document exact start/stop/update and host availability requirements.
- [x] Obtain a fresh review of deployment changes, address important findings, and push the final documentation.
