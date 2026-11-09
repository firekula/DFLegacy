# Dungeon completion and solo loot gathering

User added two changes while the approved bootstrap update work was underway: fix Sky Tower boss-death exit/idle failure, and exact solo chat `牛来` gathers ground drops to the player's feet. Implement after the bootstrap lifecycle, then exercise the changes with a local server and Gitea test update. No production service or GitHub publication. User is away and explicitly requested autonomous completion/report, so no additional design approval is needed.

## Evidence and boundaries

Read-only investigation proved `DungeonInstance.TryMoveRoom` can advance after boss completion and clears `ClearEnabled`, while subsequent settlement requires that state. This establishes an invalid post-clear transition, not proof of the reported automatic death/crash. The idle-crash branch needs separate logs/reproduction. Existing native boss cleanup scans current map actors and excludes player kind; do not blame it or change native ABI without evidence.

Implement a shared server-side final-dungeon completion guard covering solo and party room transitions, preserving generation and settlement state. Keep ordinary room transitions and hell-room return behavior. Validate any legitimate post-clear map exception against current PVF/reference evidence; default regular completed runs must not re-enter rooms.

`牛来` is an exact chat command, available to ordinary accounts only while alive in an active solo dungeon instance. Accept both normal-map (type 3) and dungeon/default party-chat (type 2) envelopes; the latter does not imply that the sender has a party. A live CMD 17 at 2026-11-08 15:16:34 carried body `34000201000000000004000000C5A3C0B4`, which the original type-3-only gate incorrectly routed to zero party recipients. Actual instance membership, not the chat envelope, determines solo eligibility. Whisper/system/direct-output types remain excluded. It moves existing visible drops in the current room once, not a continuous magnet and not an inventory grant. Repeated commands collect newly spawned drops too. Party/PvP/town and dead-player contexts do not move anything.

Server retains authoritative item identity/ownership/pickup handling. Current pickup validation is by ground ID, room and pending claim, not runtime physics coordinates. Server passive drops include unopened containers, so never generate or relocate server-only passive entries. Client must enumerate actual active ground actors and preserve ID, metadata, amount, ownership and pending-pickup state. No item removal/recreation packets and no GET_ITEM calls.

## Integration proposal

Server authorizes exact `牛来` and sends only the requesting session a Type16 message with exact ASCII body `@DFLegacy:loot-magnet:v1@` and its local user ID. This uses an existing harmless chat packet so an old client does not receive an unknown opcode. New native patch recognizes only this exact system marker, consumes it, validates live solo dungeon/player/map state, and queues one frame-safe relocation using known native position setters and protected mirrors. Verify the message function ABI before installing its hook; do not collide with the existing Type16 sound gate or boss frame hook. Fail closed with diagnostics if native signatures/layout differ. A client that lacks this feature can only display a marker, not gain items.

Tests must bind the server payload and native parser exact bytes. If investigation establishes a safer existing control path, record that ruling before changing the contract.

## Validation

- Server RED/GREEN tests for post-boss solo/party moves, generation/settlement unchanged, normal moves preserved, duplicate/delayed completion.
- Server command tests: exact message/type, normal account support, solo/alive/run gating, single-recipient marker, party/town/dead rejected.
- Native policy and guarded runtime tests: exact marker, ordinary messages untouched, active visible ground only, pending/drop identity protection, same map/scene, repeat command, no inventory/pickup calls.
- Local copied PVF, cloned saves, isolated server/client. Reproduce boss death with immediate door attempt and with idle wait, record HP, map/generation, settlement packets and crash evidence. Test gather positions before/after, repeated/new drops, quantity/metadata/inventory unchanged until manual pickup, and party rejection.
- New Gitea test release includes actual new client DLL. Test update via fixed icon, plus genuine interrupted update recovery. Illustrated report distinguishes verified behavior from any unreproduced crash symptoms.
