# Portable ACT1 town navigation scripts

`catalog.pvf` contains only the 35 original town-list, town-definition, and
town-map script payloads needed to parse the original town catalog: 48,907
decoded bytes describing 29 areas. It contains no executable, textures, audio,
save files, account data, or unrelated game assets. The full source PVF is not
included.

The read-only source was `Script.pvf`, 65,951,563 bytes, with SHA256
`288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f`.
Every script payload was extracted as its exact decrypted original bytes,
including comments, CP949 text, and line endings. `manifest.json` records each
payload's byte count, SHA256, and absolute byte offset within the original PVF.

The scripts are repacked in sorted virtual-path order into a deterministic
`DNF_SCRIPT_PACK` envelope using the normal word encryption. The derived file is
50,635 bytes with SHA256
`d2f0a13f4d702bcfcca38dde080e197134252029f31c5a4df50ec86851cfd01e`.
This is its actual archive identity, separate from the original PVF identity.
The loader pins both identities and verifies the manifest, file sizes, virtual
script set, every decoded payload, and filesystem path containment. The manifest
is limited to 32 KiB, and the archive plus manifest to 256 KiB. Missing or
corrupted files fail tests; they never cause a skip.

The two original regressions in `tools/debug/test_town_navigation.py` use this
fixture by default, regardless of whether a game is installed. Their portal,
multi-town routing, half-open geometry, native steering, and tolerant replanning
assertions remain intact. They continue through the production encrypted PVF,
CP949 text, script, and geometry parser. The other parser corruption and
synthetic geometry tests in that module remain independent.

For an explicit integration check against the complete original PVF, set
`DFLEGACY_TOWN_ORIGINAL_PVF` to its path. The loader requires the exact source
SHA256 before parsing it. `DFLEGACY_TOWN_FIXTURE_ROOT` selects an alternate
verified fixture directory. Neither setting permits a missing input to skip.

To reproduce the two generated files from a read-only verified source, choose
an explicit output directory that does not exist:

```powershell
python -m tools.acceptance.town_fixture --original-pvf 'G:/Game/DNF_ACT1/DF/Script.pvf' --output .tmp/act1-town-navigation-reproduced
python -m pytest tools/acceptance/tests/test_town_fixture.py tools/debug/test_town_navigation.py -q
```

The generator rejects an existing output before reading the source, then checks
the full original hash before creating output. It reads only the declared
scripts, writes `manifest.json` and `catalog.pvf` into a temporary sibling
directory, and renames the complete bundle into the chosen destination. Failed
validation or writes leave no partial destination. It never modifies an existing
fixture, the source installation, or runs a game client.
