# Active bag discard implementation plan

> **For agentic workers:** Use superpowers:executing-plans; root coordinates independent review. Steps use checkbox syntax.

**Goal:** Restore official active dungeon discard for ordinary items and 1..1000 gold, visible to the same party and directly collectible by its current picker.

**Architecture:** Keep ACT1 CMD50 and 17-byte NOTI40. A discard-specific planner enforces supported template rules without changing trade/mail. A focused active-drop transaction reserves an unpublished ground object, persists the existing item-space save, then publishes once to the current room and sends only the dropper its inventory ACK. Existing source0 pickup and durable gold sharing stay authoritative.

**Tech stack:** C#/.NET10, existing smoke executable and synthetic PVF fixtures.

**Spec:** Root's U04 dispatch and the byte audit at `G:/Game/DNF_ACT1/DFLegacy/.tmp/active-drop-rule-audit-20261029/report.md`.

## Global constraints

- Work only in this managed isolated worktree from 626890396dfba6eeed2a345ea04ca3981252efa7.
- Original server/client/PVF and frozen evidence remain read-only; no server, client, network or deployment.
- Active source remains0; private CMD50 ACK; one party NOTI40 including sender; preserve rollback and item metadata.
- Do not modify global CanTrade, natural drop RNG or U03 epic behavior.

## Review focus

- Persistence failure/exception must leave no published or claimable ground entity.
- An async dead peer cannot prevent notification/ACK to healthy members.
- Gold debit and pickup must conserve balance and allow one grant only.
- Room change during persistence must not spawn an old-room actor in the new room.
- Unsupported later-server item flags must be reported as mapping limits rather than invented.

### Task1: Discard eligibility and gold planning

**Files:** `DungeonItemDropPlanner.cs`, new `ActiveDropSmokeTests.cs`, smoke registration.

**Interfaces:** Existing TryPlan remains available; an overload accepts currentGold and returns planned Gold/IsGold with the unchanged-inventory gold plan.

- [x] Write and run RED cases for rarity3, title/avatar, exact sealed byte and sealing-trade.
- [x] Implement discard-only eligibility and explicit error code mapping13/23/17.
- [x] Write and run RED gold cases1/1000/1001/zero/insufficient balance.
- [x] Implement currency planning and verify unchanged source inventory and correct debit.
- [x] Run `dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --active-drop` and commit.

### Task2: Transaction, ground publication and delivery

**Files:** new `DungeonActiveDropTransaction.cs`, `DungeonGroundItemState.cs`, `DungeonInstance.cs`, `DungeonPartyService.cs`, `EntranceService.cs`, `ActiveDropSmokeTests.cs`.

**Interfaces:** Transaction consumes a validated plan, instance/room/dropper coordinates, save callback and private sender; publishes a reserved source0 item/gold only after successful save.

- [x] Write and run RED sender/peer/solo/private ACK, save-failure/exception, unpublished pickup, room-change and peer-failure cases.
- [x] Add source0 gold reservation, object-identity activation and room-scoped notification, then use the transaction from the actual CMD50 handler.
- [x] Exercise real shared item pickup reservation/claim/removal once and durable existing gold distribution after active-drop debit.
- [x] Run active-drop, pickup-removal, party-gold and loot-routing gates; commit.

### Task3: Verification and handoff

**Files:** this plan and `knowledge/decisions/2026-10-29-active-drop-rules.md`.

- [x] Run the regular full smoke executable and Release build; record exact results and any pre-existing failures.
- [x] Inspect scoped diff and commit the evidence/compatibility decisions and remaining real multi-client acceptance matrix.
- [x] Return worktree and commit IDs to root for independent review/integration. No live completion claim.

## Execution ledger

- Baseline `--pickup-removal`: 0 failures, exit0.
- Ruling: root supplied detailed implementation scope and inline execution; proceed with that scope without another design/permission loop.
- Ruling: no additional subagents; root owns final fresh review. Do not archive this worktree before integration.
- Task1: complete, b0438d6. Policy RED4 and currency RED7; active-drop GREEN0.
- Ruling: preserve the exact existing save API; root authorized a narrow pre-commit character rollback after a real file-lock RED exposed memory debit residue. Private SaveLockedAsync boundary marker is invoked only after File.Move succeeds; other save APIs retain behavior.
- Ruling: root authorized existing ephemeral dual-stack wildcard/loopback test fixtures for full regression, with TEMP/TMP within this worktree. No production listener or client was started.
- Full smoke attempt1: interrupted by pre-existing File.Move UnauthorizedAccessException during TradeSmokeTests.CreateCharacterAsync; attempt2 reached CeraGiftSmokeTests.Rejections and encountered the same exception in SendCharacterMailAsync. Both returned -532462766. No blanket full-suite GREEN claim.
- Scoped managed Server Release build GREEN (0 warnings/errors). Whole-solution dotnet build unavailable: five native C++ import errors and three unrelated missing restore assets; retained logs.
- Task2: source commit0fb5754, final active-drop/pickup-removal/party-gold/loot-routing/store-write-transaction all exit0/0 failures. Added a detached-room RED1→GREEN0 after the initial transaction tests.
- Ruling: full regression remains incomplete after two existing File.Move failures in unrelated save callers; retain exact logs and hand this limitation to root instead of changing global file-I/O semantics or claiming full GREEN.
- Final review: delegated by the parent coordinator; this child does not spawn another agent. Branch remains in place for review and integration, with live acceptance outstanding.

### Review fixes (root-authorized, same worktree)

- [x] Actual SerializedWriteStream/TCS queued publication RED: room29→old40, including return to the same coordinates.
- [x] Add write-gate conditional packet entry; capture room generation during activation; wire all four production registrations and solo; never fall back to ordinary Send.
- [x] Pin actual predicate timing after gate acquisition, party/solo revisit and member-incarnation cases, plus missing conditional sender fail-closed behavior.
- [x] Real post-File.Move logger IOException RED in the complete active-drop transaction; isolate that committed diagnostic only for SaveCharacterItemSpacesAsync; keep disk/memory debit, pickup object and40/ACK.
- [x] Assert unchanged logging-exception behavior of SaveCharacterInventoryAsync and existing pre-commit failure rollback.
- [x] Active-drop/pickup-removal/party-gold/loot-routing/store-write-transaction final focused gates0; Server Release0warnings/0errors.
- Ruling: root authorized only known post-commit diagnostic isolation for the item-space save API. Do not retry the economic save, undo a committed snapshot, or globally swallow logging failures.
- Ruling: root required state validation inside the same physical send gate used by NOTI29. The condition sender is explicit and mandatory for active publication; no ordinary-Send fallback and no instance lock across await.
- Review evidence: independent reviewer party_api_review re-ran four focused gates0 and reported both original P2s addressed in the final source difference. Its ignored report is `.tmp/active-drop-validation/independent-review-20261029.md`.
- Full smoke after review fixes: FriendSmokeTests.CheckFriendStorageAsync→CreateAccountAsync→File.Move UnauthorizedAccessException, exit-532462766; exact output retained in review-fixes-full-smoke.log. No global file-I/O workaround and no full-suite GREEN claim.
