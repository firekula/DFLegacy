# Launcher startup recovery implementation plan

> **For agentic workers:** Use superpowers:executing-plans with TDD and one final whole-branch review.

**Goal:** Make the user's ACT1 client launch reliably, expose registration, diagnose early exits, and verify actual gameplay startup before commit/push.

**Architecture:** Preserve independent Manager/Server and existing WPF Launcher. Restore the client's built-in string extraction only when an external override is absent; reject the known unsupported packed bootstrap before starting it, then watch the real child for startup exits. User client resources remain private and unchanged; local compatible EXE replacement is backed up explicitly.

**Tech Stack:** .NET10/WPF, x86 C++/Win32, real process tests, Gitea Actions.

**Authorization:** User explicitly requested analysis, self-testing, fixing, then commit/push using G:/dnf_local/DNF_2008; overnight acceptance. Implementation choices below are the builder's decisions within that scope.

## Evidence and constraints

- Source base d69e5dd, isolated existing linked worktree, branch codex/launcher-startup-recovery; protect main's existing3 dirty hashes/163 status records and all user saves.
- Original client SHA25635911F29C62197840403A60CF14B10D03FC608A6E782535F54F97448ED4A8D49, packed bootstrap EP0xA8D17B. Windows records repeated0xC0000005 before ijl15 loads. Bounded native trace confirms its private loader reads uncommitted kernel32 image gap RVA0x3240 (current .text begins0x10000).
- Same resources + local verified unfolded ACT1 executable SHA2566B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5 progress to missing-string fatal. Controlled restoration of stock extraction enters channel selection, proving a usable fallback without distributing game assets.
- No client EXE/PVF/NPK/string assets in Git or releases. Preserve originals with verified backup before the local repair; no firewall/security changes. New test path raised Windows firewall permission; do not automate it.
- Preserve CP936 external override behavior: existing invalid/unreadable files still fail; only true not-found falls back. No broad catch that masks corruption.

## Review focus

- Unreadable or malformed external override must not silently use built-in data.
- Child exit during startup, including exit0, must not be logged as successful game startup; cancellation must not orphan owned test children or expose passwords.
- Known packed layout detection must not become a file-version/hash allowlist rejecting compatible rebuilt or renamed clients.
- A visible keyboard-accessible registration entry must open the existing registration dialog and preserve current fields.
- Testing and local compatible-EXE repair must not touch original PVF/NPK/saves or other running services.

### Task1: Optional external string table with native fallback

Files: external_string_table.{h,cpp}, ijl15_tests.cpp.

- [x] Add actual filesystem regression cases for missing source fallback, existing valid override, malformed/empty/locked override and missing callback; observe RED before implementation.
- [x] Add ExtractOrFallback(sourcePath,destinationPath,resourceName,builtin) and use the original0x8AA170 extractor when GetFileAttributes reports only FILE/PATH_NOT_FOUND. Keep external CP936 validation and existing write path.
- [x] Run native compatibility suite and actual isolated client without Plugin/dnf.str, then commit.

### Task2: Bootstrap preflight, real early-exit monitoring and registration

Files: new Launcher ClientBootstrapInspector.cs / ClientStartupMonitor.cs / ClientProcessLifetime.cs; Program.cs, LaunchFlow.cs, MainWindow.xaml; Launcher tests.

- [x] Add synthetic PE layout regression for the observed packed bootstrap and compatible renamed/unfolded PE; no version-based rejection. Baseline installer with empty saved credentials stays valid.
- [x] Add real test-child immediate exit0/nonzero and still-running/cancel cases; observe RED, implement bounded startup observation and actionable errors without logging launch arguments/passwords.
- [ ] Expose an explicit accessible 注册账号 button alongside existing account actions, opening the same existing dialog. Test actual rendering and activation.
- [ ] Run Launcher tests and actual single-file headless/UI startup, then commit.

### Task3: Local repair, full acceptance and integration

- [ ] Verify backup of the user's packed EXE, use the already-owned same-version validated unfolded EXE, leave PVF/NPK/config/saves unchanged except documented automatic runtime outputs and necessary patch installation.
- [ ] Run the user's actual directory with the existing ordinary playtest account; record window/protocol evidence and graceful stop of only test processes. Report any Windows permission handoff honestly.
- [ ] Run Manager100, release tools46, native13+RDSEED, full Python/native-fixture gate and required default Server smokes through final CI; don't weaken gates.
- [ ] Dispatch one fresh whole-branch review, resolve important findings RED→GREEN, preserve main user state, commit/push authorized fixes, verify CI/release as applicable, restore runner state and write acceptance report.

NPKStudio is a separate project and separate plan; its code and game asset outputs do not enter this change.

## Review fix pass and current acceptance

Fresh independent staged review: no Critical; cancellation ownership Important fixed after runtime RED4 assertions (pre-cancel preflight/creation and actual child remains alive), then GREEN actual ownership tests including retained successful no-relay handoff and termination on aborted/non-handoff lifetimes. Root also addressed the reported native test temporary-path conversion defect after a real Chinese TEMP run reproduced failure: CP_ACP conversion now matches actual filesystem paths; corrected-working-directory Unicode suite GREEN with no FAIL lines. No deferred review findings.

Actual native13/13 and complete Launcher suite GREEN. Actual self-contained launcher exited0 and game window remained alive, both in isolation and repaired G:/dnf_local/DNF_2008; packed original now rejects with exit1 before spawning. Original425NPK+PVF426hashes and Config.ini/launcher.json remained identical. Original EXE/launcher/DLL/config backup verified. Windows firewall dialog was not acted on; physical registration keyboard/mouse and deeper gameplay remain manual handoff, not falsely claimed passed. Full CI/publication follows verified source commit.
