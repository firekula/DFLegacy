# Chapter Two four-client card matrix implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to execute the already authorized isolated work in this session. Preserve test artifacts as requested by the user.

**Goal:** Verify R01–R04 from the spec on four isolated C2 clients, with independent rendering and persisted-reward evidence.

**Architecture:** Freeze per-case conditions first. Exercise the actual C2 resource catalogs and production party settlement code offline; prepare separate untouched native-before states. Gate native runs on a verified C2 test-control adapter and a capture process ready before the boss dies. A correct offline receipt never closes the rendering check.

**Tech Stack:** Existing .NET 10 server classes, actual C2 paired PVF, PowerShell/Python evidence tools, x86 native fixtures, Computer Use.

**Spec:** `docs/superpowers/specs/2026-11-10-chapter-two-cards-matrix-design.md` and the user's explicit four-condition request.

## Global Constraints

- Only the existing `codex/chapter-two` worktree; no main, production, daily-client changes, push or publishing.
- No alteration of the original automatic card or town-return timers.
- Separate DO-NOT-SHIP states and preparation controls from ordinary delivery packages.
- Preserve raw failures, successful outputs, screenshots and logs; describe local compatibility work as 本地补丁.
- Unconfirmed C2 fields require original-image evidence before implementation.

## Review Focus

- A nominal manual case silently falls back to automatic selection: assert the actual trigger per participant.
- A peer sees the receiver's reward: cross-select columns and compare every observer to every owner.
- Black-diamond extra rewards go to an ordinary account or the wrong slot: check membership and per-owner grants.
- Empty gold/premium rewards retain stale icons: include empty and nonempty outcomes, verify group isolation.
- Repeated clicks or timer completion duplicate rewards or charges: reconcile per-card receipts, unique instances and exact balances.

## Task 1: Freeze conditions and prepare reproducible case copies

**Files:** spec above; `dist/chapter-two-cards-matrix-20261110/cases-v2/matrix.json`; isolated per-case `native-before.DO-NOT-SHIP.json` and receipts.

**Interfaces:** Input is the preserved post-settlement observation from the preceding four-client run. Output is four cases with `caseId`, `blackDiamond`, `manualFree`, `buyGold`, `memberOrder`, `freeColumns`, `paidColumns`, protected source hashes and readiness status.

- [x] Confirm source and ordinary resource digests, four accounts and legal inventory/funds.
- [x] Clone each case into a new directory; activate black diamond in R03 through the existing store method only. Leave R01/R02/R04 inactive.
- [x] Verify four distinct case identities, source unchanged and no running-client state changes.

## Task 2: Check the four cases against actual C2 catalogs and storage

**Files:** `dist/chapter-two-cards-matrix-20261110/offline/Program.cs`, `CardMatrix.csproj`, per-case check output and local publication fixtures. No production change merely to make this check pass.

**Interfaces:** Consume Task 1 copies, actual C2 `ClearRewardCatalog`/`ItemCatalog`, and `PartyCardSettlementFlow`/`PartyCardOwnColumnCommitter`. Output expected free/premium/paid contents per owner, shared publication snapshots, saved receipt counts and new instance IDs. Keep these outputs separate from native-before states.

- [x] Exercise automatic free, manual free, premium manual free plus paid, and ordinary manual free plus paid.
- [x] Cross-select free/paid columns, vary call/member order, compare all receiver publications and premium eligibility.
- [x] Verify saved balances and item ownership, repeat selection without duplicate grants/charges, and reload the saved state.
- [x] Run the existing focused party-card checks and C2 actual-image display fixture. Label all of these offline.

## Task 3: Ready the native control and capture prerequisites

**Files:** A new isolated test-control evidence/build namespace; existing C2 native source only after the original-image contract is established. Normal delivery remains unaffected.

**Interfaces:** Need a C2-native death entry with verified signatures, actual local-authority target, process/scene/room identity and explicit test-only enablement. Need four selected game windows, capture destinations and a settlement trigger. Produce a readiness record before any new matrix boss kill.

- [ ] Recover and verify C2 death/control contracts; do not load the ACT1 control module directly.
- [ ] Add the minimal adapter behind explicit isolated-test enablement, with refusal tests and original-image execution checks before native use.
- [ ] Validate the capture sequence before killing the boss; retain all four windows through the card-result phase.
- [ ] Confirm manual selections can occur before each original deadline. If a participant times out, repeat that sample without relabeling it manual.

## Task 4: Execute R01–R04 and reconcile all views

**Files:** Per-case native screenshot/log namespaces, renderer/owner matrix and persisted reconciliation; update `knowledge/decisions/2026-11-10-chapter-two-continuation.md`.

**Interfaces:** Consume Task 1 native-before states and Task 3 readiness, freeze the actual native round's contents, then compare four observer views and four persisted owners.

- [ ] Run the four exact conditions; rotate leader/member order and use different free/paid column permutations.
- [ ] Preserve each free, premium and gold result, including empty results and at least two distinct owner contents.
- [ ] Verify every owner/observer pair, actual manual/automatic trigger, all balances, per-card commits and unique instances.
- [ ] Mark native rendering and receiving separately. Stop only owned test processes when the authorized testing session finishes; preserve builds/logs/states.

No checkbox above is native-pass evidence by itself. The final table must retain missing or failed cells until the corresponding observed result exists.
