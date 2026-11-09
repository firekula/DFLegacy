# PvP Channels and GM Rewards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver four usable PvP rank channels, GM victory-point grants, and once-per-character broadcast mail including offline characters.

**Architecture:** Extend the existing ACT1 server, GM web panel, PVF and client port configuration. Persist operator operation IDs together with character mutations in the existing snapshot, and synchronize online characters through their owning session. Keep PvP rooms isolated by logical channel.

**Tech Stack:** .NET 10/C#, static HTML/JavaScript, existing encrypted ACT1 protocol fixtures, Java PVF reader/writer, native Win32 tests.

**Spec:** `docs/superpowers/specs/2026-11-07-pvp-channels-gm-rewards-design.md`

## Global Constraints

- Work only in the ACT1 linked worktree; no Chapter 2 or production mutations.
- PvP defaults: 51/7004 ranks0–9;52/7110 ranks10–19;53/7111 ranks20–PVF max;55/7112 ranks0–PVF max. Actual shipped PVF maximum is30.
- Broadcast to each existing character including offline; no later characters or already successful recipients in retries.
- VP grants change only `VictoryPoints`, never rating, grade, wins or losses.
- Preserve the separately stashed paused LaunchFlow change and the unrelated untracked acceptance document.
- Use a separate exported PVF and record source/output hashes. No automatic release, push or live reward distribution.

## Review Focus

- A late player save or PvP settlement must preserve a concurrently granted VP balance (Task1/2).
- A response lost after a committed write must not produce a second reward after retry or restart (Task1).
- Retrying an old failed mail batch after another retry succeeded must not redeliver to that character (Task1).
- A non-stackable attachment must have a different instance ID in every character mailbox (Task1).
- A preserved old launcher.json must reach all new PvP ports after updating, without changing credentials or server selection (Task4).

---

### Task 1: Durable operator reward transactions

**Files:** Create `src/DFLegacy.Server/JsonGameStore.AdminRewards.cs`, `src/DFLegacy.Server/AdminRewardModels.cs`; modify the state load and both snapshot writers in `JsonGameStore.cs`; add `tests/DFLegacy.SmokeTests/AdminRewardStoreSmokeTests.cs` and an HTTP acceptance runner.

**Interfaces:**
- Produce `GrantAdminVictoryPointsAsync(string actor, Guid operationId, Guid characterId, uint amount, CancellationToken)` returning operation status and canonical VP balance.
- Produce `PrepareAdminMailBatchAsync(string actor, Guid operationId, AdminSendMailRequest mail, Guid? retryOf, CancellationToken)`, `SendAdminMailBatchAsync(string actor, Guid operationId, CancellationToken)`, and `GetAdminMailBatchAsync(string actor, Guid operationId, CancellationToken)`.
- Persist separate grant and batch receipts in `JsonGameStoreState`; carry them through every store snapshot writer, loading missing legacy fields as empty lists.
- Use `Prepared/Completed/Rejected/Unknown` operation results. A reused ID with different actor or payload is rejected. Prepared batch captures recipient IDs; retries subtract all successful recipients across the original batch family.

- [x] Write/run a real HTTP regression expecting a GM VP grant where the existing server returns404; keep its failing output.
- [x] Add store regressions for offline/online-independent grant, zero/overflow rejection, idempotency across restart, stale save and concurrent spend/settlement.
- [x] Add bulk regressions for frozen recipients, full mailbox, independent item identities, initial/retry/repeated-retry races, deleted/new characters and snapshot failures.
- [x] Implement clone-before-write transactions under `_gate`, with once-per-batch atomic save, existing quarantine rules for uncertain writes and no precommit notifications.
- [x] Run the focused store tests and existing mail/PvP/currency transaction tests; commit only this task's files when green.

### Task 2: GM endpoints, online refresh and UI

**Files:** Create `src/DFLegacy.Server/AdminRewardEndpoints.cs`; modify `Program.cs`, `CharacterSessionRegistry.cs`, `EntranceService.cs`, `wwwroot/admin/index.html`, `wwwroot/admin/admin.js`, `wwwroot/admin/admin.css`; extend HTTP/auth/UI tests.

**Interfaces:**
- `POST /api/admin/characters/{id}/victory-points` takes `{operationId,amount}`.
- `POST /api/admin/mail/broadcast/preview` takes `{operationId,mail,retryOf?}`; `POST /api/admin/mail/broadcast/{id}/send` confirms; `GET /api/admin/mail/broadcast/{id}` retrieves status/results.
- Endpoints consume Task1 methods. Add `CharacterSessionRegistry.NotifyVictoryPointsChanged(Guid)` with an optional lease callback; owning actor reads canonical VP, updates cached VP and sends the correct inventory refresh.
- Existing single-recipient mail route remains available. Form scope selects single/all; all previews recipients and content, then sends explicitly. Result lists failures and retry-only-failed action. Operation IDs survive ambiguous failures within the form.

- [x] Add/run endpoint tests: unauthenticated/player rejection, real admin delivery, malformed payload, unknown/reused IDs and no notifications after failed commit.
- [x] Add/run encrypted session regression verifying an online character receives the new VP and later economic writes preserve it.
- [x] Implement endpoints, session wake and UI, using text-safe rendering and disabled in-flight buttons.
- [x] Test GM forms, preview cancellation, complete/partial results, retry, normal single-recipient mail and player panel separation; commit green changes.

### Task 3: Four rank-aware PvP channels and room isolation

**Files:** Modify `PvpOptions.cs`, `EntranceService.cs`, `PvpService.cs`, `ListenerReadiness.cs`; add `PvpChannelCatalog.cs`/`PvpMultiChannelSmokeTests.cs` as appropriate; update existing deployment/PvP tests.

**Interfaces:**
- Central profile list supplies channel number, display name, port and rank range for the exact defaults in Global Constraints.
- `PvpAttachment` gains an optional logical channel number defaulting51 for compatibility; rooms and lobby updates are visible/joinable only within that channel.
- All four ports are treated as PvP gameplay listeners; server admission uses canonical rating-derived grade and the selected profile.

- [x] Write/run existing-API profile tests expecting IDs51,52,53,55 and ports7004,7110,7111,7112; confirm failure against single-channel implementation.
- [x] Add boundary/admission tests (0,9,10,19,20,30), metadata/greeting tests, and same-channel versus cross-channel room/model/broadcast tests.
- [x] Implement profile generation, port conflict validation, rank admission and room isolation without changing match rewards.
- [x] Run focused tests and existing PvP login/individual/result/restart regressions; commit when green.

### Task 4: PVF, client compatibility and generated configuration

**Files:** Add a repeatable PVF channel export tool with tests under `tools/`; modify manager `ManagerSettings.cs`/`ConfigurationStore.cs`, launcher relay port composition and relevant packager fixtures; export a separately named PVF.

**Interfaces:**
- Export from SHA256 `09e5eefdee0777378a967704eb14c175383eef89ca0f4e27e00dd3409af728cb`; output changes only `etc/channel_info.etc` and `etc/pvpchannellist.etc`, with exact before/after entry verification.
- Standard legacy relay configuration containing PvP7004 gains7110–7112 at runtime; custom entries remain; no launcher.json writes. Fresh manager/offline exports include configured new ports.

- [x] Write/run port-composition regressions on legacy explicit lists, custom ports, disabled relay and generated x86/x64 configurations.
- [x] Implement configuration changes and a guarded PVF export; verify all unchanged archive entries byte-for-byte and source unchanged.
- [x] Validate all four PVF groups/rank ranges and the server/launcher metadata agreement; run native channel-switch tests and relevant managed packaging tests.
- [x] Build local x86/x64 artifacts needed for acceptance without publishing; commit task code/tests when green, keep binary outputs outside git.

### Task 5: Local acceptance, documentation and whole-branch review

**Files:** Update `README.md`, `CHANGELOG.md`, `ADMIN_API.md`, `DEPLOYING.md`, `DEPLOYMENT_MANAGER.md`, compose examples and an acceptance report.

- [x] Run server project suite and relevant manager/launcher/packager/native suites, resolving fixture prerequisites instead of weakening checks.
- [x] Use only copied ACT1 client/test state to verify channel entry/switch boundaries and GM VP/mail workflows; preserve screenshots/logs, clearly separating native UI, HTTP and protocol results.
- [x] Confirm added TCP ports and same server/client PVF in deployment documentation; include old-client update/config migration.
- [x] Request one fresh whole-branch review; fix material findings with failing/passing tests and record limitations.
- [x] Verify diff and clean up only owned running test processes; report local artifacts, evidence and pending deployment steps. Do not push or release.

Execution evidence and baseline fixture limitations: docs/acceptance/2026-11-07-pvp-gm-rewards.md. All planned local work executed; no push or release.
