# Cross-platform Deployment and Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a standalone Web deployment manager, safe client-package export, self-contained five-RID distributions, Docker deployment, and Gitea 1.26 test/build/release workflows.

**Architecture:** A .NET 10 Manager hosts static responsive HTML and supervises the existing Server. Windows CI builds immutable client assets reused by every package; Python release tooling and Gitea workflows assemble and verify the same layouts. Existing GM/player pages and the fixed 460×315 embedded pages remain distinct and linked from the Manager.

**Tech Stack:** .NET 10 / ASP.NET Core, static HTML/CSS/JavaScript, Windows C++17, Python 3.12, Docker/Compose, Gitea Actions.

**Spec:** `docs/superpowers/specs/2026-10-30-cross-platform-deployment-release-design.md` (approved 2026-10-30, including subsequent Web page inventory).

**Execution status (2026-10-30):** Tasks 1–8 are implemented in `codex/deployment-web-release`. The checklists below preserve the planned scenarios; they are not a claim that every target hardware or external service has been exercised. Actual checks, build identity and outstanding Runner/Registry/game acceptance are recorded in [the delivery report](../reports/2026-10-30-cross-platform-deployment-release-validation.md).

## Global Constraints

- Five targets: win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64; Docker linux/amd64 and linux/arm64.
- Manager default 127.0.0.1:18080; existing game API default 8081; no installed .NET requirement in release packages.
- No game resources/PVF, saved accounts, test/test credentials, machine paths, or secrets in releases or exports.
- Preserve existing 460×315 embedded client pages, adaptive GM/player pages, existing unknown config fields and saved games.
- Default Manager topology is monolithic; reject split-process configs with an actionable message.
- Windows/Linux CI runners only; macOS and ARM verification statuses must be truthful.
- Main creates development prereleases, vX.Y.Z creates official releases; publishing never updates live servers.
- Use isolated worktree `D:/codex_worktree/deployment-web-release/DFLegacy`; preserve the original checkout's research changes.

## Review Focus

- Config/environment precedence: editing a shadowed value must not misleadingly alter the running configuration (Task 3).
- Bootstrap retry after partial failure and imported saves: never reset saves or silently replace existing game administrator credentials (Tasks 1, 3).
- Shutdown/timeout/racing requests: stop only owned processes, retain Web access, and keep state consistent (Task 4).
- Export manifest traversal, resource substitution and mixed commits: reject unsafe/mismatched bundles before downloading (Tasks 2, 5).
- Cookie and setup-token request boundaries: reject hostile Host/Origin, missing CSRF, re-used setup credentials and unauthenticated controls (Task 6).

## Files and ownership

- Server integration: `src/DFLegacy.Server/HostingArguments.cs`, `StoreProcessLease.cs`, `ManagerControl.cs`, `Program.cs`, `ServerOptions.cs`, `JsonGameStore.cs`; targeted `tests/DFLegacy.SmokeTests/DeploymentHostingSmokeTests.cs` and entrypoint.
- Windows resources: IJL15 config/URL source and native tests, Launcher blank-profile handling and resource propagation, CMake registration and `.csproj` native copy configuration.
- Manager core: `src/DFLegacy.Manager/{ManagerPaths,ManagerSettings,ConfigurationStore,ManagerIdentity,ServerSupervisor,ManagerLog,ClientPackageExporter,Program}.cs`, project and `wwwroot/*`.
- Manager tests: new console runner `tests/DFLegacy.Manager.Tests/*`, meaningful file/process/HTTP behavior rather than source-text assertions.
- Packaging/CI: `tools/release/*`, `deploy/*`, `.gitea/workflows/*`, `global.json`, Dockerfile/.dockerignore/compose.yaml and `.env.example`.
- Integration/docs: solution file, README/BUILDING/DEPLOYING, `DEPLOYMENT_MANAGER.md`, this plan and spec status.

### Task 1: Safe Server hosting and offline initialization

**Interfaces:** Server accepts `--config <absolute-file>`, `--initialize-admin` with stdin JSON `{userName,password,oicqCode}`. New `SeedTestAccount` defaults true for legacy compatibility; release configuration sets false. `DFLEGACY_MANAGER_CONTROL_TOKEN` is the private per-child secret. `GET /internal/manager/status` and `POST /internal/manager/stop` require loopback source and `X-DFLegacy-Manager` secret and bypass game account auth only after that validation; status returns `{managed:true}`, stop returns 202 and gracefully shuts down after sending its response. Host/maintenance paths acquire an OS-backed lease adjacent to effective DataPath.

- [ ] Write tests for fresh seed-disabled storage, legacy default seeds, existing saves preserved, bootstrap duplicate rejected, two-process store exclusion and secret/source checks.
- [ ] Run targeted `dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --deployment-hosting` and observe missing behavior.
- [ ] Implement argument parsing before configuration, config file selection, pre-start admin CLI, lease acquisition for all state-mutating host/maintenance paths, and managed health/stop endpoints.
- [ ] Run targeted tests and the full default smoke runner; exercise real subprocess initialized/started/stopped via a temporary configuration.
- [ ] Commit only the Server integration and its tests.

### Task 2: Windows assets and remote embedded-page configuration

**Interfaces:** `Config.ini` gains `[Network] ClientServiceUrl=` (default preserves localhost); absolute http(s) URL base used for the existing `/client/client_pay.htm` and `/client/client_diamond.htm` hooks. Launcher installs this setting from its existing resolved AccountServiceUrl while preserving other user settings. Native code must validate value lengths and lifetime of patched URL storage. Launcher UI supports an empty release account list without recreating test/test.

- [ ] Add native behavior tests for local fallback, HTTP/HTTPS remote URL, invalid input, and maximum length; add launcher configuration checks for an initially empty profile.
- [ ] Run tests and observe remote-page configuration missing.
- [ ] Implement URL configuration, preserve embedded HTML/CSS, fix fresh CMake debug source lists and register debug tests, and allow explicit native asset build/copy behavior for reproducible packaging.
- [ ] Build/run IJL15 and Debug tests with VS MSBuild or CMake; publish Launcher into a clean directory and inspect native resources.
- [ ] Commit owned native/Launcher changes.

### Task 3: Manager persistent configuration and identities

**Interfaces:** `ManagerPaths(root,dataDirectory)` exposes absolute Root, Data, ServerConfig, SettingsFile, ServerDirectory, AssetsDirectory. `ConfigurationStore` reads templates, preserves unknown JSON fields, stores config atomically with backup and validates published connection settings. `ManagerSettings` stores game-facing advertised/API/relay preferences and Autostart. `ManagerIdentity` persists PBKDF2 salted management credentials, a one-use setup secret and login limits. Manager never writes game saves directly.

- [ ] Write tests for path resolution outside cwd, first-run without overwrite, atomic config backup, invalid ports/hosts/rates, unknown field preservation, environment shadowing, independent password/session identity and expired setup reuse.
- [ ] Run new console test runner; observe missing Manager feature.
- [ ] Implement paths/config/identity and safe initial administrator orchestration using Task 1 stdin CLI. Existing-save setup can skip game-admin creation; duplicate names return explicit error and can be retried.
- [ ] Run all Manager tests and inspect persisted files for secrets/permissions and non-reset behavior.
- [ ] Commit the persistent Manager core and tests.

### Task 4: Supervision, logs and graceful stop

**Interfaces:** `ServerSupervisor` exposes snapshot + `StartAsync`, `StopAsync`, `RestartAsync`, serialized with config application. Executable is fixed under runtime/server, config path is explicit, child secret fresh per start; readiness uses Task 1 protected status. `ManagerLog` keeps bounded ring buffer and rolling files and redacts known secrets. Graceful timeout may kill only its own child tree. A Manager lifetime service stops children on host termination; duplicate Manager data directories are rejected by a file lease.

- [ ] Add real-process tests for start readiness, immediate exit/port collision, repeated start/stop, hanging readiness, soft stop, wrong-owner service, crash log/exit code and data directory exclusion.
- [ ] Watch failures before implementing supervision.
- [ ] Implement state transitions, bounded output capture, preflight checks, cancellation handling, restart-required configuration reporting and shutdown cleanup.
- [ ] Run process tests against a disposable published Server using generated fixture data, not private PVF or production saves.
- [ ] Commit lifecycle functionality and tests.

### Task 5: Client bundle export

**Interfaces:** `client-assets/manifest.json` schema `{version:string,commit:string,files:[{path:string,sha256:string}]}` uses `/` separated paths relative to client-assets. Files live under `launcher/` (complete self-contained output) and `patch/` (`ijl15.dll`,`debug.dll` optional,`Config.ini`). Exporter verifies these against root `release-manifest.json` version/commit. Patch ZIP places DLL/config template at root; full ZIP places Launcher output in `DFLegacyLauncher/` and launcher.json uses `../DNF.exe`, empty credentials/list, `StartServer=false`, configured API/Relay. Generated Config.ini sets ClientServiceUrl. Both contain install instructions and SHA-256 manifest; include no original manifest-listed launcher.json/Config.ini without replacement.

- [ ] Write real-ZIP tests for default credential stripping, valid remote settings and both package kinds; reject traversal/symlinks/tampering/mixed commit/wildcard destination and missing dependencies.
- [ ] Run and observe failure, implement a fixed-path allowlist, manifest validation, bounded export lock, streaming download and expiry cleanup.
- [ ] Run ZIP tests with hand-built controlled fixtures; inspect final real Windows bundles from release tooling.
- [ ] Commit exporter and tests.

### Task 6: Web UI and HTTP boundaries

**Interfaces:** Manager CLI `--root <root> --data-dir <dir> --listen <IP> --port <port> --no-browser`; env `DFLEGACY_MANAGER_ROOT`, `DFLEGACY_MANAGER_DATA`, `DFLEGACY_MANAGER_LISTEN`, `DFLEGACY_MANAGER_PORT`, `DFLEGACY_MANAGER_NO_BROWSER`. Public `/health` represents Manager, `/ready` represents game readiness (503 when stopped). `/api/setup/status`, `/api/setup`, `/api/login`, `/api/logout`, `/api/session`, authenticated `/api/config`, `/api/server/{start,stop,restart}`, `/api/status`, `/api/logs`, `/api/export/{patch,launcher}`. Cookie HttpOnly/SameSite Strict; CSRF returned by setup/login/session, mutating API requires `X-CSRF-Token` plus same-origin/loopback-compatible origin validation and valid Host. Only fixed static assets are public.

- [ ] Add HTTP subprocess tests for unauthenticated denial, malicious Host/Origin, setup bootstrap credential required and consumed, cookie flags, missing/mismatched CSRF, login failure limit, service-stopped health vs readiness, config and export endpoints.
- [ ] Watch RED, implement endpoints and separate Chinese responsive login/setup/dashboard/config/logs/export UI sharing existing backend styling concepts.
- [ ] Include links to existing GM/player pages, distinguish their stopped status, explain embedded client pages and preserve their fixed layout. Do not expose passwords/tokens in query logs.
- [ ] Run HTTP suite, verify JS syntax and visually inspect setup, stopped dashboard and responsive page; no game UI automation required.
- [ ] Commit Manager host/UI/tests.

### Task 7: Reproducible release tooling and Gitea

**Interfaces:** Python stdlib CLI `tools/release/release.py` subcommands build-client-assets, publish, verify, test, release, prune. Publish accepts `--rid`, `--version`, `--commit`, `--client-assets`, `--output`; packages the agreed layout and SHA-256 metadata; actual runner/OS validation status is explicit. Root manifest `{version,commit,rid,verification,files:[{path,sha256}]}`. Runtime paths match Tasks 1–6. Publishing writes only its own new output directory, never an existing installation.

- [ ] Write behavioral Python tests on fixture bundles for clean templates, mixed revision rejection, archive executable modes, dependency completeness, version/ref validation and draft-release retry/no overwrite; use a local fake Gitea HTTP server for API behavior.
- [ ] Watch RED then implement reusable build, verification, archive, Gitea publish and opt-in prune with dry-run default (keep 20 dev releases).
- [ ] Add SHA-pinned actions compatible with Gitea 1.26, required tests using actual smoke executables, Windows assets then platform/container artifacts, aggregate gates and draft-before-publish. No secrets for fork PRs, Registry permissions isolated to publishing.
- [ ] Run Python tests, workflow YAML parsing, publish/verify at least win-x64 and cross-target layouts; verify final packages, not just build output.
- [ ] Commit release tooling/workflows and runner instructions.

### Task 8: Docker, start scripts, docs and final integration

**Interfaces:** image Manager root `/app`, data `/app/data`, PVF `/pvf/Script.pvf`; entrypoint Manager on `0.0.0.0:18080 --no-browser`; Compose loopback-publishes management, exposes game API and TCP/UDP ports, uses writable named data volume and read-only PVF mount. Linux host-network example preserves IPv6 deployment. Image HEALTHCHECK only probes Manager.

- [ ] Update Docker stages to include all project references and prebuilt same-revision Windows assets; add bridge/host Compose examples, .env.example, Windows/Linux/macOS launch entries, explicit import/backup/upgrade paths and Registry settings.
- [ ] Run clean Windows native and managed suites, all Manager HTTP/process tests, Python tooling tests, self-contained package smoke, archive/manifest checks and Docker build/run if daemon available. Record unavailable runtime checks explicitly.
- [ ] Review full diff with fresh reviewer against approved spec, fix important findings with reproductions, repeat affected checks.
- [ ] Commit integration/docs and report artifacts, tested coverage and remaining external Runner/Registry setup. Preserve original checkout and do not publish or push incomplete/unverified releases.
