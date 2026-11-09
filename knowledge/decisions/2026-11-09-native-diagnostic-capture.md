# Task 3 original runtime evidence — 2026-11-09

The reproducible `tools/debug/crash_capture/tests/probe_original_game.py` and test-only `original_game_host`/`original_game_probe` created and ran only a new copy at `G:/dnf_local/DFLegacy_DiagnosticProbe_20261109_01/game`. The source `G:/dnf_local/DNF_2008`, prior bootstrap fixtures, other worktrees and online services were not changed or run. No gameplay server or account/password was used. The probe's entry gate required successful native initialization and verified original hooks, and the trigger required actual BugTrap activity=5 before faulting. Original DLLs are not committed or release assets.

Copied hashes match prior static reconnaissance:

| File | SHA256 |
|---|---|
| DNF.exe | 6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5 |
| BugTrap.dll | be56494fefcd27a4cfa9caa668f8ffe6e6bbd92c9ddb83eabf5b0e14b12a8875 |
| DumpReport.dll | f9962904a1d8c6c09c7df5186dc36e46795108d4cf54e0481c592616897afccb |

Observed original process PID 53480, creation UTC ticks 639297884495070499. Registration log:

```
native-initialized=1 original-hooks=1
registration=DNF.exe+0x521f2c
registration=DumpReport.dll+0x10d0
registration=BugTrap.dll+0x11ce1
legacy-activity=5; controlled-own-process-access-violation
```

Original chain exited with 0xffffffff. Exactly one confirmed `original-exception-context` record contains exception `0xC0000005`, `original_game_probe.dll+0x1010`, and module-relative frames. The local minidump is 53,238 bytes. Result, log and raw dump are only under the isolated G drive probe directory; the JSON result includes transient local capabilities and is not an upload artifact. The deployed DTO remains free of raw exception pointers/registers/paths/dumps.

The controlled fault is in a probe thread inside the actual copied DNF process, after its real original registrations. It is not the reported historical combat fault. Production suppression deliberately prevents BugTrap's old send/report UI, so this run does not establish the historical CrashSender window's identity. Separate owned-window tests prove caption evidence remains only window evidence and cannot turn a blocked/recovering original fault into a confirmed crash.

Additional original ABI proof from the supplied binary: `BT_SetActivityType` at RVA103c8 is `mov eax,[esp+4]; mov [module+36098],eax; ret 4`. Original dispatch at RVA1367a decrements and branches only for activities1,3,4; activity5 falls through without that UI/mail/network dispatch. Tests call the actual later setter with4 and observe getter5, fault through the original filter, and confirm a loopback receiver gets only a separate ordinary game socket probe. Test safety guards disable original network APIs in legacy-baseline module fixtures; the production-privacy fixture instead uses a controlled loopback receiver and the production setter guard.

Commands:

```
python tools/debug/crash_capture/tests/probe_original_game.py G:/dnf_local/DNF_2008 C:/codex-build/client-diagnostics-20261109/native/Release G:/dnf_local/DFLegacy_DiagnosticProbe_20261109_01
python src/DFLegacy.Ijl15/tests/test_native_capture.py C:/codex-build/client-diagnostics-20261109/native/Release/native_incident_tests.exe C:/codex-build/client-diagnostics-20261109/native/Release/native_capture_helper_fixture.exe G:/dnf_local/DNF_2008
```

The first script requires a new destination; choose another numbered `G:/dnf_local/DFLegacy_DiagnosticProbe_*` for reruns. Current tests inject a fixture-only root resolver into the helper so retention never scans the real user's capture history. The production helper has no root override.

Review lifecycle corrections: native capture owns a sequence rather than a boolean busy flag, so a durably acknowledged recovery rearms even after the bounded wait expired. Every collector slot resets cached incident data and flags, including final-only observations. Real two-fault regressions use a recovered AV followed by UD2, with a six-second previous-handler delay or a verified own-collector pause. Changing the next-start dump preference retains a live game's registration and original setting; cleanup rereads PID/creation binding and never removes that live registration. The actual launcher setting-save regression waits 7.5 seconds, faults the same bound child, checks its local dump/basic event, and verifies the next session adopts the saved setting. The checked-in Win32 project now compiles the native capture and Detours dependency as well as the CMake path.

Focused review verification (2026-11-09): six affected native cases passed (`--modes recover block-recover previous-block dialog delayed-recovery-twice skip-provisional`), diagnostics `--native-integration-only` passed 13 assertions, launcher `--native-diagnostic-setting-tests <native-Release-directory>` passed 9 assertions, and both Release Win32 MSBuild and CMake production targets built successfully. These regressions were observed failing before the corresponding fixes. The original-process evidence and its limitations above remain unchanged.

Primary references checked during implementation: [SetUnhandledExceptionFilter](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-setunhandledexceptionfilter), [MiniDumpWriteDump](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump), [SetThreadStackGuarantee](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadstackguarantee). Binary ABI/signature conclusions above come from the supplied original module and actual child-process tests, not assumed upstream callback ABIs.
