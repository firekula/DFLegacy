# Manager export progress and runtime logs

Goal: show resource validation, compression, finalization and download progress;
keep management requests responsive and restore recent logs after restart.

Use an authenticated background export job and poll its bounded status. Keep the
legacy export route compatible. Serve completed ZIPs separately with range support.
Generation progress uses actual file bytes; transfer progress is shown by the browser download manager.
Do not resolve deployment domains while packaging. Keep existing archive validation.

- [x] Reproduce log loss on restart with a failing recovery test.
- [x] Add exporter progress callbacks and background job lifecycle tests.
- [x] Add authenticated job/status/download routes, preserving CSRF checks.
- [x] Restore bounded runtime log history and write manager/export lifecycle events.
- [x] Show generation stages and hand off downloads to the browser; prevent overlapping polls.
- [x] Verify Manager tests, HTTP authorization, JS syntax and browser behavior.

Work in the existing checkout; unrelated research files must remain untouched.

Verification: Manager and authenticated HTTP tests pass, including 32 MiB real
compression with concurrent health/status/log requests and an unread streaming
download. Five Node browser-flow tests pass. Local browser acceptance generated
and downloaded a 25.3 MiB fixture ZIP, displayed stage logs and a retry link.
Code review found no remaining must-fix issues after extending mutation timeouts.
No production server or published image was changed.
