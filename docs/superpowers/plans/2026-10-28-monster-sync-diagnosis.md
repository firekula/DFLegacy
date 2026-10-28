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

**Files:** Add bounded analysis CLI/functions in tools/acceptance/act1_gameplay_evidence.py; tests/test_act1_gameplay_evidence.py; new knowledge/decisions/2026-10-28-monster-sync-diagnosis.md and sanitized assets.

- [ ] Preserve timestamped original TCP stream-completion bounds and UDP records, with exact fingerprint linkage and tests; do not label socket time as native application time.
- [ ] Decode the fallback-v3 boss UID3 window and normal reference. Account for GP42/reliable nesting before interpreting missing GP5/GP6.
- [ ] Join object kind/UID, room and declared player to paired state; compare native movement flags and selected object. Test room reuse/late frames and duplicate identity cases.
- [ ] If needed, run one short fresh matched two-client capture with original screenshots and extra readonly fields. Freeze and independently review the diagnosis; record a concrete cause or a bounded unresolved contract.

### Task 3: Proven repair or documented boundary, then next ready group

**Files:** Exact repair files and one regression are chosen by an explicit root-cause amendment after Task2; no speculative source edit.

- [ ] For a proven defect, add the failing regression, implement minimally, verify meaningful full suites, independently review, freeze a new package and repeat matched normal/fault dual-client acceptance with screenshots. Deploy with the same original-input protection.
- [ ] For an unresolved/original behavior, preserve the exact gap and stop changing that subsystem; do not mark position fixed. Carry forward the user-authorized revival-purchase contract investigation, then other confirmed ready functions.
- [ ] Update coverage/report/ledger with actual build/phase identities and scoped verdicts; local commits only.
