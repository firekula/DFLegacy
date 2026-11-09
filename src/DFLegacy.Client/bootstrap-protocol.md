# Native client bootstrap protocol (version 1)

`DFLegacy.Bootstrap32.exe` and `DFLegacy.Client.dll` are statically linked x86
modules. Windows 10 or later is required for atomic process-to-job assignment.
The normal CMake build places both beside legacy `ijl15.dll` and optional
`debug.dll` in `artifacts/native/ijl15/<Configuration>/`. The launcher embeds
the helper and extracts it to its controlled runtime directory.

## Launcher contract

```text
DFLegacy.Bootstrap32.exe --client <absoluteExe> --patch <absoluteDll> --sha256 <64lowerhex> --mode normal|test --nonce <32lowerhex> -- <oneLauncherDataArgument>
```

Exactly these arguments, in this order, are accepted. Paths are limited to
1023 UTF-16 code units; launcher data is at most 16384 code units and is passed
as exactly one argument with Windows command-line quoting when needed (empty,
whitespace or quote characters). Whitespace-free launcher data is emitted
verbatim so the game's raw WinMain command-line parser sees the initial `99`
field without a leading quote; the executable path is always quoted. The helper never
prints launcher data. The launcher must supply redirected stdin/stdout pipes.
The game working directory is the selected executable's canonical directory.

The patch must resolve to **DFLegacy.Client.dll in that same directory**.
Canonical `.`/`..` paths which resolve to this sibling are accepted; another
directory, another basename, or an unresolved file is rejected. Both input
files remain open without write/delete sharing until initialization and commit
finish. The helper validates x86 PE characteristics and the patch SHA-256,
then records the patch volume/file index. The package/launcher independently
validates the helper and package version before invoking this interface.

The helper uses `CreateProcessW` with an explicit executable path, suspended
primary thread, restricted inherited handle list, and an atomic
`PROC_THREAD_ATTRIBUTE_JOB_LIST` containing its private kill-on-close job.
Unavailable creation attributes fail closed; there is no create/assign-job
fallback. Only the three handshake handles are inherited. In particular, the
game cannot keep the launcher's stdin write end or helper stdout alive.
No PID/attach option exists. The job and process handles refer only to this
newly created child.

Detours changes only the child's in-memory import descriptors, adding the
fixed ASCII name `DFLegacy.Client.dll`; the Windows executable-directory
search handles Unicode and spaces without ANSI conversion of an absolute
DLL path. Existing imports, including original IJL imports, are retained.
The selected sibling is locked and required before creation. The child's
actual loaded module path and file identity must match the selected file
before the helper reports ready. There is no search-path fallback accepted
as success. No game files are rewritten or restored.

Stdout contains exactly one UTF-8 JSON line, at most 8192 bytes including LF.
Success is compact JSON of this shape:

```json
{"schemaVersion":1,"event":"ready","nonce":"0123456789abcdef0123456789abcdef","pid":1234,"createdFileTime":"134000000000000000","patchPath":"C:\\client\\DFLegacy.Client.dll"}
```

`createdFileTime` is the unsigned decimal Windows FILETIME creation timestamp
from the retained child process handle. `patchPath` is the child's actual
wide module path encoded as JSON UTF-8. The launcher checks nonce, PID,
creation timestamp and file identity, then writes exactly:

```text
commit <same nonce>\n
```

Here `\n` means one LF byte, not two literal characters. CRLF, a wrong nonce,
unexpected bytes (including bytes already queued after the sole expected
line), stdin EOF before a complete acknowledgement, child exit or 15 seconds
without a complete acknowledgement all fail. A complete exact line succeeds
whether the writer stays open or closes immediately after writing it; EOF is
not required to commit. The helper tears down its owned child on failure and exits 1.
After `ready` it emits **no second JSON line**; the nonzero exit is the
failure signal. The launcher must observe **exit 0 after commit** before it
considers ownership transferred. On valid commit the helper clears its job's
kill-on-close limit, signals the entry gate, releases handles and exits 0.
The gate also has a bounded 17-second wait, so an interrupted handoff cannot
leave a permanently suspended entry gate.

Before ready, failures emit this shape and exit 1:

```json
{"schemaVersion":1,"event":"error","nonce":"0123456789abcdef0123456789abcdef","code":"initialization_failed","message":"Client patch initialization failed; this client image may be unsupported."}
```

Stable codes: `invalid_arguments`, `invalid_protocol`, `file_unavailable`,
`patch_location`, `hash_mismatch`, `invalid_image`, `file_identity`,
`handshake_failed`, `ownership_failed`, `create_failed`, `injection_failed`,
`initialization_timeout`, `initialization_failed`, `parent_disconnected`,
`module_identity`, `child_identity`, `bootstrap_failed`. Invalid/missing
nonce is reported as an empty string. Messages contain no credentials,
command line or launcher data. Initialization is limited to 10 seconds;
launcher EOF is monitored during initialization as well as after ready.
Killing the helper before handoff closes its job and terminates the child.

## Private child handshake and entry gate

`bootstrap_protocol.h` defines two fixed x86 structures and a private Detours
payload GUID. Each launch creates a fresh **unnamed** pagefile mapping and two
unnamed manual-reset events. The handles are inherited through the explicit
handle list; their 32-bit values, structure size and protocol magic are
copied into the suspended child's Detours payload. No environment variable,
filesystem marker, named event or command-line secret selects the mode.

The zero-initialized mapping carries magic, size, child PID, numeric mode
(`0=Normal`, `1=Test`), nonce, initialization state (`0` pending, `1` ready,
`-1` failed), loaded-module volume/file index and a bounded UTF-16 path.
The first event means initialization has finished, the second authorizes
execution of the original game entry point. These objects are private to
the helper/owned child, not a security boundary against code already running
with the same user's process-debug rights.

`DllMain` validates the payload/mapping, records the explicit mode, restores
Detours' temporary PE headers and installs the original entry-point detour.
It does not install gameplay hooks. The gate runs on the primary thread
after the loader releases its lock and before the original CRT/game entry.
It validates the supported image and applies gameplay patches; any failure
prevents entry. The helper terminates failures/timeouts even if an
initializer stalls. Already-patched primary patch bytes are rejected on the
standalone path; the existing legacy IJL path retains its compatibility
behavior. Unsupported/packed images fail closed rather than being rewritten.

The standalone DLL has one noname ordinal-1 Detours marker export and no IJL
codec exports. It refuses ordinary `LoadLibrary` without the private payload.
`bootstrap_runtime.h` exposes `Mode { Normal, Test }`, `GetMode() noexcept`
and `IsStandalone() noexcept`; legacy IJL returns `Normal`/`false` and keeps
its old entry behavior in `ijl15_entry.cpp`. Task 2 consumes the explicit mode
to enforce the ordinary/test runtime and diagnostic policy.

## Isolated native tests

The fixture DLL uses the exact production entry gate, handshake and helper,
but links a separate test initializer. There is no production test switch.
Its initializer creates and joins a worker that loads a system DLL, proving
that initialization runs outside the loader lock. The fixture executable
statically imports a separately built original-IJL fixture and writes its
entry marker only after successful initialization and commit. All fixture
assets are copied to unique Unicode/space paths. Tests cover failure,
timeouts, EOF, killed helper, mismatched commit, an unrelated live process,
normal/test mode, SHA-256, x64/malformed images, production rejection of an
unknown image, canonical sibling paths and unchanged EXE/IJL/PVF/NPK hashes.
They never launch game assets or send desktop input.
