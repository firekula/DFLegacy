# Chapter Two main synchronization and test continuation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Independent read-only client and resource audits use dispatching-parallel-agents.

**Goal:** Merge main 209a1da into codex/chapter-two, retain paused work, and continue reproducible isolated Chapter Two tests.

**Architecture:** Merge shared server/launcher changes while preserving the Chapter Two bundled-client boundary. Compare resource semantics before changing C2 data; resume paused native display work separately from the merge.

**Tech Stack:** .NET 10, PowerShell, CMake/MSVC Win32, existing PVF tooling and native image execution fixtures.

**Spec:** The user's instruction to synchronize main and continue Chapter Two testing, with the isolation constraints recorded in knowledge/decisions/2026-11-09-chapter-two-native-retest.md.

## Global constraints

- Work only in the existing D:/codex_worktree/dnf-s1c2-overnight/DFLegacy worktree on codex/chapter-two.
- Do not modify main, production, the daily client, or publish/push updates.
- Preserve paused source, test state, binaries, logs and screenshots; distinguish native observations from offline checks.
- Keep ordinary delivery resources separate from the DO-NOT-SHIP five-rarity fixture.

## Review focus

- Cached bootstrap must not suppress main update discovery; bundled C2 must retain its disabled main-update path.
- Keep C2 atomic-save, idle balance and inventory tests alongside the new main tests.
- Preserve friendly/non-hostile actors and normal boss settlement under the shared dungeon changes.
- Apply resource changes by C2 evidence, never by copying C1 IDs/offsets.
- Native cache/display changes need actual C2 image execution, signature guards and the required game TLS offset.

## Tasks

- [x] Back up the five paused free-card files and preserve stash 42ead8b3a819807a75e59b2f2a22dc492dab5b59 under a dedicated ref.
- [x] Fetch origin without modifying main; confirm local main and origin/main both point to 209a1da.
- [x] Resolve MainWindow.Updates.cs by keeping the C2 guard and the new main background-discovery behavior; preserve both SmokeTests/Program.cs test entry sections.
- [x] Run the default server smoke suite, launcher/update tests, manager/browser tests and relevant native fixtures in new isolated artifact paths. Record failures before fixing them.
- [x] Review independent client and server/PVF audits. Generate new paired resources only if an evidenced C2 semantic delta requires them. Existing ordinary C2 resource pair remains valid; C1-only IDs are not copied.
- [x] Commit the validated merge. Restore the paused free-card work from its exact ref and complete/test/review it as a separate change. Merge 35cdf41; fixes 7cd7c4b, with native UI verification tracked separately.
- [x] Build isolated server/launcher/native outputs. Continue the stopped native matrix: completed empty-bag five-rarity pickup/equip and new four-party private HUD acceptance; ordinary four-party first-room combat plus three-party next-room transition recorded. Full four-party cards and commercial drag/drop cases remain explicit follow-up work.
- [x] Preserve current test results and unfinished items in the report; cleanly stop owned test services at handoff. Both four-client runs exited normally; final owned service saved/stopped and no .2 listeners remain.
