# Standalone client acceptance

The executable fixtures below exercise the production bootstrap, launcher and
updater. Synthetic game/resource files stay in owned temporary directories. No
original game binaries or user account data belong in the repository.

| Contract | Executable coverage |
| --- | --- |
| Original EXE/IJL/PVF/NPK unchanged; Unicode and spaced paths | Native `bootstrap_*` CTests and `BootstrapManagedTests.Run`: hash snapshots around the actual owned child/helper handoff, raw WinMain data, canonical loaded-module identity, cancellation and commit ownership |
| Ordinary navigation without diagnostics; one hook owner | `player_runtime` CTest: frame callbacks with normal mode and FPS disabled, repeated diagnostic attach/shutdown, ABI version/size rejection; `debug_client-navigation` and `debug_town-route` cover navigation behavior |
| Retained debug DLL does not enable normal mode | Managed bootstrap fixture inspects the owned child module list; native player-runtime fixture rejects stale input opt-in |
| Manual migration | `StandaloneTests.Run`: cancellation, misleading backup, locked replacement, exact old-patch backup and trusted original identity; `LaunchUiTests` covers visible consent and cancellation lifecycle |
| V2 install and rollback | `UpdateV2Tests.Run`: exact normal/test packages, stale or missing debug rejection, nine real sharing-violation boundaries and eleven simulated durable power-loss boundaries |
| Player files and logs survive V2 commit/recovery | `UpdateV2Tests.CheckPreserved`: both flavors retain Config.ini, launcher.json, originals, root/nested NPK resources, current/archived logs and unrelated JSON/TXT at each boundary above |
| Log migration | `StandaloneTests.Run`: locked logs, collisions, validated PID diagnostics, unrelated files, reparse paths, and unavailable log destination without root fallback |
| Actual GPU enhancement | `d3d8_presenter_tests`: ten pixel comparisons plus four hook/reset cycles, reading the current PID's `Logs/DFLegacy/render-diagnostics` receipt |

The preservation assertions catch an updater that deletes or overwrites files
outside its manifest during commit, failure cleanup or recovery. They check
known fixture bytes independently of the updater's manifest and hash helpers.
They supplement existing rollback assertions on package binaries and metadata.

## Local execution

Build the Win32 native CMake project (`src/DFLegacy.Ijl15`) in an owned build
directory. Run CTest in Release with `--output-on-failure`. GPU acceptance is
opt-in (`DFLEGACY_ENABLE_GPU_TESTS=ON`) or run `Release/d3d8_presenter_tests.exe`
directly once. Coordinate desktop ownership before GPU, overlay or WPF tests.

Build/run `src/DFLegacy.Launcher/tests` in Release with `BuildNativeAssets=false`
and `Ijl15PrebuiltPath`, `ClientPatchPrebuiltPath`, `ClientBootstrapPrebuiltPath`
pointing to the corresponding Release native outputs. The full suite requires:

- `DFL_NATIVE_FIXTURES`: the CMake build directory, including the original/patch
  synthetic fixture targets.
- `DFL_LEGACY_PATCH_ARCHIVE`: the immutable prior public release ZIP containing
  legacy project patch `ijl15.dll`, whose expected SHA-256 is
  `f73618ba6bdca6483ef12e5eb4bee7e621cdbfd910b5bcac7fe1743078c28cc7`.
- Owned `TEMP`/`TMP` locations. Do not point fixtures at a real client directory.

No arguments runs the full Launcher suite. `--update-v2-tests` runs the focused
update/preservation cases. `--standalone-tests` runs installation, archival,
migration and actual helper integration. Full-suite evidence is required after
changes affecting their combined behavior.

For Python acceptance, put repository `tools` on `PYTHONPATH`, point both
`DFLEGACY_NATIVE_LOOP_DLL` and `DFLEGACY_GAMEPLAY_PROBE_DLL` at the built client
patch, then run `python -m pytest tools/debug tools/acceptance/tests -q`. The six
ABI/control-flow regression files can also run against legacy `ijl15.dll`.

Fixture success is separate from real gameplay, real-original migration and
published-package acceptance. Those gates require controller-owned receipts;
a short process lifetime or synthetic frame callback is not a gameplay pass.
