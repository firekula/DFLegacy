# ACT1 party TCP fallback and monster-sync investigation design

Date: 2026-10-28. Base: e6baf58cac36a7b9c878aa8173c2b4bec257f994 (actual git HEAD below is authoritative if this transcription differs).

## Intent and authorized order

The user explicitly asks to repair the party TCP fallback after UDP handshake failure, then diagnose monster position synchronization, compare official server behavior with ACT1, ship meaningful tests and dual native-client screenshots, and then continue confirmed unfinished functionality. Continue autonomously within this scope; preserve original configurations/saves and earlier acceptance evidence. The first deliverable is a working native fallback, not the entire remaining game.

## Evidence and present defect

- Ordinary channel LoginCommand still emits only 01 00 00 00 00. ACT1 sub_40F290 reads four flags, accountUID, a counted relay address, two u32 ports, sex and reliability bytes after the success byte. The real channel must supply its authenticated accountUID, never a session token or uninitialized bytes.
- Official original relay ELF SHA256 39C87B68408A82F840808C09C520B911C01176349AF9EAA3BF071CCE2295C3EB. TCPHandlerRelay::dispatch 0805B958 registers a nonzero source account from header+4 for opcode0; duplicate registration replaces the previous TCP connection. Opcode1 relays. relayToTCP 08051B76 uses destination account at header+8 and changes opcode to3. UDPHandlerRelay 0805E4C0 accepts opcode0 and relays to TCP. Packet length is little-endian u16 at+2; native header is12bytes.
- ACT1 sub_9060D0 sends a12byte registration [u16 opcode0,u16 size12,u32 ownAccount,u32 ownAccount]; sub_906500 changes outbound relay opcode to1. sub_905F80 delivers the full received relay frame to the native MT consumer. Use current private exports and machine-byte comparisons before accepting any guessed decompiler parameter type.
- Source material is newer official server plus original ACT1 executable, so use matching observed semantics, not a claim of identical server versions. No new authentication token or native packet fields are invented.
- Existing IPv6 proxy and gameplay UDP relay remain separate transports. Current canonical gameplay contexts already distinguish authenticated channel session, party+exact dungeon instance, and PvP room/match.

## Approaches and implementation choice

Chosen by implementer under user's explicit repair authorization: implement the observed relay transport as a hosted module in the existing game process. This reuses its single canonical session/context authority and does not create competing JSON writers. A separate official-style relay executable would need another state/admission interface and is outside this repair; changing NAT labels or faking peer-ready would hide the defect and is rejected.

## Protocol and configuration

1. Ordinary mode0 and existing PvP mode4 have a shared complete ACT1 login-tail serializer. Preserve mode4 bytes and its existing explicit external relay configuration. The stateless generic handler has a complete empty-endpoint tail; the real authenticated EntranceService always supplies the canonical accountUID and selected live relay endpoint.
2. NativeFallbackRelay framing: u16 opcode at0, u16 total length at2, u32 sender at4, u32 recipient at8, opaque remaining bytes. Registration0 is exactly12bytes; TCP data1 and UDP data0 become TCP receive3 with every other byte preserved. Parse TCP fragmentation and coalescing; UDP must contain exactly one whole declared frame.
3. Local input bounds: minimum12, maximum8192 (ACT1 sending buffer); reject malformed or unsupported frames and excessive unauthenticated clients. These are local hardening limits, not an official maximum claim. Registration is bounded by a5second timeout; registered idle clients are not arbitrarily kicked for lack of gameplay.
4. New FallbackRelay options, enabled by default for the requested normal deployment; TCP7005 and UDP7005, with explicit disable/bind/advertised-host/port controls. Bind host follows the game bind host unless set; advertised address resolves from the existing ACT1 dial-host policy and must be an IPv4 address. Validate TCP conflicts with entrance/additional/game/PvP/admin/channel-link/probe ports and UDP conflicts with existing character/gameplay endpoints. Disable removes advertised availability but still serializes complete login bytes.
5. Source server.json is documented; original five runtime configurations remain byte-preserved. The normal startup helper and source/native evidence are reviewed according to actual changes; no old frozen packages or deployment receipts are overwritten.

## Identity, routing and lifetime

- Admission comes only after successful password verification by AuthenticateAsync, before login reply. Existing username-only FindAccountAsync compatibility is NOT authentication: it may continue ordinary login but cannot create/replace relay admission or advertise fallback. Revoke only the same game-session old admission when its login identity changes; never revoke another session owner merely on a name lookup. Bind accountUID to its current game session incarnation and normalized remote address. Native wire has no credential secret; this is a local authenticated-game admission/IP check, not a claim of new cryptographic relay authentication.
- Relay account registration must resolve to that admission; data sender must match the registered account. Reject unknown identities and cross-address attempts. A newer authenticated game owner or duplicate relay socket retires old ownership; old detach may not revoke a new owner.
- Only current same-party+same exact dungeon-instance participants or same PvP room+match can route; no arbitrary global forwarding, no self echo. Town party handshakes may share the null instance. Reuse gameplay context truth; no second membership authority.
- Capture sender and recipient session/context generations for queued deliveries and recheck immediately before write. Leaving/rejoining the same numeric party, reconnecting or changing context invalidates old queued traffic. Serialize writes per recipient; do not hold game/context locks while awaiting socket IO.
- Disconnect/stop cancels and disposes only owned sockets, closes both listeners and leaves save ownership unchanged. Failed/invalid network input does not mutate game economy, world state, monster coordinates or NAT type.

## Acceptance

- TDD: full login literal fixtures; native relay framing; real loopback TCP segmentation/coalescing, registration/replacement, bidirectional TCP forwarding, UDP-to-TCP and unregistered/wrong owner/cross-context/stale generation/malformed/bounded shutdown negative cases. Full existing smoke remains green, Release builds clean.
- Fresh isolated save/config backups, exact binary/source hashes and PID/start identity; preserve original save, config, EXE/PVF and old frozen evidence. Root alone operates native clients. No business packet injection, client HP/coordinate/state writes, fake peer-ready or NAT labels.
- Native positive: normal UDP party/enter/combat/change-room/return regression; controlled test-only UDP peer blackhole while preserving NAT/control paths, real fallback registration and both-direction data, native party maintained and dungeon playable. The blackhole is explicit fault injection through the existing bounded peer-sink diagnostic, not a successful native business packet substitute. Test recovery/cleanup as observed; do not claim unrestricted NAT/public Internet coverage.
- Capture actual originals before/after, native socket streams, service forwarding records, canonical state and CRC only where actually decoded. Screenshots show party/world/combat/clear with captions bound to phase/hash; failed attempts remain failures. Private authentication/raw state stay private.
- A current source build and new immutable test/deployment package are required; retain native30AD unless a separately justified and tested native correction is needed.

## Monster position phase

After fallback is accepted, reproduce gunner leader plus priest with readonly VM observations of room/epoch, objectUID, object-controller, GP4/5/6 and coordinates around visible correction. Distinguish controller handoff, delayed/reliable traffic, attack/knockback and interpolation. Party host is not per-monster owner. Do not force leader-only monster authority or rewrite coordinates without causal evidence. Add a failing regression for the actual cause, implement the minimum official/ACT1-compatible change and repeat dual-native screenshot acceptance. If it cannot reproduce, record the measured negative result and instrumentation rather than invent a fix.

## Further queue

After these two priorities, continue the confirmed backlog in independent bounded groups. Inspect existing official/ACT1 evidence first; implementation-ready branches proceed with tests/native pictures. Missing rules/data remain explicit and can be researched further under this authorization. Do not label factory stubs, retained opcodes, automatic tests or seeded state as a native-complete guild/report/fatigue/COF implementation.
