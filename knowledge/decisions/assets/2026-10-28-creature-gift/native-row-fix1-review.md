# Independent scoped native row-price fix1 review

**Verdict: APPROVE. Critical: none. Important: none. Minor: none in this bounded fix diff. New native acceptance remains PENDING and root-owned.**

Reviewed the two-file working-tree delta against `c43b1f89f06bf862f065b75a9da7dc0863aa1694`: `src/DFLegacy.Ijl15/src/creature_gift_row.cpp` and `src/DFLegacy.Ijl15/tests/creature_gift_row_tests.cpp`. This reviewer did not author this product fix. Preparing the separate private v3 continuation harness does not make that harness evidence of product correctness or native acceptance. No builds, test execution, live process/UI operations, deployment, product edits, configuration/state writes or commits were performed in this review.

## Corrected finding and evidence chain

The original C79 native run is ENTRY_RED. Its earlier synthetic record constructor duplicated the helper's wrong cash-at+32 assumption, so passing tests did not establish the native field mapping. The earlier review's description of correct cash guarding was therefore incorrect for C79. This review supersedes that field-mapping conclusion; original reports, failed run and C79 bytes remain preserved.

The fix has independent evidence beyond a changed test constructor:

1. Parsed the three literal `records[][84]` arrays from the new test source and compared every byte with `record_hex` in the frozen PID52752 shop snapshot. All three records match exactly, including the original pointer bytes. Their commodity/item pairs are 105010/63013, 105009/63007, 105008/63006; the three price dwords at +32/+36/+40 are respectively `(0,0,9900)`, `(0,0,6600)`, `(0,0,9900)`. Snapshot labels `cash`/`alternate2` retain the old mistaken decoder names; the raw bytes and corrected offsets are authoritative here.
2. The actual test copies each entire literal into its row, then relocates **only** +80 to a readable local item. It calls production `UpdateRow`, checks button+17 becomes1, and checks the record remains byte-identical to its relocated baseline. The synthetic shop/control association supplies context; it does not synthesize the price fields. The separate swapped-layout case puts6600 at+32 and0 at+40, starts enabled, and requires a clear to0.
3. Independently read the original on-disk EXE, SHA256 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5`, and mapped its PE sections. All125 instruction-byte rows in the frozen `offline-field-provenance.txt` match the actual EXE bytes. Parser72E6F0's creature branch uses record base EBP-23C and writes successive numeric price fields at EBP-21C/-218/-214, establishing offsets32/36/40. The full parser branch and record registration were read, rather than inferring a field from a single instruction.
4. The full original price consumer729F30 reads `[ESI+28h]` in both direct and adjusted paths. Independently disassembling the original checkout from its function boundary confirms call729F30 at869193, accumulation into EBP-8, and subtraction of that total from the B75A44 cash balance at869248. The materialized-price refresh85E560 also writes record+40, while writing the other two fields at+32/+36. Thus the consumer and checkout identify the meaning, not merely the parser's field order.
5. Independently decoded the scoped original CP936 `Fixtures/CreatureGift/etc/newcashshop.etc`. All15 fixed tuples match the original columns `commodity/item/quantity/0/0/cash`. No newly inferred subtype, lifetime or materialized-object business offset is added.

## Scope and counterexamples

Product changes only move the zero-price guards to+32/+36 and the exact tuple cash comparison to+40, with a provenance comment. Nonzero either alternative price, zero/wrong cash, the old swapped layout, wrong commodity/item/quantity, and disallowed or stale metadata remain rejected. Tests now mutate wrong cash at+40 and alternate values at+32/+36; the15tuple and23negative cases remain, alongside the independent three real records.

The current six-row association, category/subtab, FF attribute, quantity, sticker/restriction/gift-ban, readable item check and server-authoritative policy are unchanged. The correction does not broaden the fixed15tuple policy or promise support for modified-PVF lifetime/sealing rules. An unexpectedly changed or discounted materialized price outside the exact tuple remains conservatively disabled rather than admitted by another currency column.

The production updater/button-write, emitted stub, installer and rollback tail are unchanged from c43. The other six integration files show no Git diff against c43. Existing unreadable-record SEH coverage, stale-row cases, actual emitted-stub register/ESP/EFLAGS/DF execution, signature mismatch, idempotence, foreign-hook and rollback tests remain intact. No new asynchronous callback, pointer traversal, write target, hook span or calling convention is introduced. Prior reconnect, basket and MTU integration is outside the two-file change.

## Frozen identity and recorded validation

Independently recomputed all28 hashes in `native-row-fix1-freeze-manifest.json`: 11 source/build/artifact entries, 6 old-output fences, 11 evidence files. All match.

- New isolated DLL and test-directory DLL both: `30AD9A4A3CD565DE845F74AB3D6C6D60EED1F3429EC7D53929D912F158246584`.
- New test EXE: `0AD1F2A2288A0C2399864658BDCF3FCD91C83657AB54DCCC7C309D5026C9357B`.
- Both new DLLs independently parse as x86 PE machine014C and retain the six expected IJL exports.
- All six old output files retain their frozen hashes: C79 DLL, C79 test DLL and coverage test EXE; default743 DLL, old743 test DLL and old test EXE. No old artifact was repurposed.
- Scoped `git diff --check` returned clean; source/test diff against c43 contains exactly the two expected paths.

Inspected author execution records: RED contains exactly the three real-record failures and the swapped-layout rejection failure; focused log reports `creature gift row tests passed`; full log reports `ijl15 compatibility tests passed`; test/DLL build tails both record0warnings/0errors. These are frozen author-run results, **not reviewer reruns**. The stated RED is a behavioral failure against the prior implementation, not a compile failure. The fresh source/artifact/evidence hashes bind the supplied fix1 validation package.

## Acceptance boundary

Approval is for the bounded price-field correction and its frozen isolated30AD artifact. It does not convert C79 ENTRY_RED into GREEN and does not assert that30AD is deployed or loaded. Root still needs its new namespace and actual eligible row Gift→original UI112→CMD146→confirmation→CMD67 path, then the agreed canonical debit/quota/mail/claim/hatch/persistence evidence and bounded shop/ordinary-channel/PvP regressions. The v3 harness remains separately reviewed orchestration, not a substitute for any of those observations.
