# ACT1 Monster Sync Diagnosis Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. Root owns all native input and IDA; independent whole-feature review uses requesting-code-review. The user already authorized continuous groups without repeated confirmation.

**Goal:** Explain the newly captured persistent monster-position difference at the failing boundary, repair only a proven local defect, and retain dual-client screenshot evidence.

**Architecture:** Add an offline application decoder beside the accepted relay decoder; join exact application identities/time bounds to frozen paired VM_READ and original client dispatch. Gameplay remains unchanged until a root-cause regression exists.

**Tech Stack:** Python3.12 unittest, existing original-client readonly readers, private IDA database, C#/.NET10 and Win32 C++ only if a proven repair requires them.

**Spec:** docs/superpowers/specs/2026-10-28-monster-sync-diagnosis.md

## Global constraints

- Preserve original save/config/EXE/PVF, prior frozen packages and failed evidence. New phases/packages have unique names.
- No memory writes, native business calls, injected gameplay packets, forced controller/coordinates or fabricated readiness.
- Source/account/slot/object identities and socket/native application times are separate; decode unsupported forms as opaque with explicit counts.
- Existing relay proof remains strict and independent. No skipped malformed records or resynchronization.

## Review focus

1. Reliable records containing multiple applications or truncated boundaries: pin full consumption and reject truncation.
2. Empty bodies matching multiple cipher candidates: distinguish identical decoded bytes from conflicting interpretations.
3. Cross-room UID reuse and late data: include room/generation context and never compare by UID alone.
4. Duplicate/fragmented wrapper records: follow original sequence/reassembly limits before inferring delivery or GP6 absence.
5. A received packet rejected by native state: correlate actual GP4 room/declared-player checks and GP6 dispatch, not packet counts alone.

### Task 1: Original receive contract and deterministic offline decoder

**Files:** Create tools/acceptance/act1_gameplay_evidence.py, tests/test_act1_gameplay_evidence.py. Retain private raw output in a new monster-sync namespace. Public vectors must contain gameplay only, never authentication.

**Interfaces:** DecodedApplication(mode,protocol,payload,codec); MtApplicationRecord(block_type,sequence,sender_slot,record_index,application). decode_application(raw) validates exact header/CRC and known encoding; decode_mt_blocks(raw) consumes every byte of ACK/reliable/unreliable forms. Nested GP42 has a separate bounded interface only after verifying original framing.

- [x] Cross-check GP4/GP6/GP42 and cipher/reader original functions, relevant vtables and local-object test against original PE bytes. Record exact field/length semantics and unresolved items.
- [x] Add independent literal/native gameplay vectors and tests for plaintext, observed cipher, multiple reliable records, ACK length, fragmentation/truncation, ambiguous/unsupported forms and hard bounds. Watch missing implementation fail.
- [x] Implement minimal strict offline decoding; run focused tests and frozen-phase analysis without modifying any capture. Commit tested decoder and contract notes.

### Task 2: Join the sustained divergence to application/native state

**Files:** Add bounded analysis helpers beside tools/acceptance/act1_gameplay_evidence.py, including act1_stream_timing.py and tests/test_act1_stream_timing.py; new knowledge/decisions/2026-10-28-monster-sync-diagnosis.md and sanitized assets.

**Timing amendment after Boss-map delivery:** Keep the accepted RelayStream decoder unchanged. A timed wrapper records each complete relay frame's first/last contributing socket-log record timestamps, source lines and exclusive byte offsets. Fragmentation/coalescing must not stamp the entire frame solely with its final chunk; empty chunks contribute no bytes. Exact normalized relay fingerprints remain separate from MT/application identity, room/generation and native execution. Repeated identical frames remain separate observations, and ambiguous send attempts must not become an invented one-to-one time match. Validate bounded byte inputs and monotonic metadata, consume every byte, and make malformed/unfinished streams terminal. These are socket-hook log bounds, never native dispatch/application times or proven network latency. Literal split/coalesced/duplicate/truncated fixtures must exercise real parsing under RED/GREEN before frozen analysis.

- [x] Preserve timestamped original TCP stream-completion bounds and UDP records, with exact fingerprint linkage and tests; do not label socket time as native application time. Three completion-only prototype assertions RED, nine boundary tests GREEN;25ACT1+8relay tests and independent read-only review passed. Existing relay/gameplay decoders unchanged.
- [x] Decode the fallback-v3 boss UID3 window and normal reference. Account for GP42/reliable nesting before interpreting missing GP5/GP6. Exact67B_TX/A_RX and82fresh-M1 pairs retain whole-envelope provenance; observed log deltas0–16ms/1–16ms, not apply times. Normal reference is a different map/seed. GP42 inner remains opaque; no GP5/6 absence claim.
- [ ] Join object kind/UID, room and declared player to paired state; compare native movement flags and selected object. Test room reuse/late frames and duplicate identity cases.

  Partial/boundary: freshM1 has285paired samples with actualStage(2,1), stable per-role map and selected object pointers, engine flag1, same associatedUID2 and identical targets at the largest121.37 gap. Actual original15function RAM bytes match PE on both clients. The oldv3 window lacks actualStage fields; a general native generation/application join has not been implemented or claimed. Specific receiver-gate execution, GP42 inner action stream and typed motion-state effects remain unresolved; no guessed owner/coordinate repair.
- [x] If needed, run one short fresh matched two-client capture with original screenshots and extra readonly fields. M1 supplies Stage/current/target/association,30RAM/PE checks and344frozen files. Independent final reviewer verified all source sampling/differences,21analysis files, six original pictures and corrected automatic EPLP return. Scoped result: unresolved native application contract; position not fixed.

### Task 3: Proven repair or documented boundary, then next ready group

**Files:** Exact repair files and one regression are chosen by an explicit root-cause amendment after Task2; no speculative source edit.

- [ ] For a proven defect, add the failing regression, implement minimally, verify meaningful full suites, independently review, freeze a new package and repeat matched normal/fault dual-client acceptance with screenshots. Deploy with the same original-input protection.
- [x] For the current unresolved boundary, preserve the exact gap and stop speculative gameplay changes; do not mark position fixed. Specific receiver-gate execution/GP42 inner/motion semantics and general native generation/application join remain pending. Carry forward the user-authorized revival-purchase contract investigation, then other confirmed ready functions.
- [x] Update coverage/report/ledger with actual build/phase identities and scoped verdicts; local commits only. Offline sourceb7a3b56, M1managed20d/native8AB, current deliveredBossmanaged596/native8AB stay distinct. Evidence/report commit follows this checklist update; the unproven-repair/general-native-join boxes remain open rather than manufacturing success.
