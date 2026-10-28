# ACT1 Party TCP Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. User has already authorized continuous implementation, tests and native screenshot acceptance. A fresh independent whole-feature reviewer is required; root alone owns native UI and deployment.

**Goal:** Make ACT1 party gameplay fall back to a real account-routed TCP relay after a controlled UDP failure, preserve normal UDP, and then begin the separately bounded monster-position investigation.

**Architecture:** Add an observed native 12-byte transport and hosted relay to the existing canonical game process. GameplayDatagramService remains the single session/context authority, extended with authenticated admission and context generations. The relay carries opaque native payloads; it never writes save files or monster positions.

**Tech Stack:** C#/.NET10, real TCP/UDP sockets and existing smoke executable; original ACT1 DNF.exe and native30AD; Python/PowerShell private evidence tooling; local official ELF and private IDA copies.

**Spec:** docs/superpowers/specs/2026-10-28-party-tcp-fallback-design.md

## Global Constraints

- Follow authorized order: TCP fallback, then monster-position cause, then implementation-ready remaining functionality. No speculative claim of complete restoration.
- Native header12bytes: u16 opcode0,u16 total length2,u32 sender4,u32 recipient8; TCP registration0 exactly12, TCP outgoing1 and UDP outgoing0 become receive3. Opaque bytes preserved, no fabricated native-ready/NAT/controller state.
- Default enabled TCP7005/UDP7005; maximum frame8192; unregistered timeout5seconds. Advertise only to password-authenticated channel sessions and only after validated configuration.
- Original save/config/EXE/PVF and all old evidence remain intact. Private raw auth/save/config never enter public assets or git. New builds use worktree-local isolated artifacts; test TEMP/TMP are per-process local paths.
- Root retains existing D:/codex_worktree/party-chat-gold/DFLegacy branch codex/channel-return-next. Keep primary history and prior frozen packages unchanged; local commits only, no push/merge/PR.
- All gameplay/native claims require actual original screenshots, process/source/loaded-file bindings and independent evidence verification. Preserve failures separately.

## Review Focus

1. Username-only account lookup replacing a password-authenticated relay owner: negative test in Task2; unavailable advertisement in Task3.
2. Two game sessions of the same account reconnecting: latest authenticated admission wins, old teardown and queued frames cannot revoke/deliver to the new incarnation (Task2).
3. Slow peer, TCP fragmentation/coalescing, truncated/oversized frames and UDP overrun: real-socket tests, bounded queues and cancellation/cleanup (Task1/2).
4. Party reuse, dungeon instance changes and PvP room/match changes: capture immutable generation tickets and recheck at output (Task2).
5. Real ACT1 selects the TCP path only under actual UDP failure, with account/header/payload fidelity: controlled peer-sink positive plus normal UDP regression and original screenshots (Task4).

### Task 1: Complete native login and framing contracts

**Files:** Create src/DFLegacy.Protocol/GameProtocolEngine.Login.cs, src/DFLegacy.Protocol/NativeFallbackRelayProtocol.cs, tests/DFLegacy.SmokeTests/FallbackRelayProtocolSmokeTests.cs. Modify GameProtocolEngine.cs, GameProtocolEngine.Pvp.cs, tests/DFLegacy.SmokeTests/Program.cs.

**Interfaces:** NativeFallbackRelayHeader(ushort Opcode,ushort Length,uint SenderAccountUid,uint RecipientAccountUid); NativeFallbackRelayProtocol.TryReadHeader(ReadOnlySpan<byte>,out NativeFallbackRelayHeader), CreateDelivery(ReadOnlySpan<byte>):byte[], ReadTcpFrameAsync(Stream,CancellationToken):Task<byte[]?>. GameProtocolEngine.CreateChannelLoginSuccess(uint accountUid,string relayIPv4,uint relayTcpPort,uint relayUdpPort):GameServerPacket; existing CreatePvpLoginSuccess signature unchanged, common private serializer preserves mode4. Generic Handle Login serializes an empty/unavailable endpoint and unknown UID0; authenticated Entrance overrides with its real identity.

- [x] Write and run actual existing Handle(Login) full-tail literal test: 23 bytes for empty host, UID0, two zero ports, sex1/reliability0; old5byte behavior must fail. Add --fallback-protocol test selector and full-suite invocation.
- [x] Add fixtures for authenticated UID0x11223344, IPv4 127.0.0.1, ports7005/7005, mode0 and exact mode4 compatibility; reject malformed addresses/ports without borrowing serializer output as expected data.
- [x] Add literal frame fixtures for header(0,12,A,A), TCP data(1,17,A,B), UDP data(0,17,A,B), expected delivery(3,17,A,B) and untouched input. Exercise invalid lengths<12/>8192, unknown opcodes, wrong declared UDP size, EOF and fragmentation/coalescing through a real Stream/socket.
- [x] Implement serializers/framing minimally, update the old incorrect five-byte expectation, then run focused and full suite under task-local TEMP/TMP. Record expected RED/GREEN and commit Task1.

### Task 2: Canonical admission, context tickets and real relay service

**Files:** Create src/DFLegacy.Server/FallbackRelayOptions.cs, src/DFLegacy.Server/GameplayDatagramService.FallbackRelay.cs, src/DFLegacy.Server/FallbackRelayService.cs, tests/DFLegacy.SmokeTests/FallbackRelaySmokeTests.cs. Modify GameplayDatagramService.cs, ServerOptions.cs, Program.cs, tests/DFLegacy.SmokeTests/Program.cs.

**Interfaces:** GameplayDatagramService becomes partial. AuthenticateRelayAccount(Guid sessionId,uint accountUid):RelayAccountAdmission? (uses the session's registered TCP address; caller only after verified password). RevokeRelayAccount(Guid sessionId), FindRelayAdmission(uint uid,IPAddress source):RelayAccountAdmission?, IsCurrentRelayAdmission(RelayAccountAdmission):bool, TryCaptureRelayRoute(RelayAccountAdmission,uint recipient,out FallbackRelayRoute):bool, IsCurrentRelayRoute(FallbackRelayRoute):bool. Admission and route records are immutable internal identities and context revisions; factories alone establish canonical authority. FallbackRelayService(ServerOptions,GameplayDatagramService,ILogger<FallbackRelayService>) is singleton/hosted and offers the selected advertised endpoint plus bounded observed status needed for native readiness.

- [x] Write real admission tests before implementation: nonexistent session/UID0/wrong address; same-party town permitted; nonparty/cross-party/different exact instance/PvP room/match denied; normalized IPv4-mapped identity; context leave/rejoin invalidates a ticket; duplicate/new owner retires old; old detach cannot remove new owner. Ensure auth lookup alone has no admission side effect.
- [x] Write real loopback socket tests: TCP registration without ACK; both directions opcode1->3 payload equality; UDP0->TCP3; split header/body and coalesced frames; unknown sender, mismatched registered sender, repeated registration, duplicate connection replacement, stale owner/context queues, missing target; malformed frame/UDP drop and host remains healthy; clean stop releases owned listeners/sockets.
- [x] Add FallbackRelayOptions enabled/default7005 endpoints, IPv4 advertisement and protocol-specific collision checks. Maintain bounded registration time, connection count and per-target delivery queues. UDP cannot stall all users behind one slow receiver; output checks the captured ticket immediately before write.
- [x] Implement authority and hosted service, preserving existing UDP path, party host and object controller. Run focused and complete smoke; Release builds0warnings/0errors; commit Task2.

### Task 3: Real authenticated channel integration and readiness

**Files:** Modify EntranceService.cs, Program.cs, source server.json and README.md; extend tests/DFLegacy.SmokeTests/FallbackRelaySmokeTests.cs. Do not change original runtime config JSON.

**Interfaces:** On successful password authentication, bind the verified account to runtimeSession before replying. Username-only compatibility cannot register/replace owner or advertise a relay. Revoke only the calling session's old identity when switching/login teardown; UnregisterChannelSession retires its admission. Reply uses full mode0 or mode4 serializer and live local fallback endpoint; preserve explicit external Pvp relay settings.

- [x] Write real integration tests for password-authenticated normal login advertising full account/address/ports; wrong password/no owner; username-only login cannot replace another authenticated owner and has unavailable endpoint; current session changing identity revokes its old admission; old channel finally cannot revoke replacement; disabled relay still produces complete login tail.
- [x] Implement hooks/config validation and endpoint selection with minimal Entrance edits. Add structured bounded register/route/drop readiness logging, never passwords or raw payloads. Full smoke and Release build; independent whole-feature code review before native use.
- [x] Publish into a NEW frozen private package, hash all source and binaries and preserve earlier package/native30AD. Resolve Important/Critical review findings by new RED/GREEN and whole-suite verification; commit Task3.

### Task 4: Dual native acceptance, screenshot report and normal delivery

**Files:** New tools/acceptance/party_fallback_evidence.py and its meaningful decoder negatives if needed; knowledge/decisions/2026-10-28-party-tcp-fallback-verification.md and assets/2026-10-28-party-tcp-fallback; update current coverage/index/README/STATUS without changing old frozen artifacts. Private fixture/controllers/closed evidence live in G:/Game/DNF_ACT1/DFLegacy/.tmp/party-relay-sync-20261028 and current plan workspace.

- [x] Preserve fresh original save plus configured normal DataPath if distinct, five configs, four native targets, two EXEs/two PVFs, normal runtime inventory and stopped/process identities. Use an independent clone, owned processes and no business/HP/coordinate injection. Reuse reviewed native file input and readonly observation.
- [x] Observe normal UDP path with two original clients: authenticated login, party creation, selection, combat/clear/return and responsive channel reentry. Record actual path, not an assumed UDP success.
- [x] Controlled fault phase: UDP peer-sink discards peer traffic while NAT/control/TCP stay available. Observe real native TCP registration with canonical account IDs, both-direction data/forwarding and playable party beyond former timeout. Capture original A/B party/world/combat/clear images and exact packets/state/build/PID hashes.
- [x] Independently reassemble relay streams and compare header changes plus opaque body hashes; keep authentication private. Verify routing boundaries, actual canonical state and earlier economy/identity invariants appropriate to natural gameplay. Do not claim opaque application CRC or unmeasured Internet coverage.
- [x] Close fixture; new current build normal delivery with original save/config protection and isolated readiness; final all originally absent game/server/launcher processes remain absent. Verify original hashes, historical artifacts and meaningful smoke/native checks. Commit source/test/report/evidence only; no private data or old artifact overwrite.
- [x] Update queue ledger: TCP accepted -> start a separate monster-sync diagnosis plan grounded in fresh readonly native traces. Continue user-authorized confirmed backlog in bounded independently evidenced groups; evidence gaps stay explicit.

## Execution and evidence notes

### Task 4 amendment: native output queue race exposed by strict verification

Do not mark Task4 complete or deploy `20d996f/30AD` as an accepted TCP package.
Follow [the bounded native repair design](../specs/2026-10-28-party-relay-send-queue.md):

1. Pin newly exported append/flush/buffer/owner original machine bytes and a
   deterministic two-thread interleaving RED; preserve failed fallback-v2.
2. Implement relay-only append/flush serialization with guarded transactional
   x86 installation and retained trampolines. Add meaningful concurrency,
   recursion, bypass, return-value and patch rollback tests.
3. Run focused and full native compatibility, strict new Release builds,
   independent review, and freeze a new source/native package without replacing
   any historical build or evidence. Fix review findings with RED/GREEN.
4. Repeat fresh normal/fault original-client acceptance, complete strict stream
   proofs and verify absence of malformed relay headers. Then finish original
   Task4 delivery, preservation, screenshots and queue update.

Baseline full smoke passed from worktree-local isolated artifacts with task-local TEMP/TMP. Earlier attempts failed first on Windows default TEMP permissions and then on fixture discovery for artifacts outside the repo; all original logs are preserved and no product code was changed to hide either failure. The builder check identified the passwordless lookup distinction and this document includes its explicit negative cases. User's current request and earlier continuous-work authorization cover implementing this concrete existing-project repair without a repeated approval menu.
