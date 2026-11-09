# C2 HUD portrait cache local patch — design and implementation plan

Goal: restore the original 500ms, eight-slot portrait refresh rotation when four team portraits share one rendering pass. The accepted read-only design and original-instruction RED are in `dist/chapter-two-native-retest-20261109/party-hud-native/REPORT.md`.

Architecture: wrap only the two C2 party drawing calls (91DBF6 and91DBFF) in a stack-scoped rendering context. At90E4FA, lazily call the original9C0E00 timer once per matching context and reuse its bool for subsequent slots. Keep original timer state, count&7, UID/dirty immediate redraw, image drawing, coordinates and slot meanings. Other callers forward unchanged. No polling or frame acceleration.

The host requires `gameTls - __tls_start == 8`. The implementation therefore adds no TLS storage: a rendering-thread ID binds the scoped context, and calls on other threads forward to the original timer. Nested contexts restore the previous stack context. This preserves the host's game TLS layout.

Install only after all three call bytes, caller dispatch context, complete cached-portrait branch and original timer signature match the known C2 image. Validate everything before writing. A mismatch performs zero writes; writer failure rolls all calls back. Do not patch ACT1 or any running process.

Files: new `ChapterTwoHudPortraitCache.{h,cpp}` and `hud_portrait_cache_tests.cpp`; minimal additions to native CMake and `Build-PresentationHost.ps1`; this knowledge note. Outputs only under new `dist/native-hud-fix11`.

Implementation steps (inline; root arranges independent review, no commits):

- [x] Write the native execution fixture first. Map the real C2 PE at400000; execute the original group call sites, original90E440 cache branch and original9C0E00 timer. Stub only external clock/drawing and the group's unrelated UI work.
- [x] Run baseline against the real original bytes and observe four-slot refresh assertions fail, with only the first drawn slot periodically refreshed.
- [x] Implement guarded installation and scoped timer reuse; run the same execution fixture with the actual installed patch. Verify 1/4/8 slots, reverse order, first-draw source unavailable then ready, UID/dirty immediate redraw, nested and outside-scope calls, stack/register contracts,500ms timing and count&7.
- [x] Exercise every signature mismatch and partial/mixed installation with zero writes; inject writer failure before/after each call mutation and verify rollback; verify idempotence.
- [x] Integrate the host call, build the complete native host into newdist, run all native neighboring targets and the available cross-town fixture, and check the required TLS offset.
- [x] Record source/artifact hashes, RED/GREEN logs, limits and review handoff. Do not claim four-window GUI acceptance.

Review focus: a source that becomes drawable after its first empty cache; timer tick at the500ms boundary; all slots dirty without timer advancement; nested/different-owner rendering and unrelated timer calls; incomplete signature/partial installation. Each is covered by the native execution or installation fixture.

Progress: design authorized by root's implementation task; existing linked worktree `codex/chapter-two`, clean baseline. No applicable AGENTS.md found in ancestors or native source directory. TDD, writing-plans, executing-plans and verification skills read. Root's explicit scope/no-commit/no-new-process-controls requirements override workflow defaults.

Implementation result: the actual unpatched x86 fixture failed three foreign-slot assertions (`5,0,0,0`,40 timer events); the same fixture with the compiled production installer/hooks passed103547 checks (`5,5,5,5`, same40 events). This is the actual local C2 patch, beyond the earlier Unicorn candidate model. Complete native host build and all presentation/inventory/policy/party/town-map/cross-town neighboring tests passed. New output is `dist/native-hud-fix11/native`, with logs and hashes in `dist/native-hud-fix11`.

Ruling: signature context now begins at91DBE7 on an instruction boundary and includes the complete9C0E00 ret4 through9C0E6F, strengthening the earlier read-only proposal without changing behavior.

Final fixed: the public runtime installer must reject a verified heap copy because its original routines use fixed VAs. An actual heap-input assertion first failed (`base-guard-red.log`), then passed after adding an explicit400000 guard; the detail installer remains available for detached-image signature tests. Independent review identified the same boundary. All native targets were rebuilt and the complete neighboring suite rerun successfully after this change (`final-build.log`, `final-suite.log`).

Limit: this **C2 本地补丁** fixes periodic-cache starvation after a drawable source exists. It does not claim to establish why a private-room first draw was empty. Live four-window visual acceptance and independent review are pending. No current native client/service was restarted, patched or otherwise controlled; no git commit was created.
