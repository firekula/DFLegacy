# Manual U04 trace decoder format fix

This is a new offline evidence helper copied from the frozen Y decoder. Y and both copied raw logs are unchanged. No product source, live process/client, input, configuration, build or deployment was touched. No authentication bytes or raw hex are exported.

The old parser accepted TCP/TCP-CONNECT only. Every old `unparsed_rawline` in these copies was a valid UDP envelope using the same `->` arrow: A20428 and B20432, starting at line83. There was no additional unknown prefix. The new parser recognizes UDP and explicitly ignores it before the TCP7001 stream filter, with `ignored_transport_records`. Other remote TCP is still ignored separately. Target TCP frame parsing, length/CRC/bounds checking, stream taint and no-resynchronization behavior are unchanged.

TDD: the frozen17 tests pass; two added UDP-interleaving/damaged-target-TCP tests first fail, then all19 pass (`tests-red.log`, `tests-green.log`). A UDP datagram aimed at port7001 cannot become a TCP frame. Broken target TCP still invalidates its stream and a later plausible frame is not recovered by scanning.

Actual copied inputs:

| Side / PID | Raw lines | SHA256 | Result | UDP ignored | Other TCP ignored |
|---|---:|---|---|---:|---:|
| A /33324 |20661|98E5501ACAC02F4E75F793602311A5A942D35BCFC475B769FF298CE0AB8504B1|complete,35 selected,0 errors,0 pending|20428|72|
| B /14376 |20681|618BDCC5C97AB8ED117FCC39B8F7EE0B61ED69DD11B0A0549D1FE16371B82EF9|complete,35 selected,0 errors,0 pending|20432|72|

Both new reports preserve the exact35 old selected frames, including original 1-based line spans, frame indices and timestamps. `complete` is parse completeness, not automated business acceptance. `manual-business-association.json` carries the full whitelisted business fields and provenance.

Observed manual command chains (raw line references refer to each side's original copied log):

| Ground | CMD50 source, slot/count; ACK1 | ItemId; NOTI40 A/B | NOTI39 pickup | NOTI86 removal A/B |
|---:|---|---|---|---|
|2|B5047, slot14/count1; B5051|33000; A5037/B5051|A5265, winner UID1, slot16|A5265/B5276|
|3|B5576, slot0/count10; B5577|0 (10gold); A5563/B5577|A5695/B5708, amounts[5,5,0,0]|A5695/B5708|
|4|A18368, slot43/count4; A18369|1004; A18369/B18381|B18468, winner UID2, slot43|A18455/B18468|
|5|B18648, slot55/count7; B18649|5579; A18636/B18649|A18711, winner UID1, slot43|A18711/B18723|
|6|B19021, slot76/count992; B19022|3027; A19008/B19022|A19120, winner UID1, slot77|A19120/B19133|
|7|B19046, slot75/count3; B19047|3021; A19031/B19047|A19111, winner UID1, slot76|A19111/B19125|
|8|A19402, slot77/count992; A19403|3027; A19403/B19417|B19512, winner UID2, slot103|A19497/B19512|

All7 manual CMD50 commands have successful matching ACKs. Both clients receive the same7 NOTI40 ground identities and8 subtype5 NOTI86 removals. Item NOTI39 is present on the picker side; the other side has removal. Gold Ground3 has both-side split notification, conserving10 as5+5. Ground1 has an earlier gold pickup/removal chain but no captured NOTI40, so it is not counted as a manual Drop chain.

User-reported success of four manual categories remains a human observation. This offline pass corroborates these packet chains; it does not establish the complete rarity/dice/yes/no matrix, rendered visibility, inventory persistence or reconnect behavior. Item IDs alone do not prove rarity. Equipment `add_info` is not generally a count; the count column above comes from CMD50.

Reproduce on these immutable copies:

```
python -B -m unittest test_decode_netdump -v
python -B decode_netdump.py ../../preflight/manual-A-netdump.log --expected-pid 33324 --output manual-A-business-fixed.json
python -B decode_netdump.py ../../preflight/manual-B-netdump.log --expected-pid 14376 --output manual-B-business-fixed.json
```

CLI logs record the parse results; frozen original Y usage remains available in its unchanged README.
