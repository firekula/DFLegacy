# Stable native startup entry

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
worker validates and repairs the complete installation before opening that UI.

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
