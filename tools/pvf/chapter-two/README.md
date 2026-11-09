# Chapter Two semantic PVF port

`port.py` applies the verified main-branch gameplay changes to the pinned Chapter Two archive. It does not run the ACT1 exporters with relaxed guards. Python 3.12 and its standard library are sufficient.

## Reproduce

Run from the repository root. Pick an output directory that does not exist:

```powershell
python -B tools/pvf/chapter-two/test_port.py
python -B tools/pvf/chapter-two/port.py `
  --source 'G:/Project/DNF2-isolated-followup/DNFS1C2/Script.pvf' `
  --main-evidence-pvf 'G:/dnf_local/PVF-exports/Script-act1-20261109-seria-materials-priced.pvf' `
  --output 'G:/Project/DNF2-isolated-followup/MainSync-20261109/pvf/new-export'
```

The source is fixed to SHA256 `0cb607b9502084ecb9e8cd45a67087e1e96f58f4ac5420d3fc673adc73788ae0`, UUID directory format version 42831, and 69,398 unique paths. Its one duplicate numeric file ID is intentional and preserved. The ACT1 evidence is fixed to released SHA256 `69aa1a0338408fbcc3eff381924311ba42119a2cd2db4e8ffd2fab369bb9b34b`. Neither input is modified. Unknown sources, existing output directories, unexpected IDs, paths, Chinese string mappings, prices, or layouts fail closed.

`quest-recovery.json` is portable evidence derived from the original ACT1 audit: original-language text, historical Chinese replacement text, field/key references, and canonical source quest fingerprints. It includes provenance and the original evidence file hash. Applying each key requires the Chapter Two quest ID/path, gameplay script, Chinese placeholder, original Korean text, and released ACT1 replacement all to match. A shared key is preserved if any known referencing field fails.

## Generated outputs

- `Script.chapter-two.native.pvf`: native UUID header and version retained.
- `Script.chapter-two.server.pvf`: `DNF_SCRIPT_PACK` directory projection, identical encrypted body and every decoded entry.
- `verification.json`: inputs, outputs, complete change allowlist, per-feature inventory, skipped evidence, and validation results.

The export checks the file-ID-seeded CRC of every original Chapter Two entry and every generated entry. PVFStudio's released ACT1 evidence uses a different entry-key convention, so the immutable ACT1 archive is authenticated by its full pinned SHA256 instead. The two new archives are reopened and all 69,399 entries are compared to the original or the explicit changed bytes; input identities are rechecked afterward. Existing entry IDs, paths, and all unrelated content are preserved. One new disabled-offer list is added.

## Ported behavior and version boundaries

| Area | Chapter Two result |
| --- | --- |
| Quest text | 122 proven original Chinese keys restored. 121 referencing fields are preserved: 117 have changed original Korean text, four have changed quest scripts (IDs 375, 380, 385). Shared-key checks mean these counts are fields, not disjoint key counts. |
| Retired offers | 16 explicitly retired quest IDs hidden for new offers only. Original definitions, rewards, completion mappings, and existing progress remain. |
| Seasonal offers | ACT1's 18 seasonal disables are preserved because Chapter Two material-source absence is unproven. Some materials also become available through the requested Seria expansion; a missing-source decision must be reevaluated for this version. |
| Albert | Own NPC 27 remains; mage 57/21/22/23/24/65 and priest 47/48/30/41 are added to the original menu. Own levels, prerequisite rules, and SP base costs remain. Stealing uses 200% SP, +4 required character levels, and the verified per-growth caps. |
| Light sword | Skill 33 growth caps and steal metadata match the main repair. The existing swordman Albert menu is preserved. |
| Invitations | All seven bosses have exactly one item 3213 at 5000/10000. Six were already correct; only Evil Eye changes from 7000 to 5000. |
| PvP | Nine public server groups have channels 51/52/53/55 and ranges 0–9/10–19/20–30/0–30. Own maximum grade 30 is checked. All other channel data is retained. |
| Seria | Original 105 slots retained, including the initial item 7089 row. All 253 own materials with positive configured prices appended; 77 zero/missing-price materials excluded. Seven-column padding gives 364 slots. All 330 material names resolve through the own Chinese mappings. |

Main-only earlier custom `void.skl`/`fastheal.skl` policies are not copied over the own Chapter Two definitions. The ten added Albert skills and light-sword repair have explicit own-data guards. Native UI, actual skill casting, shop scrolling/purchases, and PvP entry have not been validated in-game by this exporter.

## Verified 2026-11-09 output

Two fresh exports produced identical binaries:

```text
native fb0eef35483852e0689d037fa6b5018b750940b0e35fd8a73fd35395c0396144
server 001899b2cf50d501906cf6764884655309a38ba6b9924ccc4c883833d4279af6
body   f853957d3f4fa38a79402b266fc9ed04b8398bf2d3dfbcd5862996001ffc724f
```

`G:/Project/DNF2-isolated-followup/MainSync-20261109/pvf/export-repeat/` contains the final outputs and detailed report. `export-repeat.log`, `tests-red.log`, `tests-final.log`, `negative-guards.log`, and `independent-conversion.log` are alongside it. The preexisting `G:/Game/DNF_ACT1/S1C2-78687eb-lab/Convert-S1C2Pvf.py` independently projected the generated native archive and produced the identical server SHA256.
