# Client Update V4 and Incident Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Repair every managed client file reliably, preserve player configuration, and produce useful individual incident reports, verified against an isolated Gitee test release.

**Architecture:** A small UI-independent .NET library implements V4 manifests, source validation, resumable content cache, configuration migrations and journaled transactions. The launcher adapts it to its existing update UI and authenticated helper handoff; V3 remains a separate compatible path. The Python release tool produces V4 payloads for both the CLI and offline GUI.

**Tech Stack:** .NET 10/C#, WPF/WinForms, native x86 C++, Python 3.12, public GitHub/Gitee release APIs.

**Spec:** `docs/superpowers/specs/2026-11-08-client-update-v4-diagnostics-design.md` (approved 2026-11-08).

## Global Constraints

- Only this linked worktree/branch; no changes to chapter two or the original main worktree.
- Gitee `firekula/DFLegacy` test publishing authorized; formal GitHub releases and online servers untouched. Never mirror private Git history.
- Full SHA256 verification of every managed file on every start; per-file downloads plus resume, no binary delta.
- Installed A and discovered B remain distinct. Offline launch requires complete validation of installed A and no pending transaction.
- Preserve x86/x64 and normal/test separation, explicit hosted V3 and disabled-network modes.
- Configuration is migrated structurally, never replaced by a managed binary. Preserve player credentials and unrelated/unknown fields.
- No process termination without player consent; no automatic diagnostic upload.
- Gitee transport parts <= 48 MiB; reject provider count/total limits, never silently omit or delete releases.

## Review Focus

- Duplicate JSON names, Windows aliases and file/directory path collisions must not bypass the protected-path policy (Task 1).
- A partial file from an old URL/ETag or an HTML login page must never be accepted as the intended resource (Task 2).
- Skipping releases must apply cumulative retirement/migrations, but never remove a player-owned file (Task 3/6).
- An interrupted update from a corrupt baseline must not expose a mixed installation as usable or rely on the broken launcher to repair (Task 3/4).
- Normal channel handoff, cancellation and handled native SEH must not be classified as user-visible crash/network incidents (Task 5).

---

### Task 1: V4 manifest, safe paths and complete verification

**Files:** Create `src/DFLegacy.UpdateCore/{DFLegacy.UpdateCore.csproj,Manifest.cs,PathPolicy.cs,FileVerifier.cs}` and `tests/DFLegacy.UpdateCore.Tests/{DFLegacy.UpdateCore.Tests.csproj,Program.cs,ManifestTests.cs}`; update solution.

**Interfaces:** `ManifestCodec.Parse(ReadOnlySpan<byte>) -> ClientManifest`; `ManifestCodec.Serialize(ClientManifest) -> byte[]`; `PathPolicy.Resolve(root, relative, allowConfiguration=false) -> string`; `FileVerifier.VerifyAsync(root, manifest, progress, token) -> VerificationResult`.

- [x] Write tests: arbitrary nested NPK/PVF/DLL and zero-byte files verify; same-length corruption is detected; duplicate/case-colliding names, ADS, reserved names, traversal, links, protected runtime/config paths, file-as-parent and unknown schemas are rejected. Explicit architecture/flavor and cumulative retirement required.
- [x] Run `dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release -- manifest`; expect missing implementation failure.
- [x] Implement typed schema 4 records, bounded/strict JSON, normalized relative paths and streaming hashes. Limit metadata to 16 MiB and 100,000 files, sizes to signed 64-bit with checked totals. Include sequence/minimum baseline, migrations and retirement records; prohibit managed path prefixes colliding.
- [x] Run the manifest tests, then the whole core test runner; expect zero failures. Commit.

### Task 2: Trusted providers and persistent resumable downloads

**Files:** Create `src/DFLegacy.UpdateCore/{ReleaseSource.cs,ReleaseProvider.cs,ResourceDownloader.cs,ContentCache.cs}` and tests `{ProviderTests.cs,DownloadTests.cs,TestHttpServer.cs}`.

**Interfaces:** `ReleaseSource(provider, repository, channel, architecture, flavor)`; `IReleaseProvider.FindAsync(token) -> VerifiedRelease?`; `VerifiedRelease` binds raw manifest/hash, release identity and provider. `ResourceDownloader.FetchAsync(ResourcePart, cache, progress, token) -> verified path`; `ContentCache.MaterializeAsync(ManagedFile, downloader, progress, token) -> verified logical file path`.

- [x] Write tests using a real loopback HTTP server: 206 resume survives a new downloader; 200 restarts only the partial; 416 validates or retries; wrong ranges/length/hash, HTML, changed ETags, expired redirects, transient 429/503, stalls and explicit cancellation are distinguished. Previously completed content is reused with zero GETs.
- [x] Write provider fixtures: public GitHub and Gitee structured attachments, draft/incomplete releases excluded, wrong channel/architecture/flavor/sequence refused, repository/redirect escape rejected, anonymous request headers, fixed target despite latest changing.
- [x] Run `dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release -- transport`; expect RED.
- [x] Implement anonymous providers with bounded metadata, strict repository attachment origins, content-addressed cache and resumable parts. Use request/idle timeout, max 4 attempts with bounded Retry-After, 2 downloads maximum, streaming copies and per-part/final SHA256.
- [x] Run full core suite; expect GREEN. Commit.

### Task 3: Configuration migration and recoverable installation

**Files:** Create `src/DFLegacy.UpdateCore/{ConfigurationMigration.cs,InstallationStore.cs,InstallTransaction.cs,UpdateEngine.cs,InstallationLease.cs}`; tests `{MigrationTests.cs,TransactionTests.cs,EngineTests.cs}`.

**Interfaces:** `ConfigurationMigration.Apply(json, migrations) -> byte[]`, `IsSatisfied`; `InstallationStore.ReadInstalled() -> VerifiedRelease?`; `UpdateEngine.CheckAsync(progress, token) -> UpdateCheck`; `DownloadAsync(check,progress,token) -> StagedUpdate`; `InstallTransaction.Apply(root, staged, progress)` and `Recover(root, progress)`; `InstallationLease.Acquire(root)`.

- [x] Write tests for preserved credentials/unknown fields/default ports, idempotent port union/default/conditional operations, malformed config retained; same-version repair and missing old metadata; offline valid A versus corrupt A; B cancelled/offline before commit leaves A usable; cumulative A-to-C retirements operate only on managed history.
- [x] Write real filesystem/subprocess fault tests: after each replacement/config/state commit interruption, recover to verified old or complete new; corrupt baseline restores bytes but is not marked usable; corrupted backup/stage fails closed; locked files, concurrent install/setting writes and space estimation.
- [x] Run `dotnet run --project tests/DFLegacy.UpdateCore.Tests -c Release -- install`; expect RED.
- [x] Implement durable write-ahead journal, exact byte backups with hashes, pinned/no-link directories, precommit resource checks, safe same-volume replacements, migration+state commit last, installed receipt separated from discovered targets. Fail closed on malformed journals; bounded cleanup never removes pending recovery data.
- [x] Run full core suite including process-kill recovery; expect GREEN. Commit.

### Task 4: Launcher UI, helper, legacy bridge and independent repair

**Files:** Modify launcher `Updates/{UpdateSession.cs,UpdateModels.cs,UpdateController.cs,UpdateHelper.cs,UpdateCoordinator.cs}`, `Models/LauncherOptions.cs`, `Services/LauncherAccountStore.cs`, `ClientPackageManifest.cs`, `Program.cs`, project; create `Updates/{V4UpdateSession.cs,V4UpdateHelper.cs}` and launcher tests `UpdateV4Tests.cs`.

**Interfaces:** V4 adapts the existing `IUpdateSession` and adds truthful status/progress/offline validation without bypassing launch checks. Existing V3 types remain backward compatible. A separately distributed self-contained launcher copy supports `--repair-client <directory>` and does not execute the installed/broken launcher during repair.

- [x] Add failing tests for same-version corruption repairing before game launch, offline-A allowed with warning, damaged/mixed files blocked, hosted/disabled legacy paths preserved, explicit Gitee overriding no other implicit source, concurrent account edits preserved, helper parent/target authenticity, independent repair of a non-executable launcher.
- [x] Run launcher focused tests; expect RED.
- [x] Integrate core engine, shared config lock, phased progress/source/version messages, verified launch gate and recovery. Preserve helper current-user pipe authentication and release game/launcher file handles before commit. Surface actual lock owners using existing process-lock support; require consent before termination.
- [x] Run whole launcher tests and core tests; build self-contained x86/x64; expect GREEN. Commit.

### Task 5: Incident reports and native crash/network evidence

**Files:** Create launcher `Diagnostics/IncidentReporter.cs`; modify launcher logging/startup/update/account error boundaries, `src/DFLegacy.Relay.Core/RelayHost.cs`, native diagnostic files/CMake, and relevant tests. Preserve and integrate `tools/debug/crash_capture/` as explicit detailed capture.

**Interfaces:** Typed incident kind plus session/event ID and bounded context; relay termination event carries reason/protocol/connection/bytes/errors. Native reports identify evidence and process/version; no false crash classification on handled exceptions.

- [x] Write failing tests for unique filenames, redaction, bounded retention/repeated error grouping, update success/failure summaries, normal cancellation/EOF/expected handoff suppression, unexpected exit with unknown native cause and unhandled native exception evidence.
- [x] Run focused managed/native tests; expect RED.
- [x] Add best-effort per-incident logs and linked context. Verify native hook signatures; unsupported binaries report missing evidence. Never swallow native faults or automatically attach debugger; detailed crash capture remains opt-in. Cover direct and relayed network events at verified semantic boundaries.
- [x] Run full managed/native relevant suites, real native exception and external capture fixtures; expect GREEN. Commit.

### Task 6: V4 publisher, provider limits and offline GUI

**Files:** Create `tools/release/update_v4.py`, `tools/release/tests/test_update_v4.py`; modify `release.py`, `github_publish.py` only shared integration, packager `{PackagerForm.cs,ClientBuild.cs,DFLegacy.ClientPackager.csproj}`.

**Interfaces:** Python `build_manifest(...)`, `plan_resources(...)`, `publish(...)`; CLI commands `preview/build/publish/verify`, JSON receipts shared with GUI. Produces exact Task 1 schema; content-hash parts and final manifest; accepts prior manifests for cumulative retirement/migrations/resource reuse.

- [x] Write failing tests for protected exclusions, nested arbitrary files, both architecture/flavor variants, >48 MiB segmentation, empty files, omitted file rejects without explicit retire/unmanage, A-to-C cumulative records, metadata/channel identity, all assets uploaded before visibility, GitHub 1000 assets/<2 GiB and Gitee 100 MB/1 GB preflight, no secret leakage, anonymous postpublish verification.
- [x] Run `py -3.12 -m pytest tools/release/tests/test_update_v4.py -q`; expect RED.
- [x] Implement shared CLI/GUI resource selection, include/exclude and diff preview, size/migration/retirement preview, provider/channel choice. Preserve existing V3 bridge generation. Do not upload whole game directory or private metadata by default.
- [x] Run all release Python tests, cross-language manifest fixtures against core parser, packager build; expect GREEN. Commit.

### Task 7: End-to-end Gitee test, documentation and report

> 2026-11-08 user scope update: finish local tests and deliver ZIPs for manual upload. Sanitized public README branch pushed; release tags/attachments and anonymous project download verification remain deferred until that upload. No API token request is pending.

**Files:** Add integration harness under `tools/release/tests/`; update `README.md`, `CHANGELOG.md`, `BUILDING.md`, deployment/update/packager documentation that describes changed behavior; reports under `artifacts/client-update-v4-20261108/`.

- [x] Build isolated x86/x64 normal/test bridge + V4 test assets and independent repair entry; version suffix/channel are explicitly test-only. Inventory/hash before upload, no formal GitHub mutation.
- [ ] Use available Gitee authorization to publish minimal sanitized source note and test assets only. If absent, complete artifacts and all independent tests, then request the needed connection; never print/store credentials in client files.
- [ ] Anonymous clean download from Gitee, both architectures, resume/part reuse; V3 bridge→V4→corrupt one file→only that file fetched; valid-A offline/cancel B; config ports preserved; actual installed hashes checked. Record URL/content hashes/request bytes and outcomes.
- [x] Run `py -3.12 -m pytest -q`, core suite, launcher suite, native suite and packager build once after integration; list any unrelated failures accurately.
- [x] Complete documentation, test report and local commits; fresh whole-branch review, fix important findings with RED→GREEN tests. Gitee test only, no GitHub push/release or online deployment.

## Execution record

Native execution selected as a routine implementation choice following the user's explicit approval to implement and auto-test. Progress and rulings are recorded in `.superpowers/sdd/2026-11-08-client-update-v4/progress.md`; no additional design approval is required.
