# Per-maze Boss Map Specification Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. The user specified the defect/configuration and authorized continuous restoration; root owns native input/deployment. Independent requesting-code-review before delivery.

**Goal:** Select the configured dedicated Boss-map candidates for each maze, including Lorien maze2 boss(2,2)→MAP6416, and pin the configuration with a regression.

**Architecture:** Extend DungeonMazeDefinition with an initialized dedicated dictionary, reuse section-aware map-spec parsing, and consult this list before generic map specs in ResolveMapId. Reuse existing valid-role/topology fallback and room cache.

**Tech Stack:** C#/.NET10, existing smoke executable and literal ScriptFileSystem fixture, original PVF read-only, original ACT1 clients/native8AB if run live.

**Spec:** docs/superpowers/specs/2026-10-28-boss-map-specification.md

## Global constraints

- No changes to original PVF, saves/configs/EXE or frozen evidence; new builds/phases/receipts use unique names.
- Boss-specific candidates apply only at the selected boss coordinate and remain local to their maze.
- Valid explicit external-dungeon Boss maps are allowed; normal/dummy candidates cannot replace a Boss through this list.
- Preserve cached/quest replacement, absent/invalid-list fallback and ordinary-room behavior.

## Review focus

1. Three-maze parsing cannot leak the last section to previous mazes.
2. MAP6416's dungeon51 ownership and topologyC must not discard the explicit override.
3. Invalid IDs/coordinates, absent maps and normal/dummy candidates retain safe existing fallback.
4. Ordinary/start coordinates with a dedicated entry do not become Boss rooms.
5. Multiple valid candidates and revisits preserve selection/caching; active quest substitutions still win.

### Task 1: Functional RED/GREEN and minimal selection repair

**Files:** src/DFLegacy.Server/DungeonCatalog.cs; new tests/DFLegacy.SmokeTests/DungeonMapSelectionSmokeTests.cs; tests/DFLegacy.SmokeTests/Program.cs.

- [x] Add literal three-maze/configured-map fixture, actual room/monster assertions and focused --dungeon-map-selection selector; invoke tests in complete smoke. Observe current valid implementation return13 instead of6416.
- [x] Add initialized BossMapSpecifications dictionary, parameterize the existing section parser, populate it per maze, and consult it only at a selected boss coordinate before generic specifications.
- [x] Exercise legacy absence, maze isolation, ordinary room, invalid candidate, alternate valid selection and cache regressions. Run focused/complete smoke and Release0warnings/0errors; independently review and resolve Important issues with RED/GREEN. Independent review identified Start/Boss overlap; public1x1 regression failed before the role==Boss guard, then passed. Actual-PVF focused test and complete smoke passed; strict Release reported0warnings/0errors (boss-map-full-smoke-v2.log, boss-map-release-v2.log).

### Task 2: Actual PVF/build delivery and user-visible acceptance

**Files:** new knowledge/decisions/2026-10-28-boss-map-specification-verification.md and sanitized assets; update current coverage/README/STATUS. Private namespace .tmp/boss-map-spec-20261028.

- [x] Confirm the real current PVF yields maze2/3x3/boss2,2/MAP6416/monster1077 with the repaired catalog. Preserve original PVF hashes.
- [x] Freeze a new managed build with retained native8AB and actual source identity; root ran the configured3x3 through both original clients:6416,9monsters,1077Boss625→0. B died, A cleared; both Continue and leader-B Town return observed. Eight original pictures, all failed inputs and915 raw/harness files preserved; postcommit smoke uses identical three managed DLL hashes.
- [x] Deliver the verified package to the normal runtime with49 new preimages, Windows PowerShell5.1 isolated readiness and original-input/historical artifact checks. Keep old20d/8AB acceptance and all failures immutable. New managed59666d9; Server0E5767, Protocol79295A, retainednative8AB.
- [x] Commit source/tests/report with a scoped result, close this plan, then continue checkpointed monster-sync S02 and other confirmed unfinished functionality. Source59666d9; final independent reviewer verified12public assets/8original pictures,915frozen files, initial/final loaded bindings, actualroute/625→0/Continue/Town and49-file delivery, no remaining Critical/Important. Final checker passed with all owned processes0. Documentation/evidence commit follows this checklist update; monster-sync remains active.
