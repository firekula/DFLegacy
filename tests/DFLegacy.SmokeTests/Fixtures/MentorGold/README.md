# Mentor master gold evidence fixture

`etc/goldlimitbylevel.etc` is the unmodified decrypted entry from the ACT1
`G:/Game/DNF_ACT1/DF/Script.pvf`, retaining original CP936 bytes. It is not
the UTF-8 recon export. `member_cnt_config.tbl` retains the bytes of the
original Monitor table copied into the approved recon evidence. The manifest
records archive, entry, table, and original machine-code evidence hashes.

`math-oracle.json` is independently derived by `generate_oracle.py`, without
loading any server assembly. Its 230 rate rows cover every relationship
level 1..10 and every connected count 1..capacity for capacities 5, 8, 10.
The oracle decodes the native binary64 constants as exact rational values,
rounds each Monitor float32 store with integer ties-to-even arithmetic,
and floors the exact rational batch product. Its 24 integer-boundary rows
include eight examples where a final binary64 multiplication rounds upward
before integer conversion and therefore yields one extra gold.

The 301 alternate timer rows represent all first rand31 samples 0..300
for `get_rand_int(300)`. They preserve signed32 LCG wrapping, signed
division truncation, bit masks/XOR, and the final unsigned comparison.
Ordinary branch draws and the permitted delay endpoint 900 seconds are
tested separately. The fixture does not claim an original rand sequence
or native gameplay acceptance.

Regenerate from the repository root with:

```powershell
python tests/DFLegacy.SmokeTests/Fixtures/MentorGold/generate_oracle.py
```

Original PVF/table entries must retain exact bytes in Git. Derived JSON
and Python hashes are checked after CRLF normalization on Windows.
