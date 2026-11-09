# Stable native startup entry

Release note: v0.1.9 deliberately reuses the published v0.1.8 entry bytes. The
`startup.dat` fast path described below is a source experiment, not part of the
v0.1.9 binary delivery. Its managed updater still reuses validated receipts.

`DFLegacy.Launcher.exe` is the fixed shortcut target. It has a static C++ runtime,
uses the existing launcher icon, and needs no .NET runtime or external client
assets to validate and start the separate updater. The managed application is
`DFLegacy.Launcher.App.exe`; the managed worker is `DFLegacy.Updater.exe`.

Build with Visual Studio C++ tools and CMake 3.24 or newer:

```powershell
./src/DFLegacy.Startup/build-native.ps1 -Architecture x86 -RunTests -BuildDirectory C:/build/DFLegacy-startup/x86
./src/DFLegacy.Startup/build-native.ps1 -Architecture x64 -RunTests -BuildDirectory C:/build/DFLegacy-startup/x64
```

Set `CMAKE_EXE` if CMake is neither in PATH nor included in Visual Studio. The
output is `<BuildDirectory>/Release/DFLegacy.Launcher.exe`. The build script does
not install tools, remove existing directories, or publish artifacts. Its default
build directory is under `%TEMP%`; MSBuild may emit MSB8029 for that location.

## Contract

The entry accepts the exact five-line ASCII descriptor in the design document:
`[Bootstrap]`, `SchemaVersion=1`, `Architecture=x86|x64`, `LauncherSha256`, and
`UpdaterSha256`. The four keys may be reordered. Hashes are lowercase SHA256,
length is at most 4096 bytes, and LF/CRLF with an optional final newline is
accepted. BOMs, blank lines, unknown/duplicate fields and incompatible
architectures are rejected. It checks the actual updater PE architecture as
well as its hash. The UI hash is passed on as recovery metadata; the managed
worker validates and repairs the complete installation before opening that UI
on the recovery path.

After a successful worker run the entry writes a separate `startup.dat`, protected
with Windows DPAPI for the current user. It binds the absolute installation root,
process architecture, and file IDs, lengths, creation and write times of the app,
updater, descriptor, `installed.json` and `validated.json`. Normal startup reads
this small receipt and file attributes, then opens the app directly using the
existing readiness event. It does not hash or copy executables or game resources.
The managed app checks its completed V4 receipt and critical EXE/DLL attributes,
then discovers new releases in the background while login remains visible.

Missing or invalid receipts, changed identities, missing critical executables,
either pending transaction journal, or an app which exits before acknowledging
readiness return to the verified worker path. An unconfirmed child is terminated
and its exit confirmed before recovery starts. Explicit bootstrap rechecks also
use the worker. Headless commands wait for their actual exit and are never retried
after completion. DPAPI receipt creation failure leaves successful UI startup
intact; the next launch uses full verification again.

The V4 completion record is flushed and replaced atomically only after complete
post-install verification and receipt commit; a pending journal always overrides
it, and rollback invalidates it. Old installations verify once before creating
the completion record. Ordinary startup intentionally trusts resource state
between installations; attribute-preserving modifications are outside this fast
path's guarantee. Explicit repair and actual game-launch validation retain full
content checks. Offline or current-version results do not create modal notices.
The independent Diagnostics.exe authorization check still hashes that helper
before executing it; it does not scan the installation's resource contents.

This new native entry has different bytes from the historical `.3`/`.4` entry.
Those versions cannot receive it through ordinary V4 updates: the existing
publisher and runtime stable-entry guards remain enforced. Deliver a new full
starter or an explicit entry migration before claiming this native fast path is
deployed to those installations.

All installation and temporary paths must be absolute local drive paths.
Directory components and files reject reparse points; executable, descriptor,
lock and receipt handles also reject hard links. Held directory handles prevent
directory renaming while bootstrap runs. Hashing and copying use held file
handles that deny writes/deletes. The installed worker handle is closed before
starting its temporary copy so that updater self-replacement remains possible.

An exclusive `.dflegacy/bootstrap/entry.lock` covers the entire operation.
An existing `app.lock` owner suppresses duplicate startup. Busy startup exits 0
without launching a child. Arguments are quoted according to the Windows CRT
rules and forwarded as `--bootstrap-client <absolute root> -- <original args>`.
The working directory is the client root. Only fixed event labels are written
to the bounded 16 KiB `bootstrap.log`; arguments and paths are never logged.

The primary is cached as `.dflegacy/bootstrap/<UpdaterSha256>.exe` before
execution. `recovery.ini` is atomically replaced only after exit 0, using the
descriptor of the worker that actually ran. A replacement installed by that
worker therefore does not become recovery until its own successful start.
Exit 2 cancels without fallback. Exit 1 permits at most one different previously
verified recovery worker. Updater exit 3 means a managed CLI command finished
with failure: the native entry returns 1 without retrying the command. Other
unexpected worker exit codes also fail without fallback.
A receipt commit failure after successful worker execution never retries,
because the successful worker may already have opened the UI.

Native error dialogs are suppressed only by the managed CLI's exact
`--no-dialog`, `--third-party-notices`, `--self-test`, `--install-ijl15` and
`--check-d3d9` flags (case insensitive). Exit status and recovery validation are
unchanged; flags such as `--headless` alone do not suppress error dialogs.

Temporary directories use random names. Cleanup only deletes the fixed copied
EXE and then removes that exact empty directory; it never recursively traverses
temporary files. Unexpected contents are left in place. The recovery receipts
are local evidence, not a signature-based trust root against replacement of all
local descriptors and executables.

## Native validation

`bootstrap_tests` runs policy assertions and actual child-process fixtures for
both architectures: descriptor grammar, hashes and PE machine checks, Unicode
and spaces, Windows quoting, primary/recovery selection, cancellation and
bounded fallback, actual fixed-entry startup, real exclusive locks, junctions
and hard links, bounded private diagnostics, updater self-replacement, cache
repair, and atomic recovery advancement/failure. Test fixtures are confined to
unique test-owned temporary roots and never contact update servers.
CLI tests also exercise terminal exit 3 without recovery retry and run the
actual fixed entry with each quiet flag to detect blocking error dialogs.

The first implementation run recorded 11 failing groups against the empty
implementation. A later real file-sharing fault exposed recovery commit failure
incorrectly triggering fallback after worker success; the regression was
observed failing before that behavior was corrected.
The CLI review regression likewise reproduced the blocking native dialog
before adding the quiet-flag policy.
