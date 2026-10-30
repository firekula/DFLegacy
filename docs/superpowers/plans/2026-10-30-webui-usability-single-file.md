# Web UI usability and single-file release implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Fix startup recovery, accept administrator PVF uploads, explain probability multipliers, export one root-layout player ZIP and publish self-contained single-file programs.

**Architecture:** Retain the independent Manager/Server lifecycle and existing .NET 10/WPF stack. Add explicit listener readiness, stream PVF uploads under the supervisor gate, and validate actual .NET bundle contents during release assembly.

**Tech Stack:** .NET 10, ASP.NET Core, WPF, plain JavaScript, Python 3.12, Gitea Actions, Docker/buildx.

**Spec:** `docs/superpowers/specs/2026-10-30-webui-usability-single-file-design.md` (approved).

## Global Constraints

- Preserve existing saves, private PVF, user-running Release and game client. Work only in the existing linked worktree on `codex/webui-release-usability`.
- Upload limit 1 GiB; authenticate, same-origin and CSRF checks; streaming, atomic commit and lifecycle serialization; never overwrite externally mounted PVF.
- Keep `Drop.RatePercent` JSON semantics: UI multiplier = percent / 100; do not change drop algorithms.
- Player ZIP has no wrapper directory, empty credentials, `ClientPath=DNF.exe`, `StartServer=false`; no root Config.ini overwrite, native `ijl15.dll` remains required.
- Publish Manager and Server separately as self-contained single files; no trimming or AOT; verify Windows/Linux execution and all five RID archives without claiming native macOS/ARM execution.
- CI runners may be temporarily resumed for actual validation/publication, then paused again. Never replace an already-published immutable release.

## Review Focus

- A later UDP/TCP bind fails after an earlier bind succeeded: release every owned socket, keep unrelated listener alive, allow retry (Task 1).
- Cancellation or a failing configuration commit during PVF upload: retain the previous usable file/config and remove incomplete files (Task 2).
- Cookies without CSRF, hostile origin, oversize or malformed upload: reject before replacing resources (Task 2).
- Extracting over an existing client: preserve Config.ini window settings and never include private credentials (Task 3).
- Single-file output containing an apphost stub or missing native/WPF/framework dependency: fail structural release verification, not just hash checks (Task 3).

### Task 1: Startup readiness and deterministic cleanup

**Files:** `src/DFLegacy.Server/{ListenerReadiness,ManagerControl,EntranceHandshakeService,EntranceService,CharacterDatagramService,GameplayDatagramService,ProbeService,Program}.cs`, `src/DFLegacy.Manager/ServerSupervisor.cs`, `tests/DFLegacy.Manager.Tests/SupervisorBehaviorTests.cs`, server smoke tests.

**Interfaces:** `ListenerReadiness` records enabled listener states and reports `IsReady`; private `/internal/manager/status` exposes `managed` and `ready`; Supervisor accepts readiness only when both are true. `ServerSupervisor` continues owning the only child and gate.

- [x] Add and run failing real-socket/child tests: delayed or failed listener never reports ready; a second failed bind releases the first; same supervisor retries; bind access denial differs from address-in-use; failed start before child creation is recoverable.
- [x] Add listener readiness and finally/disposal coverage around binding, await complete readiness and process liveness under the state lock, clear failed child state, explain native socket error and affected protocol/port without killing other processes.
- [x] Run `dotnet run --project tests/DFLegacy.Manager.Tests -c Release -- --http --server-dir src/DFLegacy.Server/bin/Release/net10.0`; expect zero failures. Keep complete default Server smoke mandatory in Task 4, where it is already a CI gate. Commit `fix: recover partial startup and wait for listener readiness`.

### Task 2: PVF upload and understandable configuration

**Files:** new `src/DFLegacy.Manager/PvfResourceStore.cs`; Manager paths, supervisor, application, configuration UI (`index.html`, `manager.js`, `manager.css`); `tests/DFLegacy.Manager.Tests/{PvfUploadTests,HttpBehaviorTests,Program}.cs`.

**Interfaces:** authenticated `GET /api/resources/pvf` returns availability/size/external-control; `POST /api/resources/pvf` accepts raw `application/octet-stream` and streams to owned data storage. `ServerSupervisor.UploadPvfAsync(Stream,CancellationToken)` serializes with start/stop/config and returns resource metadata. Root directory/files selected server-side.

- [x] Write and run RED tests for valid synthetic PVF, malformed/empty/truncated/oversized/cancelled streams, failed config commit, concurrent/running/external-controlled replacement, authentication/CSRF/Origin/content type, upload after Manager restart.
- [x] Stream into a private temporary file, validate PVF header/tree bounds, select an immutable content-addressed filename, commit configuration only after file completion; roll back candidates on failure. Preserve old external paths. Set body limits only for this endpoint.
- [x] Replace editable PVF path with upload/progress/status controls. Show 1x/2x probability multiplier with the existing percent conversion and accurate overlapping extra-rate explanation; reduce export buttons to one in preparation for Task 3.
- [x] Run full Manager behavior/HTTP/child suite and JavaScript syntax checks; expect zero failures. Commit `feat: upload PVF resources and clarify deployment controls`.

### Task 3: Root-layout player bundle and verified single-file applications

**Files:** Launcher csproj/installer/default template; Manager exporter; `tools/release/{bundle.py,release.py,ci.py,tests/test_release.py}`; `tests/DFLegacy.Manager.Tests/{Program,docker_smoke.py}`; `.gitea/workflows/release.yaml`, Dockerfile as needed, deployment instructions.

**Interfaces:** release `check_app(...)` understands and verifies single-file bundle tables/embedded deps/runtime config/runtime RID; loose main-assembly layouts are rejected, including fixtures. `ExportAsync("client",...)` and compatible old aliases emit the same root player layout; defaults embedded in Launcher.

- [x] Add/run RED bundle-format/real-publish tests and ZIP contract tests: apphost stub, missing/mismatched embedded dependency, compressed-entry bounds, root launcher+patch, no Config.ini, no credentials, DNF.exe relative path, preserve existing INI on installation.
- [x] Publish with `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `DebugType=embedded`, no trimming/AOT. Parse actual official .NET bundle format rather than trust arbitrary sidecars; verify embedded deps and RID/native/WPF files. Include the same options in Windows client build; keep external web/config content and native bridge working.
- [x] Embed default INI in Launcher; preserve existing config when installing. Export a single root ZIP; update UI/docs/test fixtures and Docker smoke to the new contract. Maintain immutable identity/hash/path safeguards.
- [x] Run Manager and Launcher suites, all release tooling tests, actual Windows single-file Manager/Server/Launcher lifecycle and Launcher installation with nonexistent DOTNET_ROOT. Linux final package/container lifecycle and all five RID archive checks are the mandatory actual CI acceptance in Task 4. Commit `feat: ship compact single-file server and player packages`.

### Task 4: Review, CI and immutable release publication

**Files:** validation report and completed plan/spec; external runner state retained outside Git.

**Interfaces:** existing Gitea workflow, registry private owner, release verification scripts; user main dirty-file inventory must remain byte-for-byte unchanged.

- [x] Run complete required local gates; dispatch one fresh whole-branch review using requesting-code-review. Resolve Critical/Important findings with RED→GREEN tests and a full green suite; record rulings/minors in the ledger.
- [x] Fast-forward main preserving the original tracked/untracked inventory; push authorized changes and temporarily resume both registered runners. Wait for actual required jobs and publication; fix any discovered integration failure with a regression before republishing a new immutable version.
- [x] Independently download remote attachments and verify archives, OCI manifest/config digests and private authorization; inspect actual ZIP contents/file counts and final Windows/Linux behavior evidence.
- [x] Pause runners again, preserve CI evidence outside worktree, finish report and final communication with release link and actual platform verification limits. Commit `docs: record compact release validation` as appropriate; documentation-only CI need not republish indefinitely.
