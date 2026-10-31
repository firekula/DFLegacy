# Native exporter test fixtures

These four files retain exact bytes emitted by the existing C++ test programs
through the production JSON exporters. The input states and clocks are test
models. They are not captures of a player, a running game, or performance data.
Process IDs and process-start/tick fields belong to the producer test processes.

`manifest.json` records each file's byte count and SHA-256, the native test/DLL
hashes and the native source inputs. The generator also compares freshly
installed motion-repair bytes with the bounded instruction fixture. JSON is not
reconstructed in Python. `.gitattributes` preserves raw export bytes.

After configuring the Windows Win32 CMake build, generate into a new directory:

```powershell
python tools/release/native_fixtures.py generate --build-dir artifacts/native/ijl15 --output artifacts/new-native-fixtures
python tools/release/native_fixtures.py verify artifacts/new-native-fixtures
$env:DFLEGACY_NATIVE_FIXTURE_ROOT = Join-Path $PWD 'artifacts/new-native-fixtures'
```

The generator rebuilds the producer targets and uses the CMake executable from
the validated build cache, including Visual Studio-bundled CMake. An existing
output directory is refused. To refresh checked snapshots, review the new
exports and copy only the four JSON files and manifest; keep the README.

Without an override, Python reads these checked files and verifies their hashes.
The original per-fixture `DFLEGACY_*_FIXTURE` variables still accept an explicit
native export. Missing or modified resources fail instead of skipping tests.
